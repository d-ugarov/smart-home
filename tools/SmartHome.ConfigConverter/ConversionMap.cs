using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartHome.ConfigConverter;

/// <summary>
/// What the old config does not know: readable ids of entities and pages, inputs that belong to the controller, valves.
/// </summary>
internal sealed record ConversionMap(
    IReadOnlyDictionary<string, string> Ports,
    IReadOnlyDictionary<string, string> Pages,
    IReadOnlyDictionary<string, IReadOnlyList<int>> HardwareInputs,
    IReadOnlyList<ValveMap> Valves)
{
    public const string PagePathSeparator = " / ";

    public static ConversionMap Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: MasterConfig.JsonOptions)!.AsObject();

        return new ConversionMap(
            Strings(root["Ports"]),
            Strings(root["Pages"]),
            root["HardwareInputs"]?.AsObject().ToDictionary(x => x.Key, x => (IReadOnlyList<int>)x.Value!.AsArray().Select(i => i!.GetValue<int>()).ToList()) ?? [],
            root["Valves"]?.Deserialize<List<ValveMap>>() ?? []);
    }

    public ValveMap? ValveOfRelay(string deviceId, string portId) =>
        Valves.FirstOrDefault(x => x.Controller == deviceId && (x.OpenRelay.ToString() == portId || x.CloseRelay.ToString() == portId));

    private static Dictionary<string, string> Strings(JsonNode? node) =>
        node?.AsObject().ToDictionary(x => x.Key, x => x.Value!.GetValue<string>()) ?? [];
}

internal sealed record ValveMap(string Id, string Name, string Controller, int OpenInput, int CloseInput, int OpenRelay, int CloseRelay);
