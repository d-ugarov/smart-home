using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MegaDTelegramRemoteControl.Tests.Infrastructure;

public sealed record PortRef(string DeviceId, int Port)
{
    public override string ToString() => $"{DeviceId}/{Port}";
}

public sealed record HouseRule(PortRef Source, IReadOnlyList<string> Commands, IReadOnlyList<PortRef> ConditionPorts, IReadOnlyList<PortRef> DestinationPorts);

/// <summary>
/// Input of the reference tests: server config, controller states (<c>cmd=all</c> snapshots) and golden files.
/// <list type="bullet">
/// <item><see cref="Example"/> - the example flat from the repo's appsettings.json, always available.</item>
/// <item><see cref="Real"/> - the owner's home, kept in the private configs repository next to this one
/// (or the <c>SMART_HOME_CONFIGS</c> directory). Its tests are ignored when the repository is not there.</item>
/// </list>
/// Config is read independently of the server code, so the tests do not depend on its configuration classes.
/// </summary>
public sealed class House
{
    public const string TestPassword = "test-pwd";

    private static readonly JsonDocumentOptions jsonOptions = new() {CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true};

    private readonly Func<string, string> cmdAllSnapshot;
    private readonly Lazy<JsonObject> root;

    private House(string name, string? unavailableReason, string appSettingsPath, Func<string, string> cmdAllSnapshot, string goldenDirectory)
    {
        Name = name;
        UnavailableReason = unavailableReason;
        AppSettingsPath = appSettingsPath;
        GoldenDirectory = goldenDirectory;
        this.cmdAllSnapshot = cmdAllSnapshot;
        root = new Lazy<JsonObject>(() => JsonNode.Parse(File.ReadAllText(AppSettingsPath), documentOptions: jsonOptions)!.AsObject());
    }

    public static House Example { get; } = CreateExample();

    public static House Real { get; } = CreateReal();

    public static IEnumerable<House> All => [Example, Real];

    public string Name { get; }
    public string? UnavailableReason { get; }
    public string AppSettingsPath { get; }
    public string GoldenDirectory { get; }

    public IEnumerable<(string Id, string Name, string Url)> Devices =>
        root.Value["DevicesConfig"]!["Devices"]!.AsArray()
                                                .Select(x => (x!["Id"]!.GetValue<string>(), x["Name"]!.GetValue<string>(), x["Ip"]!.GetValue<string>()));

    /// <summary> Rules as the server loads them: rules and ports of undescribed controllers/ports are skipped. </summary>
    public IReadOnlyList<HouseRule> Rules => LoadRules();

    public string CmdAllSnapshot(string deviceId) => cmdAllSnapshot(deviceId);

    public void EnsureAvailable()
    {
        if (UnavailableReason != null)
            Assert.Ignore(UnavailableReason);
    }

    public override string ToString() => Name;

    private List<HouseRule> LoadRules()
    {
        static PortRef Port(JsonNode node) => new(node["DeviceId"]!.GetValue<string>(), int.Parse(node["PortId"]!.GetValue<string>()));

        var ports = root.Value["DevicesConfig"]!["Devices"]!.AsArray()
                                                             .SelectMany(d => d!["DevicePorts"]!.AsArray().Select(p => new PortRef(d["Id"]!.GetValue<string>(), int.Parse(p!["Id"]!.GetValue<string>()))))
                                                             .ToHashSet();

        return root.Value["AutomationConfig"]!["Triggers"]!.AsArray()
                                                           .Select(x => new HouseRule(
                                                                       Port(x!["SourcePortState"]!),
                                                                       x["SourcePortState"]!["Status"]!["InPortCommands"]!.AsArray().Select(c => c!.GetValue<string>()).ToList(),
                                                                       x["AdditionalConditions"]?["Ports"]?.AsArray().Select(p => Port(p!)).Where(ports.Contains).ToList() ?? [],
                                                                       x["DestinationPortStates"]!.AsArray().Select(p => Port(p!)).Where(ports.Contains).ToList()))
                                                           .Where(x => ports.Contains(x.Source))
                                                           .ToList();
    }

    private static House CreateExample()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var snapshots = JsonNode.Parse(File.ReadAllText(Path.Combine(testData, "example-controllers.json")))!.AsObject();

        return new House("example",
                         unavailableReason: null,
                         Path.Combine(testData, "example-appsettings.json"),
                         id => snapshots[id]!.GetValue<string>(),
                         Path.Combine(ProjectDirectory(), "Golden"));
    }

    private static House CreateReal()
    {
        var configs = Environment.GetEnvironmentVariable("SMART_HOME_CONFIGS") ??
                      Path.GetFullPath(Path.Combine(ProjectDirectory(), "..", "..", "Smart Home Configs"));
        var appSettings = Path.Combine(configs, "config", "appsettings.json");

        return new House("real",
                         File.Exists(appSettings) ? null : $"Real home data not found: {appSettings} (private repository, set SMART_HOME_CONFIGS)",
                         appSettings,
                         id => File.ReadAllText(Directory.GetDirectories(Path.Combine(configs, "controllers", "dump"), $"{id}-*").Single() + "/cmd=all.txt"),
                         Path.Combine(configs, "tests", "golden"));
    }

    private static string ProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;
}
