using System.Text.Json.Nodes;

namespace SmartHome.ConfigConverter;

internal sealed record ConversionResult(JsonObject Devices, JsonObject Entities, JsonObject Automations, IReadOnlyList<string> Notes, IReadOnlyList<string> Errors);

/// <summary>
/// The config of <c>master</c> → house files of the new server, with the same behaviour. What <c>master</c> silently skipped
/// at startup (unknown ports, rules without commands) is left out and listed in the notes; what the new server would
/// reject is an error.
/// </summary>
internal sealed class Converter(MasterConfig master, ConversionMap map, bool withPasswords)
{
    private readonly List<string> notes = [];
    private readonly List<string> errors = [];
    private readonly HashSet<string> buttons = [];
    private readonly HashSet<string> sensors = [];

    public static ConversionResult Convert(MasterConfig master, ConversionMap map, bool withPasswords) => new Converter(master, map, withPasswords).Run();

    private ConversionResult Run()
    {
        var rules = new JsonArray(master.Rules.Select(Rule).OfType<JsonNode>().ToArray());
        var menu = new JsonArray(master.Locations.Select(x => Page(x, path: null)).OfType<JsonNode>().ToArray());
        var entities = Entities();

        var devices = new JsonObject {["MegaD"] = new JsonObject {["Controllers"] = Controllers()}};
        var home = new JsonObject {["Home"] = new JsonObject {["Entities"] = entities, ["Menu"] = menu}};
        var automations = new JsonObject {["Home"] = new JsonObject {["Rules"] = rules}};

        return new ConversionResult(devices, home, automations, notes, errors);
    }

    private JsonObject? Rule(MasterRule rule, int index)
    {
        var name = $"Rule #{index + 1} ({rule.Source.Key})";
        var source = master.FindPort(rule.Source.DeviceId, rule.Source.PortId);

        if (source == null)
            return Skip($"{name}: source port is not described - master skipped the rule");

        if (!source.IsInput)
            return Skip($"{name}: source is an output - such rules never fire in master");

        if (map.HardwareInputs.GetValueOrDefault(source.DeviceId)?.Contains(int.Parse(source.Id)) == true)
            return Error($"{name}: rule on a hardware input");

        var actions = new JsonArray();
        foreach (var action in rule.Actions)
        {
            var port = master.FindPort(action.Port.DeviceId, action.Port.PortId);

            if (port is not {IsRelay: true})
            {
                notes.Add($"{name}: action on {action.Port.Key} skipped - {(port == null ? "port is not described" : "not a relay")}, master skipped it too");
                continue;
            }

            if (map.ValveOfRelay(port.DeviceId, port.Id) is { } valve)
            {
                Error($"{name}: commands relay {port.Key} of valve {valve.Id} directly");
                continue;
            }

            if (action.Command == null)
            {
                notes.Add($"{name}: action on {action.Port.Key} without a command skipped, master did nothing for it");
                continue;
            }

            var command = action.Command switch
            {
                "On" => "On",
                "Off" => "Off",
                "Switch" => "Toggle",
                _ => null,
            };

            if (command == null)
            {
                Error($"{name}: command {action.Command} is not supported");
                continue;
            }

            var json = new JsonObject {["Entity"] = Id(port), ["Command"] = command};
            if (action.DelayBeforeMs > 0)
                json["DelayBefore"] = Delay(action.DelayBeforeMs);
            if (action.DelayAfterMs > 0)
                json["DelayAfter"] = Delay(action.DelayAfterMs);

            actions.Add(json);
        }

        if (actions.Count == 0)
            return Skip($"{name}: no commands left - master skipped the rule");

        buttons.Add(source.Key);

        var result = new JsonObject
                     {
                         ["Button"] = Id(source),
                         ["Presses"] = new JsonArray(rule.Presses.Select(Press).Select(x => (JsonNode)x).ToArray()),
                     };

        if (rule.Condition != null && Condition(rule.Condition, name) is { } condition)
            result["Condition"] = condition;

        result["Actions"] = actions;
        result["Response"] = rule.Result == "DoNothing" ? "Handled" : "ControllerActs";

        if (rule.IsFinal)
            result["IsFinal"] = true;

        return result;
    }

