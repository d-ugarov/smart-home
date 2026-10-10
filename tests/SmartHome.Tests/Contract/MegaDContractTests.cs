using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;
using System.Net;

namespace SmartHome.Tests.Contract;

/// <summary>
/// The contract with the controllers in readable form, on the example flat (appsettings.json of the server).
/// Every rule and state combination is covered by <see cref="MegaDEventsGoldenTests"/>.
/// </summary>
[NonParallelizable]
public class MegaDContractTests
{
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    private FakeMegaD MegaD1 => factory.Network.Devices["1"];
    private FakeMegaD MegaD2 => factory.Network.Devices["2"];

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        factory = new HomeServerFactory(House.Example);
        client = factory.CreateClient();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [SetUp]
    public void SetUp() => factory.Reset();

    [Test]
    public async Task RuleMatched_CommandsSentBeforeResponse_200()
    {
        var response = await client.GetAsync("/megad/1?pt=0&click=1&cnt=1");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 7, "2")}));
            Assert.That(MegaD1.IsOn(7), Is.True);
        });
    }

    [Test]
    public async Task InputHasRules_ButNoneForThisPress_204()
    {
        var response = await client.GetAsync("/megad/1?pt=0&m=1&cnt=1");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task OffCommands_SkippedForRelaysAlreadyOff_Still200()
    {
        MegaD2.Set(10, false);

        var response = await client.GetAsync("/megad/1?pt=0&m=2");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task OffCommands_SentToRelaysThatAreOn_InRuleOrder_OnBothControllers()
    {
        MegaD2.Set(9, true);
        MegaD1.Set(7, true);

        await client.GetAsync("/megad/1?pt=0&m=2");

        Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 7, "0"), new MegaDCommand("2", 9, "0"), new MegaDCommand("2", 10, "0")}));
    }

    [Test]
    public async Task CommandedControllerOffline_StillAnswers200_ControllerDoesNotRunItsAct()
    {
        MegaD1.Set(7, true);
        MegaD2.IsOffline = true;

        var response = await client.GetAsync("/megad/1?pt=0&m=2");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 7, "0")}));
        });
    }

    [Test]
    public async Task ConditionMet_FinalRuleStopsTheRest()
    {
        MegaD1.Set(10, false);
        MegaD1.Set(11, false);

        await client.GetAsync("/megad/1?pt=2&click=1");

        Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 10, "1"), new MegaDCommand("1", 11, "1")}));
    }

    [Test]
    public async Task ConditionNotMet_NextRuleApplies()
    {
        MegaD1.Set(10, true);

        await client.GetAsync("/megad/1?pt=2&click=1");

        Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 10, "0")}));
    }

    [Test]
    public async Task RuleWithDefaultResult_CommandsSent_ButAnswer204_SoControllerRunsItsActToo()
    {
        MegaD2.Set(7, false);
        MegaD2.Set(8, false);

        var response = await client.GetAsync("/megad/2?pt=1&click=1");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("2", 7, "1")}));
        });
    }

    [TestCase("/megad/1?pt=5&cnt=5", TestName = "Water meter pulse (input without rules)")]
    [TestCase("/megad/1?pt=12&mdid=1&v=1", TestName = "Relay switched on (output event)")]
    [TestCase("/megad/1?pt=12&mdid=1&v=0", TestName = "Relay switched off (output event)")]
    [TestCase("/megad/1?st=1", TestName = "Controller started")]
    [TestCase("/megad/9?pt=0&click=1", TestName = "Unknown controller")]
    [TestCase("/megad/1?pt=99&click=1", TestName = "Unknown port")]
    [TestCase("/megad/1", TestName = "No parameters")]
    public async Task NotHandledByServer_204_ControllerRunsItsAct(string url)
    {
        var response = await client.GetAsync(url);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [TestCase("/megad/1?pt=0&m=2", 1, 1, TestName = "Rules command both controllers")]
    [TestCase("/megad/1?pt=1&click=1", 1, 0, TestName = "Rules command one controller")]
    public async Task StatusesAreReadOncePerControllerPerEvent(string url, int megaD1Reads, int megaD2Reads)
    {
        await client.GetAsync(url);

        Assert.Multiple(() =>
        {
            Assert.That(MegaD1.StatusRequests, Is.EqualTo(megaD1Reads));
            Assert.That(MegaD2.StatusRequests, Is.EqualTo(megaD2Reads));
        });
    }
}
