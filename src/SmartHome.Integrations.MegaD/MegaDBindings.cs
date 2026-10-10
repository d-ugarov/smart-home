using Microsoft.Extensions.Options;
using SmartHome.Core.Entities;
using SmartHome.Core.Home;

namespace SmartHome.Integrations.MegaD;

public sealed record MegaDController(string Id, string Name, Uri Address, string Password, IReadOnlySet<int> HardwareInputs)
{
    public override string ToString() => Name;
}

internal sealed record PortBinding(MegaDController Controller, int Port, Entity Entity);

internal sealed record ValveBinding(MegaDController Controller, Entity Valve, int OpenInput, int CloseInput, int OpenRelay, int CloseRelay)
{
    public int InputOf(ValveCommand command) => command == ValveCommand.Open ? OpenInput : CloseInput;
}

/// <summary> Validated <see cref="MegaDOptions"/> as lookups: controller ↔ port ↔ entity. </summary>
internal sealed class MegaDBindings
{
    private readonly Dictionary<(string Controller, int Port), PortBinding> byPort = [];
    private readonly Dictionary<string, PortBinding> byEntity = [];
    private readonly Dictionary<string, ValveBinding> valves = [];
    private readonly Dictionary<(string Controller, int Port), (ValveBinding Valve, ValveCommand Command)> valveRelays = [];

    public MegaDBindings(IOptions<MegaDOptions> options, HomeModel home)
    {
        foreach (var (id, config) in options.Value.Controllers)
        {
            var controller = new MegaDController(id, config.Name, new Uri(config.Address), config.Password, config.HardwareInputs.ToHashSet());
            Controllers[id] = controller;

            foreach (var (port, entityId) in config.Ports)
            {
                var binding = new PortBinding(controller, port, home.FindEntity(entityId)!);
                byPort[(id, port)] = binding;
                byEntity[entityId] = binding;
            }

            foreach (var valve in config.Valves)
            {
                var binding = new ValveBinding(controller, home.FindEntity(valve.Entity)!, valve.OpenInput, valve.CloseInput, valve.OpenRelay, valve.CloseRelay);
                valves[valve.Entity] = binding;
                valveRelays[(id, valve.OpenRelay)] = (binding, ValveCommand.Open);
                valveRelays[(id, valve.CloseRelay)] = (binding, ValveCommand.Close);
            }
        }
    }

    public Dictionary<string, MegaDController> Controllers { get; } = [];

    public PortBinding? FindPort(MegaDController controller, int port) => byPort.GetValueOrDefault((controller.Id, port));

    public PortBinding? FindPort(Entity entity) => byEntity.GetValueOrDefault(entity.Id);

    public ValveBinding? FindValve(Entity valve) => valves.GetValueOrDefault(valve.Id);

    public (ValveBinding Valve, ValveCommand Command)? FindValveRelay(MegaDController controller, int port) =>
        valveRelays.TryGetValue((controller.Id, port), out var relay) ? relay : null;

    public MegaDController? ControllerOf(Entity entity) => FindPort(entity)?.Controller ?? FindValve(entity)?.Controller;

    public IEnumerable<Entity> EntitiesOf(MegaDController controller) =>
        byEntity.Values.Where(x => x.Controller == controller).Select(x => x.Entity)
                .Concat(valves.Values.Where(x => x.Controller == controller).Select(x => x.Valve));
}
