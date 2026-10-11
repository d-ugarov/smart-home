using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartHome.Tests.Infrastructure;

public sealed record PortRef(string DeviceId, int Port)
{
    public override string ToString() => $"{DeviceId}/{Port}";
}

public sealed record HouseRule(PortRef Source, IReadOnlyList<string> Presses, IReadOnlyList<PortRef> ConditionPorts, IReadOnlyList<PortRef> DestinationPorts);

/// <summary> A relay switched at a time of day; <see cref="Days"/> - names of the days of the week, empty for every day. </summary>
public sealed record TimedCommand(string At, string Days, PortRef Relay, bool On)
{
    public override string ToString() => $"{At} {(Days == "" ? "every day" : Days)}: {Relay} {(On ? "on" : "off")}";
}

/// <summary>
/// Input of the reference tests: house files (<c>devices.json</c>, <c>entities.json</c>, <c>automations.json</c>),
/// controller states (<c>cmd=all</c> snapshots, Cron pages <c>cf=7</c>) and golden files.
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
    private readonly Func<string, string?> cronPage;

    private House(string name, string? unavailableReason, string homeDirectory, Func<string, string> cmdAllSnapshot, Func<string, string?> cronPage,
                  string goldenDirectory)
    {
        this.cronPage = cronPage;
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

    /// <summary> Schedules of the house files as relay commands. </summary>
    public IReadOnlyList<TimedCommand> Schedules => LoadSchedules();

    /// <summary> The Cron of the controllers (their own schedules), from the dumps of the settings pages. </summary>
    public IReadOnlyList<TimedCommand> Cron => Controllers().SelectMany(x => ParseCron(x.Key, cronPage(x.Key))).ToList();

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
        var ports = Ports();

        return Read("automations.json")["Home"]!["Rules"]!.AsArray()
                                                         .Select(x => new HouseRule(
                                                                     ports[x!["Button"]!.GetValue<string>()],
                                                                     x["Presses"]!.AsArray().Select(p => p!.GetValue<string>()).ToList(),
                                                                     x["Condition"]?["Entities"]?.AsArray().Select(e => ports[e!.GetValue<string>()]).ToList() ?? [],
                                                                     x["Actions"]!.AsArray().Select(a => ports[a!["Entity"]!.GetValue<string>()]).ToList()))
                                                         .ToList();
    }

    private List<TimedCommand> LoadSchedules()
    {
        var ports = Ports();

        return Read("automations.json")["Home"]!["Automations"]!
               .AsArray()
               .SelectMany(x => x!["Rules"]!.AsArray())
               .Where(x => x!["Trigger"]!["At"] != null)
               .SelectMany(x => x!["Actions"]!.AsArray()
                                .Select(a => new TimedCommand(x["Trigger"]!["At"]!.GetValue<string>(),
                                                              Days(x["Trigger"]!["Days"]?.AsArray().Select(d => Enum.Parse<DayOfWeek>(d!.GetValue<string>())) ?? []),
                                                              ports[a!["Entity"]!.GetValue<string>()],
                                                              a["Command"]!.GetValue<string>() == "On")))
               .ToList();
    }

    /// <summary> Rows "T: HH:MM:D, Act: P:V;P:V"; D is 0 for every day, 1-7 for Monday-Sunday or a range of them. </summary>
    private static IEnumerable<TimedCommand> ParseCron(string deviceId, string? page)
    {
        if (page == null)
            yield break;

        var acts = Regex.Matches(page, @"name=crna(\d+) value=([^ >]*)").ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);

        foreach (Match row in Regex.Matches(page, @"name=crnt(\d+) value=(\d\d:\d\d):([\d-]+)"))
        {
            var spec = row.Groups[3].Value;
            IEnumerable<int> days = spec == "0"
                ? []
                : spec.Split('-') is [var from, var to]
                    ? Enumerable.Range(int.Parse(from), int.Parse(to) - int.Parse(from) + 1)
                    : [int.Parse(spec)];

            foreach (var command in acts[row.Groups[1].Value].Split(';'))
            {
                var (port, isOn) = command.Split(':') is [var p, var v] && v is "0" or "1"
                    ? (int.Parse(p), v == "1")
                    : throw new InvalidOperationException($"Cron of {deviceId}: unsupported action '{command}'");
                yield return new TimedCommand(row.Groups[2].Value, Days(days.Select(x => (DayOfWeek)(x % 7))), new PortRef(deviceId, port), isOn);
            }
        }
    }

    private static string Days(IEnumerable<DayOfWeek> days) => string.Join(",", days.Order());

    private Dictionary<string, PortRef> Ports() =>
        Controllers().SelectMany(c => c.Value!["Ports"]!.AsObject().Select(p => (Entity: p.Value!.GetValue<string>(), Port: new PortRef(c.Key, int.Parse(p.Key)))))
                     .ToDictionary(x => x.Entity, x => x.Port);

    private static House CreateExample()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var snapshots = JsonNode.Parse(File.ReadAllText(Path.Combine(testData, "example-controllers.json")))!.AsObject();

        return new House("example",
                         unavailableReason: null,
                         Path.Combine(RepositoryDirectory(), "src", "SmartHome.Host", "home"),
                         id => snapshots[id]!.GetValue<string>(),
                         _ => null,
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
                         id => File.ReadAllText(Path.Combine(Dump(id), "cmd=all.txt")),
                         id => File.Exists(Path.Combine(Dump(id), "cf=7.html")) ? File.ReadAllText(Path.Combine(Dump(id), "cf=7.html")) : null,
                         Path.Combine(configs, "tests", "golden"));

        string Dump(string id) => Directory.GetDirectories(Path.Combine(configs, "controllers", "dump"), $"{id}-*").Single();
    }

    private static string ProjectDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(Path.GetDirectoryName(path))!;

    private static string RepositoryDirectory() => Path.GetFullPath(Path.Combine(ProjectDirectory(), "..", ".."));
}
