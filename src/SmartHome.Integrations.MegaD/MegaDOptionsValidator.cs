using Microsoft.Extensions.Options;
using SmartHome.Core.Configuration;
using SmartHome.Core.Entities;

namespace SmartHome.Integrations.MegaD;

/// <summary>
/// Startup check of the bindings against the entities of the house. Besides typos it guards the rules of the owner:
/// no buttons (so no rules) on the leak protection inputs, no direct commands to the valve relays.
/// </summary>
internal sealed class MegaDOptionsValidator(IOptions<HomeOptions> homeOptions) : IValidateOptions<MegaDOptions>
{
    public const int MaxPort = 63;

    public ValidateOptionsResult Validate(string? name, MegaDOptions options)
    {
        var errors = new List<string>();
        var entities = homeOptions.Value.Entities.Where(x => x.Type != null).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().Type!.Value);
        var bound = new Dictionary<string, string>();

        void Bind(string entityId, string where)
        {
            if (!bound.TryAdd(entityId, where))
                errors.Add($"Entity '{entityId}' is bound twice: {bound[entityId]} and {where}");
        }

        foreach (var (id, controller) in options.Controllers)
        {
            var prefix = $"MegaD controller '{id}'";

            if (string.IsNullOrWhiteSpace(controller.Name))
                errors.Add($"{prefix}: Name is required");

            if (!Uri.TryCreate(controller.Address, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttp)
                errors.Add($"{prefix}: Address must be an http URL, e.g. http://192.168.0.11");

            if (string.IsNullOrEmpty(controller.Password))
                errors.Add($"{prefix}: Password is required (a secret: appsettings of the server, not the house files)");

            var hardwareInputs = controller.HardwareInputs.ToHashSet();
            var valvePorts = new Dictionary<int, string>();

            foreach (var valve in controller.Valves)
            {
                if (!entities.TryGetValue(valve.Entity, out var type) || type != EntityType.Valve)
                    errors.Add($"{prefix}: valve '{valve.Entity}' is not a valve entity");
                else
                    Bind(valve.Entity, prefix);

                foreach (var input in new[] {valve.OpenInput, valve.CloseInput})
                {
                    if (!hardwareInputs.Contains(input))
                        errors.Add($"{prefix}: input {input} of valve '{valve.Entity}' runs a scenario of the controller and must be in HardwareInputs");
                }

                foreach (var port in new[] {valve.OpenInput, valve.CloseInput, valve.OpenRelay, valve.CloseRelay})
                {
                    if (port is < 0 or > MaxPort)
                        errors.Add($"{prefix}: port {port} of valve '{valve.Entity}' is out of range 0..{MaxPort}");
                    else if (!valvePorts.TryAdd(port, valve.Entity))
                        errors.Add($"{prefix}: port {port} is used twice by valves ('{valvePorts[port]}', '{valve.Entity}')");
                }
            }

            foreach (var (port, entityId) in controller.Ports)
            {
                var where = $"{prefix} port {port}";

                if (port is < 0 or > MaxPort)
                    errors.Add($"{where}: out of range 0..{MaxPort}");

                if (string.IsNullOrEmpty(entityId))
                {
                    errors.Add($"{where}: no entity");
                    continue;
                }

                if (!entities.TryGetValue(entityId, out var type))
                {
                    errors.Add($"{where}: unknown entity '{entityId}'");
                    continue;
                }

                Bind(entityId, where);

                switch (type)
                {
                    case EntityType.Switch when valvePorts.TryGetValue(port, out var valve):
                        errors.Add($"{where}: relay of valve '{valve}' - the server never commands it directly");
                        break;
                    case EntityType.Button when hardwareInputs.Contains(port):
                        errors.Add($"{where}: button '{entityId}' on a hardware input - rules are not allowed there");
                        break;
                    case EntityType.Sensor when valvePorts.TryGetValue(port, out var valve) && !hardwareInputs.Contains(port):
                        errors.Add($"{where}: port of valve '{valve}'");
                        break;
                    case EntityType.Valve:
                        errors.Add($"{where}: valve '{entityId}' is bound in Valves, not in Ports");
                        break;
                    case EntityType.Group:
                        errors.Add($"{where}: group '{entityId}' has no hardware, its switches are bound");
                        break;
                }
            }
        }

        foreach (var (id, type) in entities.Where(x => x.Value != EntityType.Group && !bound.ContainsKey(x.Key)))
            errors.Add($"{type} '{id}' is not bound to a MegaD port");

        // The cleaning holds back the leak sensor of the valve: the sensor on its close input.
        var withLeakSensor = options.Controllers.Values
                                    .SelectMany(c => c.Valves.Where(v => c.Ports.TryGetValue(v.CloseInput, out var e) && !string.IsNullOrEmpty(e) &&
                                                                 entities.GetValueOrDefault(e) == EntityType.Sensor))
                                    .Select(x => x.Entity)
                                    .ToHashSet();
        foreach (var valve in CleaningValves(homeOptions.Value.Menu).Where(x => !withLeakSensor.Contains(x)).Distinct())
            errors.Add($"Valve '{valve}' has a cleaning button, but no sensor is bound to its close input");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static IEnumerable<string> CleaningValves(IEnumerable<MenuPageOptions> pages) =>
        pages.SelectMany(x => x.Items.Where(i => i.Cleaning).Select(i => i.Entity).Concat(CleaningValves(x.Pages)));
}
