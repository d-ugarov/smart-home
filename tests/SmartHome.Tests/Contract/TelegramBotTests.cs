using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// The bot through the Telegram Bot API (fake api.telegram.org) on the example flat: access, navigation and what
/// a device button does. The whole menu is covered by <see cref="TelegramMenuGoldenTests"/>.
/// </summary>
[NonParallelizable]
public class TelegramBotTests
{
    private HomeServerFactory factory = null!;
    private BotSession owner = null!;

    private FakeTelegramApi Telegram => factory.Network.Telegram;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        factory = new HomeServerFactory(House.Example);
        await factory.WaitForBotAsync();
        owner = new BotSession(Telegram);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown() => await factory.DisposeAsync();

    [SetUp]
    public void SetUp() => factory.Reset();

    [Test]
    public async Task AnyText_FromOwner_SendsDashboard()
    {
        var menu = await owner.SendTextAsync("hello");

        Assert.Multiple(() =>
        {
            Assert.That(menu.Text, Is.EqualTo("Dashboard"));
            Assert.That(menu.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"🏠 Rooms", "✔️ Now on", "🛠 Engineering"}));
        });
    }

    [Test]
    public async Task Stranger_GetsNoAnswer()
    {
        Telegram.SendText(HomeServerFactory.StrangerId, "/dashboard");
        await owner.SendTextAsync();
        await Task.Delay(300);

        Assert.That(Telegram.Calls.Where(x => x.Method == "sendMessage").Select(x => x.Body["chat_id"]!.ToString()),
                    Is.EqualTo(new[] {HomeServerFactory.OwnerId.ToString()}));
    }

    [Test]
    public async Task StrangerButtonPress_Ignored()
    {
        var dashboard = await owner.SendTextAsync();
        var engineering = await owner.PressAsync(dashboard, "🛠 Engineering");
        var callsBefore = Telegram.Calls.Count;

        Telegram.PressButton(HomeServerFactory.StrangerId, engineering.MessageId, engineering.Find("Floor heating").CallbackData);
        await Task.Delay(300);

        Assert.Multiple(() =>
        {
            Assert.That(Telegram.Calls, Has.Count.EqualTo(callsBefore));
            Assert.That(factory.Network.Commands, Is.Empty);
        });
    }

    [Test]
    public async Task DeviceButton_SwitchesRelay_AndRedrawsPage()
    {
        var dashboard = await owner.SendTextAsync();
        var rooms = await owner.PressAsync(dashboard, "🏠 Rooms");
        var bedroom = await owner.PressAsync(rooms, "Bedroom" + BotPage.NavigationMark);

        var redrawn = await owner.PressAsync(bedroom, "Night light");

        Assert.Multiple(() =>
        {
            Assert.That(bedroom.Find("Night light").Text, Is.EqualTo("🟢 Night light"));
            Assert.That(factory.Network.Commands, Is.EqualTo(new[] {new MegaDCommand("2", 10, "2")}));
            Assert.That(redrawn.Text, Is.EqualTo("Bedroom"));
            Assert.That(redrawn.Find("Night light").Text, Is.EqualTo("🔘 Night light"));
        });
    }

    [Test]
    public async Task SensorButton_DoesNothing()
    {
        var dashboard = await owner.SendTextAsync();
        var engineering = await owner.PressAsync(dashboard, "🛠 Engineering");

        await owner.PressAsync(engineering, "Leak sensor");

        Assert.That(factory.Network.Commands, Is.Empty);
    }

    [Test]
    public async Task PortWithoutIcons_ShowsStateAsText()
    {
        var dashboard = await owner.SendTextAsync();
        var engineering = await owner.PressAsync(dashboard, "🛠 Engineering");

        Assert.That(engineering.Find("Main contactor").Text, Is.EqualTo("[On] Main contactor"));
    }

    [Test]
    public async Task NowOn_ListsEveryRelayThatIsOn()
    {
        var dashboard = await owner.SendTextAsync();

        var nowOn = await owner.PressAsync(dashboard, "✔️ Now on");

        Assert.That(nowOn.Buttons.Select(x => x.Text),
                    Is.EqualTo(new[] {"🟢 Bathroom fan", "[On] Main contactor", "🟢 Bedroom night light", "🟢 Floor heating"}));
    }

    [Test]
    public async Task FooterButtons_BackAndDashboard()
    {
        var dashboard = await owner.SendTextAsync();
        var rooms = await owner.PressAsync(dashboard, "🏠 Rooms");
        var bathroom = await owner.PressAsync(rooms, "Bathroom" + BotPage.NavigationMark);
        var water = await owner.PressAsync(bathroom, "💧 Water" + BotPage.NavigationMark);

        var back = await owner.PressAsync(water, "↩ Back");
        var home = await owner.PressAsync(water, "🔼 Dashboard");

        Assert.Multiple(() =>
        {
            Assert.That(back.Text, Is.EqualTo("Bathroom"));
            Assert.That(home.Text, Is.EqualTo("Dashboard"));
        });
    }
}
