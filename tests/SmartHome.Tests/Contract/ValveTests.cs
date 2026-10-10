using SmartHome.Core.Menu;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Valve buttons of the bot (agreed difference from <c>master</c>): a confirmation, then the scenario of the controller
/// (<c>?pt=N&amp;cmd=d</c>) instead of a single relay; no new command while a pulse is running.
/// Example flat: valve.bathroom, open - input 15 / relay 12, close - input 6 / relay 13.
/// </summary>
[NonParallelizable]
public class ValveTests
{
    private const string OpenButton = "Open valves";
    private const string CloseButton = "Close valves";

    private HomeServerFactory factory = null!;
    private HttpClient client = null!;
    private BotSession owner = null!;

    private FakeMegaD MegaD1 => factory.Network.Devices["1"];

    // A server per test: a sent command blocks the valve inside the server.
    [SetUp]
    public async Task SetUp()
    {
        factory = new HomeServerFactory(House.Example);
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
        owner = new BotSession(factory.Network.Telegram);
    }

    [TearDown]
    public async Task TearDown()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Test]
    public async Task Close_AsksFirst_YesRunsTheCloseScenario()
    {
        var water = await OpenWaterPageAsync();

        var question = await owner.PressAsync(water, CloseButton);
        var commandsAfterQuestion = factory.Network.Commands.ToList();
        var after = await owner.PressAsync(question, MenuTexts.Yes);

        Assert.Multiple(() =>
        {
            Assert.That(question.Text, Is.EqualTo("Bathroom water\n\nClose valves?"));
            Assert.That(commandsAfterQuestion, Is.Empty);
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 6, "d")}));
            Assert.That(after.Text, Is.EqualTo("💧 Water"));
        });
    }

    [Test]
    public async Task No_GoesBackWithoutCommand()
    {
        var water = await OpenWaterPageAsync();
        var question = await owner.PressAsync(water, OpenButton);

        var back = await owner.PressAsync(question, MenuTexts.No);

        Assert.Multiple(() =>
        {
            Assert.That(back.Text, Is.EqualTo("💧 Water"));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task PulseRunning_ShownOnTheButton_NoCommandForEitherLine()
    {
        MegaD1.Set(13, true);
        var water = await OpenWaterPageAsync();

        var afterClose = await owner.PressAsync(water, CloseButton);
        var afterOpen = await owner.PressAsync(afterClose, OpenButton);

        Assert.Multiple(() =>
        {
            Assert.That(water.Find(CloseButton).Text, Is.EqualTo($"{MenuTexts.PulseRunning} {CloseButton}"));
            Assert.That(water.Find(OpenButton).Text, Is.EqualTo(OpenButton));
            Assert.That(afterClose.Text, Is.EqualTo("💧 Water"), "no confirmation while the pulse runs");
            Assert.That(afterOpen.Text, Is.EqualTo("💧 Water"));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task AfterCommand_Blocked_UntilTheControllerReportsTheEndOfThePulse()
    {
        // The relay turns on a second after the command: the server must not take the gap for "idle".
        var water = await OpenWaterPageAsync();
        await owner.PressAsync(await owner.PressAsync(water, CloseButton), MenuTexts.Yes);
        factory.Reset();

        var blocked = await owner.PressAsync(water, OpenButton);
        var closeRelayOff = await client.GetAsync("/megad/1?pt=13&mdid=1&v=0");
        var question = await owner.PressAsync(water, OpenButton);
        await owner.PressAsync(question, MenuTexts.Yes);

        Assert.Multiple(() =>
        {
            Assert.That(blocked.Text, Is.EqualTo("💧 Water"));
            Assert.That(blocked.Find(CloseButton).Text, Is.EqualTo($"{MenuTexts.PulseRunning} {CloseButton}"));
            Assert.That((int)closeRelayOff.StatusCode, Is.EqualTo(204));
            Assert.That(question.Text, Is.EqualTo("Bathroom water\n\nOpen valves?"));
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("1", 15, "d")}));
        });
    }

    [Test]
    public async Task ControllerOffline_NoCommand()
    {
        var water = await OpenWaterPageAsync();
        var question = await owner.PressAsync(water, CloseButton);
        MegaD1.IsOffline = true;

        await owner.PressAsync(question, MenuTexts.Yes);

        Assert.That(factory.Network.Commands, Is.Empty);
    }

    private async Task<BotPage> OpenWaterPageAsync()
    {
        var dashboard = await owner.SendTextAsync();
        var rooms = await owner.PressAsync(dashboard, "🏠 Rooms");
        var bathroom = await owner.PressAsync(rooms, "Bathroom" + BotPage.NavigationMark);
        return await owner.PressAsync(bathroom, "💧 Water" + BotPage.NavigationMark);
    }
}
