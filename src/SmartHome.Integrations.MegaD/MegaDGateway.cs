using Microsoft.Extensions.Logging;
using SmartHome.Core.Common;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;

namespace SmartHome.Integrations.MegaD;

internal sealed class MegaDGateway(MegaDBindings bindings, MegaDClient client, ILogger<MegaDGateway> logger) : IDeviceGateway
{
    public async Task<IReadOnlyDictionary<string, EntityValue>> ReadAsync(IReadOnlyCollection<Entity> entities, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, EntityValue>();

        // One cmd=all per controller, in the order the controllers first appear.
        foreach (var group in entities.Select(x => (Entity: x, Controller: bindings.ControllerOf(x)))
                                      .Where(x => x.Controller != null)
                                      .GroupBy(x => x.Controller!))
        {
            var read = await InvokeOperations.InvokeAsync(logger, $"Read {group.Key.Name}", () => client.ReadAllAsync(group.Key, cancellationToken));
            if (!read.IsSuccess)
                continue;

            foreach (var (entity, _) in group)
            {
                if (Parse(entity, read.Data!) is { } value)
                    result[entity.Id] = value;
            }
        }

        return result;
    }

    public Task<OperationResult> SwitchAsync(Entity entity, SwitchCommand command, CancellationToken cancellationToken)
    {
        if (entity.Type != EntityType.Switch || bindings.FindPort(entity) is not { } binding)
            return Task.FromResult(OperationResult.Failed($"{entity.Id} is not a MegaD relay"));

        var value = command switch
        {
            SwitchCommand.Off => 0,
            SwitchCommand.On => 1,
            SwitchCommand.Toggle => 2,
            _ => throw new NotSupportedException($"Command {command}"),
        };

        return InvokeOperations.InvokeAsync(logger, $"{binding.Controller.Name} {binding.Port}:{value} ({entity.Id})",
                                            () => client.SetAsync(binding.Controller, binding.Port, value, cancellationToken));
    }

    public Task<OperationResult> RunValveAsync(Entity valve, ValveCommand command, CancellationToken cancellationToken)
    {
        if (bindings.FindValve(valve) is not { } binding)
            return Task.FromResult(OperationResult.Failed($"{valve.Id} is not a MegaD valve"));

        var input = binding.InputOf(command);
        return InvokeOperations.InvokeAsync(logger, $"{binding.Controller.Name} pt={input}&cmd=d ({valve.Id} {command})",
                                            () => client.RunScenarioAsync(binding.Controller, input, cancellationToken));
    }

    private EntityValue? Parse(Entity entity, string[] ports)
    {
        if (bindings.FindValve(entity) is { } valve)
        {
            return (ParseOnOff(ports, valve.OpenRelay), ParseOnOff(ports, valve.CloseRelay)) switch
            {
                (OnOff.On, _) => new ValveValue(ValveActivity.Opening),
                (_, OnOff.On) => new ValveValue(ValveActivity.Closing),
                (OnOff.Off, OnOff.Off) => new ValveValue(ValveActivity.Idle),
                _ => null,
            };
        }

        return bindings.FindPort(entity) is { } port && ParseOnOff(ports, port.Port) is { } state
            ? new OnOffValue(state)
            : null;
    }

    /// <summary> <c>ON</c>, <c>OFF</c>; an input may carry its press counter: <c>OFF/12</c>. </summary>
    internal static OnOff? ParseOnOff(string[] ports, int port)
    {
        if (port >= ports.Length)
            return null;

        return ports[port].Split('/')[0].Trim().ToUpperInvariant() switch
        {
            "ON" => OnOff.On,
            "OFF" => OnOff.Off,
            _ => null,
        };
    }
}