    private JsonObject? Condition(MasterCondition condition, string name)
    {
        var switches = new List<string>();

        foreach (var portRef in condition.Ports)
        {
            var port = master.FindPort(portRef.DeviceId, portRef.PortId);
            if (port == null || port.Type != "OUT")
            {
                notes.Add($"{name}: condition port {portRef.Key} skipped - {(port == null ? "not described" : "not an output")}, master skipped it too");
                continue;
            }

            if (!port.IsRelay || map.ValveOfRelay(port.DeviceId, port.Id) != null)
            {
                Error($"{name}: condition on {port.Key}, which is not a light relay");
                continue;
            }

            switches.Add(Id(port));
        }

        if (switches.Count == 0)
        {
            notes.Add($"{name}: no condition ports left - the rule has no condition, as in master");
            return null;
        }

        // StatesAnyEqual without a status was a copy of StatesAreEqual in master: kept as it worked.
        var (type, state) = (condition.Type, condition.Status) switch
        {
            ("StatesAreEqual", null) => ("AllSame", null),
            ("StatesAreEqual", var s) => ("AllAre", s),
            ("StatesAnyEqual", null) => ("AllSame", null),
            ("StatesAnyEqual", var s) => ("AnyIs", s),
            ("StatesNotEqual", null) => ("Mixed", null),
            ("StatesNotEqual", var s) => ("NoneIs", s),
            _ => (null, null),
        };

        if (type == null)
        {
            Error($"{name}: condition {condition.Type} is not supported");
            return null;
        }

        if (condition is {Type: "StatesAnyEqual", Status: null})
            notes.Add($"{name}: StatesAnyEqual without Status worked as 'all the same' in master - converted to AllSame");

        var json = new JsonObject {["Type"] = type};
        if (state != null)
            json["State"] = state;
        json["Entities"] = new JsonArray(switches.Select(x => (JsonNode)x).ToArray());

        return json;
    }

    private JsonObject? Page(MasterLocation location, string? path)
    {
        path = path == null ? location.Name : path + ConversionMap.PagePathSeparator + location.Name;

        if (!map.Pages.TryGetValue(path, out var id))
            return Error($"Menu page '{path}': no id in the map (Pages)");

        var page = new JsonObject {["Id"] = id, ["Title"] = location.Name};

        var subPages = location.SubLocations.Select(x => Page(x, path)).OfType<JsonNode>().ToArray();
        if (subPages.Length > 0)
            page["Pages"] = new JsonArray(subPages);

        var items = new JsonArray();
        foreach (var item in location.Items)
        {
            var port = master.FindPort(item.Port.DeviceId, item.Port.PortId);
            if (port == null)
            {
                notes.Add($"Menu page '{path}': item {item.Port.Key} skipped - port is not described, master skipped it too");
                continue;
            }

            if (map.ValveOfRelay(port.DeviceId, port.Id) is { } valve)
            {
                // A valve button replaces the relay button: the controller runs the whole pulse.
                items.Add(new JsonObject
                          {
                              ["Entity"] = valve.Id,
                              ["Label"] = item.CustomName ?? port.Name,
                              ["Valve"] = valve.OpenRelay.ToString() == port.Id ? "Open" : "Close",
                          });
                continue;
            }

            if (!port.IsInput && !port.IsRelay)
            {
                Error($"Menu page '{path}': item {port.Key} is an output in mode {port.OutMode}");
                continue;
            }

            if (port.IsInput)
                sensors.Add(port.Key);

            var json = new JsonObject {["Entity"] = Id(port)};
            if (item.CustomName != null && item.CustomName != port.Name)
                json["Label"] = item.CustomName;

            items.Add(json);
        }

        if (items.Count > 0)
            page["Items"] = items;

        var lists = location.Conditions.Where(x => x is {Type: "PortOutCurrentStatus", Status: not null}).ToList();
        if (lists.Count > 1)
            Error($"Menu page '{path}': more than one list of switches");
        else if (lists.Count == 1)
            page["ListSwitches"] = lists[0].Status;

        return page;
    }

