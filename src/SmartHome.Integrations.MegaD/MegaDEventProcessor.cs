using Microsoft.Extensions.Logging;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Rules;
using SmartHome.Core.State;
using SmartHome.Core.Valves;

namespace SmartHome.Integrations.MegaD;

/// <summary>
/// A request of a controller. The answer tells the controller whether to run its own scenario: 200 - the server has
/// handled the press, 204 - everything else (no rules, hardware inputs, outputs, restarts, unknown requests). The one
/// exception on a hardware input: the leak sensor of a bathroom being cleaned gets 200, see <see cref="CleaningService"/>.
/// </summary>
public interface IMegaDRequestHandler
{
    /// <returns> true - answer 200, the controller does not run its scenario. </returns>
    Task<bool> ProcessAsync(string controllerId, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken);
}

internal sealed class MegaDEventProcessor(
    MegaDBindings bindings,
    InputRuleEngine rules,
    ValveService valves,
    CleaningService cleaning,
    HomeState state,
    HomeEventBus events,
    ILogger<MegaDEventProcessor> logger) : IMegaDRequestHandler
{
    /// <returns> true - answer 200, the controller does not run its scenario. </returns>
    public async Task<bool> ProcessAsync(string controllerId, IReadOnlyList<KeyValuePair<string, string>> query, CancellationToken cancellationToken)
    {
        var raw = string.Join("&", query.Select(x => $"{x.Key}={x.Value}"));

        if (!bindings.Controllers.TryGetValue(controllerId, out var controller))
        {
            logger.LogWarning("Request of an unknown controller {Controller}: {Query}", controllerId, raw);
            events.Publish(new DeviceEventReceived($"megad/{controllerId}", $"unknown controller, {raw}", DeviceEventKind.Unknown));
            return false;
        }

        var request = MegaDRequest.Parse(query);
        logger.LogDebug("{Controller}: {Request}", controller.Name, request);

        switch (request)
        {
            case MegaDRequest.Started:
                events.Publish(new DeviceRestarted(MegaDRestartHandler.DeviceId(controller), controller.Name));
                return false;

            case MegaDRequest.Port port when bindings.FindPort(controller, port.Number) is { } binding:
                return await ProcessPortAsync(binding, port, cancellationToken);

            case MegaDRequest.Port port when bindings.FindValveRelay(controller, port.Number) is var (valve, command):
                var relay = $"{valve.Valve.Name}, {command.ToString().ToLowerInvariant()} relay: {Output(port.Output)}";
                events.Publish(new DeviceEventReceived(controller.Name, relay, DeviceEventKind.LeakProtection));
                if (port.Output == OnOff.Off)
                    valves.OnPulseFinished(valve.Valve, command);
                return false;

            case MegaDRequest.Port port when controller.HardwareInputs.Contains(port.Number):
                events.Publish(new DeviceEventReceived(controller.Name, $"hardware input, {raw}", DeviceEventKind.LeakProtection));
                return false;

            default:
                events.Publish(new DeviceEventReceived(controller.Name, $"not handled, {raw}", DeviceEventKind.Regular));
                return false;
        }
    }

    private async Task<bool> ProcessPortAsync(PortBinding binding, MegaDRequest.Port port, CancellationToken cancellationToken)
    {
        var entity = binding.Entity;

        switch (entity.Type)
        {
            case EntityType.Button:
            case EntityType.Sensor:
                var press = $"{entity.Name}: {Press(port.Press)}{(port.Counter is { } cnt ? $", cnt {cnt}" : "")}";
                events.Publish(new DeviceEventReceived(binding.Controller.Name, press, Kind(binding), entity));
                return entity.Type == EntityType.Button
                    ? await rules.HandleAsync(entity, port.Press, cancellationToken)
                    : HoldsBackLeak(binding, port);

            case EntityType.Switch:
                if (port.Output is { } output)
                    state.Set(entity.Id, new OnOffValue(output), StateSource.Device);
                events.Publish(new DeviceEventReceived(binding.Controller.Name, $"{entity.Name}: {Output(port.Output)}", Kind(binding), entity));
                return false;

            default:
                return false;
        }
    }

    // The hardware inputs are the leak protection: the leak sensors and the buttons of the valves run scenarios of the controller there.
    private static DeviceEventKind Kind(PortBinding binding) =>
        binding.Controller.HardwareInputs.Contains(binding.Port) ? DeviceEventKind.LeakProtection : DeviceEventKind.Regular;

    private bool HoldsBackLeak(PortBinding sensor, MegaDRequest.Port port) =>
        port is {Press: PressKind.Press, Output: null} &&
        bindings.FindValveClosedBy(sensor.Controller, sensor.Port) is { } valve &&
        cleaning.HoldsBack(valve.Valve, sensor.Entity);

    private static string Press(PressKind press) => press switch
    {
        PressKind.Press => "press",
        PressKind.Release => "release",
        PressKind.Click => "click",
        PressKind.DoubleClick => "double click",
        PressKind.LongPress => "long press",
        _ => press.ToString(),
    };

    private static string Output(OnOff? output) => output switch
    {
        OnOff.On => "on",
        OnOff.Off => "off",
        _ => "?",
    };
}
