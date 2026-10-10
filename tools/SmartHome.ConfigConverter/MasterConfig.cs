using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartHome.ConfigConverter;

/// <summary> The config of <c>master</c> (DevicesConfig, HomeMapConfig, AutomationConfig), read as plain JSON. </summary>
internal sealed record MasterConfig(IReadOnlyList<MasterDevice> Devices, IReadOnlyList<MasterLocation> Locations, IReadOnlyList<MasterRule> Rules)
{
    public static readonly JsonDocumentOptions JsonOptions = new() {CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true};

    public MasterPort? FindPort(string deviceId, string portId) =>
        Devices.FirstOrDefault(x => x.Id == deviceId)?.Ports.FirstOrDefault(x => x.Id == portId);

    public static MasterConfig Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: JsonOptions)!.AsObject();

        var devices = Array(root["DevicesConfig"]?["Devices"])
                      .Select(d => new MasterDevice(Str(d, "Id")!, Str(d, "Name")!, Str(d, "Ip")!, Str(d, "Pwd"),
                                                    Array(d["DevicePorts"]).Select(p => new MasterPort(Str(d, "Id")!, Str(p, "Id")!, Str(p, "Type")!, Str(p, "Name")!,
                                                                                                       Str(p, "OutMode"), Icons(p["InOutSWModeIcons"])))
                                                                           .ToList()))
                      .ToList();

        var locations = Array(root["HomeMapConfig"]?["Locations"]).Select(Location).ToList();

        var rules = Array(root["AutomationConfig"]?["Triggers"])
                    .Select(t => new MasterRule(
                                PortRef(t["SourcePortState"]!),
                                Array(t["SourcePortState"]!["Status"]?["InPortCommands"]).Select(x => x.GetValue<string>()).ToList(),
                                t["AdditionalConditions"] is { } c
                                    ? new MasterCondition(Str(c, "Type") ?? "StatesAreEqual", Str(c, "Status"), Array(c["Ports"]).Select(PortRef).ToList())
                                    : null,
                                Array(t["DestinationPortStates"]).Select(a => new MasterAction(PortRef(a), Str(a["Action"], "SWCommand"),
                                                                                                 Int(a, "DelayBeforeActionInMilliseconds"),
                                                                                                 Int(a, "DelayAfterActionInMilliseconds")))
                                                                 .ToList(),
                                Str(t, "Result") ?? "Default",
                                t["IsFinal"]?.GetValue<bool>() ?? false))
                    .ToList();

        return new MasterConfig(devices, locations, rules);
    }

    private static MasterLocation Location(JsonNode node) => new(
        Str(node, "Name")!,
        Array(node["Items"]).Select(x => new MasterLocationItem(PortRef(x), Str(x, "CustomName"))).ToList(),
        Array(node["ItemsConditions"]).Select(x => new MasterLocationCondition(Str(x, "Type")!, Str(x, "Status"))).ToList(),
        Array(node["SubLocations"]).Select(Location).ToList());

    private static MasterPortRef PortRef(JsonNode node) => new(Str(node, "DeviceId")!, Str(node, "PortId")!);

    private static Dictionary<string, string> Icons(JsonNode? node) =>
        node?.AsObject().ToDictionary(x => x.Key, x => x.Value!.GetValue<string>()) ?? [];

    private static IEnumerable<JsonNode> Array(JsonNode? node) => node?.AsArray().Select(x => x!) ?? [];

    private static string? Str(JsonNode? node, string name) => node?[name] is JsonValue value ? value.ToString() : null;

    private static int Int(JsonNode node, string name) => node[name]?.GetValue<int>() ?? 0;
}

internal sealed record MasterDevice(string Id, string Name, string Ip, string? Password, IReadOnlyList<MasterPort> Ports);

internal sealed record MasterPort(string DeviceId, string Id, string Type, string Name, string? OutMode, IReadOnlyDictionary<string, string> Icons)
{
    public string Key => $"{DeviceId}/{Id}";
    public bool IsInput => Type == "IN";
    public bool IsRelay => Type == "OUT" && OutMode == "SW";
}

internal sealed record MasterPortRef(string DeviceId, string PortId)
{
    public string Key => $"{DeviceId}/{PortId}";
}

internal sealed record MasterLocation(string Name, IReadOnlyList<MasterLocationItem> Items, IReadOnlyList<MasterLocationCondition> Conditions, IReadOnlyList<MasterLocation> SubLocations);

internal sealed record MasterLocationItem(MasterPortRef Port, string? CustomName);

internal sealed record MasterLocationCondition(string Type, string? Status);

internal sealed record MasterRule(MasterPortRef Source, IReadOnlyList<string> Presses, MasterCondition? Condition, IReadOnlyList<MasterAction> Actions, string Result, bool IsFinal);

internal sealed record MasterCondition(string Type, string? Status, IReadOnlyList<MasterPortRef> Ports);

internal sealed record MasterAction(MasterPortRef Port, string? Command, int DelayBeforeMs, int DelayAfterMs);
