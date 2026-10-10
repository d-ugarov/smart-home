using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartHome.Tests.Infrastructure;

public sealed record PortRef(string DeviceId, int Port)
{
    public override string ToString() => $"{DeviceId}/{Port}";
}

public sealed record HouseRule(PortRef Source, IReadOnlyList<string> Presses, IReadOnlyList<PortRef> ConditionPorts, IReadOnlyList<PortRef> DestinationPorts);

/// <summary>
/// Input of the reference tests: house files (<c>devices.json</c>, <c>entities.json</c>, <c>automations.json</c>),
/// controller states (<c>cmd=all</c> snapshots) and golden files.
/// <list type="bullet">
/// <item><see cref="Example"/> - the example flat of the repo (<c>src/SmartHome.Host/home</c>), always available.</item>
/// <item><see cref="Real"/> - the owner's home, kept in the private configs repository next to this one
/// (or the <c>SMART_HOME_CONFIGS</c> directory). Its tests are ignored when the repository is not there.</item>
/// </list>
/// Files are read as plain JSON, independently of the server code.
/// </summary>
public sealed class House
{
    public const string TestPassword = "test-pwd";

    private static readonly JsonDocumentOptions jsonOptions = new() {CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true};

    private readonly Func<string, string> cmdAllSnapshot;

    private House(string name, string? unavailableReason, string homeDirectory, Func<string, string> cmdAllSnapshot, string goldenDirectory)
    {
        Name = name;
        UnavailableReason = unavailableReason;
        HomeDirectory = homeDirectory;
        GoldenDirectory = goldenDirectory;
        this.cmdAllSnapshot = cmdAllSnapshot;
    }

    public static House Example { get; } = CreateExample();

    public static House Real { get; } = CreateReal();

    public static IEnumerable<House> All => [Example, Real];

    public string Name { get; }
    public string? UnavailableReason { get; }
    public string HomeDirectory { get; }
    public string GoldenDirectory { get; }

    public IEnumerable<string> Files => Host.HomeFiles.Names.Select(x => Path.Combine(HomeDirectory, x));

    public IEnumerable<(string Id, string Name, string Url)> Devices =>
        Controllers().Select(x => (x.Key, x.Value!["Name"]!.GetValue<string>(), x.Value["Address"]!.GetValue<string>()));

    public IReadOnlyList<HouseRule> Rules => LoadRules();

    /// <summary> Valve entity → its relays. </summary>
    public IReadOnlyDictionary<string, (PortRef Open, PortRef Close)> ValveRelays =>
        Controllers().SelectMany(c => c.Value!["Valves"]?.AsArray().Select(v => (Entity: v!["Entity"]!.GetValue<string>(),
                                                                                    Open: new PortRef(c.Key, v["OpenRelay"]!.GetValue<int>()),
                                                                                    Close: new PortRef(c.Key, v["CloseRelay"]!.GetValue<int>())))
                                     ?? [])
                     .ToDictionary(x => x.Entity, x => (x.Open, x.Close));

    public string CmdAllSnapshot(string deviceId) => cmdAllSnapshot(deviceId);

    public void EnsureAvailable()
    {
        if (UnavailableReason != null)
            Assert.Ignore(UnavailableReason);
    }

    public override string ToString() => Name;

    private JsonObject Read(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(HomeDirectory, file)), documentOptions: jsonOptions)!.AsObject();

    private JsonObject Controllers() => Read("devices.json")["MegaD"]!["Controllers"]!.AsObject();

    private List<HouseRule> LoadRules()
    {
        var ports = Controllers().SelectMany(c => c.Value!["Ports"]!.AsObject().Select(p => (Entity: p.Value!.GetValue<string>(), Port: new PortRef(c.Key, int.Parse(p.Key)))))
                                 .ToDictionary(x => x.Entity, x => x.Port);

        return Read("automations.json")["Home"]!["Rules"]!.AsArray()
                                                         .Select(x => new HouseRule(
                                                                     ports[x!["Button"]!.GetValue<string>()],
                                                                     x["Presses"]!.AsArray().Select(p => p!.GetValue<string>()).ToList(),
                                                                     x["Condition"]?["Entities"]?.AsArray().Select(e => ports[e!.GetValue<string>()]).ToList() ?? [],
                                                                     x["Actions"]!.AsArray().Select(a => ports[a!["Entity"]!.GetValue<string>()]).ToList()))
                                                         .ToList();
    }

    private static House CreateExample()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var snapshots = JsonNode.Parse(File.ReadAllText(Path.Combine(testData, "example-controllers.json")))!.AsObject();

        return new House("example",
                         unavailableReason: null,
                         Path.Combine(RepositoryDirectory(), "src", "SmartHome.Host", "home"),
                         id => snapshots[id]!.GetValue<string>(),
                         Path.Combine(ProjectDirectory(), "Golden"));
    }

    private static House CreateReal()
    {
        var configs = Environment.GetEnvironmentVariable("SMART_HOME_CONFIGS") ??
                      Path.GetFullPath(Path.Combine(RepositoryDirectory(), "..", "Smart Home Configs"));
        var home = Path.Combine(configs, "config", "home");

        return new House("real",
                         Directory.Exists(home) ? null : $"Real home data not found: {home} (private repository, set SMART_HOME_CONFIGS)",
                         home,
                         id => File.ReadAllText(Directory.GetDirectories(Path.Combine(configs, "controllers", "dump"), $"{id}-*").Single() + "/cmd=all.txt"),
                         Path.Combine(configs, "tests", "golden"));
    }

    private static string ProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    private static string RepositoryDirectory() => Path.GetFullPath(Path.Combine(ProjectDirectory(), "..", ".."));
}
