using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Every automation rule of the house: each input with rules × each kind of press × states of the lamps the
/// rules look at and command. Recorded from <c>master</c>: response code and relay commands in order.
/// </summary>
[NonParallelizable]
[TestFixtureSource(typeof(House), nameof(House.All))]
public class MegaDEventsGoldenTests(House house)
{
    private const int MaxExhaustiveConditionPorts = 5;

    private static readonly (string Name, string Query)[] presses =
    [
        ("press", ""),
        ("release", "&m=1"),
        ("click", "&click=1"),
        ("double", "&click=2"),
        ("long", "&m=2"),
    ];

    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        house.EnsureAvailable();
        factory = new HomeServerFactory(house);
        client = factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        client?.Dispose();
        if (factory != null)
            await factory.DisposeAsync();
    }

    [Test]
    public async Task AllRules_MatchMaster()
    {
        var lines = new List<string>();
        var deviceNames = house.Devices.ToDictionary(x => x.Id, x => x.Name);

        foreach (var source in house.Rules.Select(x => x.Source).Distinct().OrderBy(x => x.DeviceId).ThenBy(x => x.Port))
        {
            var rules = house.Rules.Where(x => x.Source == source).ToList();
            var conditionPorts = rules.SelectMany(x => x.ConditionPorts).Distinct().OrderBy(Key).ToList();
            var otherPorts = rules.SelectMany(x => x.DestinationPorts).Except(conditionPorts).Distinct().OrderBy(Key).ToList();

            foreach (var state in States(conditionPorts, otherPorts))
            {
                foreach (var (pressName, pressQuery) in presses)
                {
                    factory.Reset();
                    foreach (var (port, isOn) in state)
                        factory.Network.Devices[port.DeviceId].Set(port.Port, isOn);

                    var response = await client.GetAsync($"/megad/{source.DeviceId}?pt={source.Port}{pressQuery}&cnt=1");
                    var commands = factory.Network.Commands;

                    lines.Add($"{deviceNames[source.DeviceId]} pt={source.Port} {pressName} " +
                              $"| {string.Join(" ", state.Select(x => $"{x.Port}={(x.IsOn ? 1 : 0)}"))} " +
                              $"| {(int)response.StatusCode} " +
                              $"| {(commands.Count > 0 ? string.Join(" ", commands) : "-")}");
                }
            }
        }

        Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
        Golden.AssertMatches(house, "megad-events.golden", lines);
    }

    /// <summary>
    /// All combinations of the ports used in conditions (when there are few of them); the remaining commanded
    /// ports are either all off or all on, which is what decides whether On/Off commands are skipped.
    /// </summary>
    private static IEnumerable<List<(PortRef Port, bool IsOn)>> States(List<PortRef> conditionPorts, List<PortRef> otherPorts)
    {
        var conditionStates = conditionPorts.Count <= MaxExhaustiveConditionPorts
            ? Enumerable.Range(0, 1 << conditionPorts.Count).Select(mask => conditionPorts.Select((p, i) => (p, (mask & (1 << i)) != 0)).ToList())
            : [conditionPorts.Select(p => (p, false)).ToList(), conditionPorts.Select(p => (p, true)).ToList()];

        foreach (var conditionState in conditionStates)
        {
            foreach (var othersOn in otherPorts.Count > 0 ? new[] {false, true} : [false])
            {
                yield return conditionState.Concat(otherPorts.Select(p => (p, othersOn))).ToList();
            }
        }
    }

    private static (string, int) Key(PortRef port) => (port.DeviceId, port.Port);
}