    private JsonArray Entities()
    {
        var result = new JsonArray();

        foreach (var device in master.Devices)
        {
            foreach (var port in device.Ports)
            {
                var type = port switch
                {
                    {IsRelay: true} when map.ValveOfRelay(port.DeviceId, port.Id) == null => "Switch",
                    {IsInput: true} when buttons.Contains(port.Key) => "Button",
                    {IsInput: true} when sensors.Contains(port.Key) => "Sensor",
                    _ => null,
                };

                if (type == null)
                {
                    if (map.ValveOfRelay(port.DeviceId, port.Id) == null)
                        notes.Add($"Port {port.Key} '{port.Name}' ({port.Type}) is not used by rules or the menu - not converted");
                    continue;
                }

                var entity = new JsonObject {["Id"] = Id(port), ["Name"] = port.Name, ["Type"] = type};

                if (port.Icons.TryGetValue("On", out var on) && port.Icons.TryGetValue("Off", out var off))
                    entity["Icons"] = new JsonObject {["On"] = on, ["Off"] = off};
                else if (port.Icons.Count > 0)
                    Error($"Port {port.Key}: icons for both On and Off are needed");

                result.Add(entity);
            }

            foreach (var valve in map.Valves.Where(x => x.Controller == device.Id))
                result.Add(new JsonObject {["Id"] = valve.Id, ["Name"] = valve.Name, ["Type"] = "Valve"});
        }

        return result;
    }

    private JsonObject Controllers()
    {
        var result = new JsonObject();

        foreach (var device in master.Devices)
        {
            var controller = new JsonObject {["Name"] = device.Name, ["Address"] = device.Ip};

            if (withPasswords)
                controller["Password"] = device.Password;

            if (map.HardwareInputs.TryGetValue(device.Id, out var hardwareInputs))
                controller["HardwareInputs"] = new JsonArray(hardwareInputs.Select(x => (JsonNode)x).ToArray());

            var ports = new JsonObject();
            foreach (var port in device.Ports)
            {
                if (map.Ports.TryGetValue(port.Key, out var id) && IsEntity(port))
                    ports[port.Id] = id;
            }

            controller["Ports"] = ports;

            var valves = map.Valves.Where(x => x.Controller == device.Id).ToList();
            if (valves.Count > 0)
            {
                controller["Valves"] = new JsonArray(valves.Select(x => (JsonNode)new JsonObject
                                                                                    {
                                                                                        ["Entity"] = x.Id,
                                                                                        ["OpenInput"] = x.OpenInput,
                                                                                        ["CloseInput"] = x.CloseInput,
                                                                                        ["OpenRelay"] = x.OpenRelay,
                                                                                        ["CloseRelay"] = x.CloseRelay,
                                                                                    })
                                                           .ToArray());
            }

            result[device.Id] = controller;
        }

        return result;
    }

    private bool IsEntity(MasterPort port) => port switch
    {
        {IsRelay: true} => map.ValveOfRelay(port.DeviceId, port.Id) == null,
        {IsInput: true} => buttons.Contains(port.Key) || sensors.Contains(port.Key),
        _ => false,
    };

    private string Id(MasterPort port)
    {
        if (map.Ports.TryGetValue(port.Key, out var id))
            return id;

        Error($"Port {port.Key} '{port.Name}': no entity id in the map (Ports)");
        return $"missing.p{port.DeviceId}_{port.Id}";
    }

    private static string Press(string command) => command switch
    {
        "KeyPressed" => "Press",
        "KeyReleased" => "Release",
        "Click" => "Click",
        "DoubleClick" => "DoubleClick",
        "LongClick" => "LongPress",
        _ => throw new NotSupportedException($"Press {command}"),
    };

    private static string Delay(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss\.fff");

    private JsonObject? Skip(string note)
    {
        notes.Add(note);
        return null;
    }

    private JsonObject? Error(string error)
    {
        if (!errors.Contains(error))
            errors.Add(error);
        return null;
    }
}
