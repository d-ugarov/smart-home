using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// The owner gets every request of the controllers and every command of the rules, as in <c>master</c> (K10); now
/// through the notification service instead of the log. Sent after the answer to the controller.
/// </summary>
[NonParallelizable]
public class NotificationTests
{
    private const long ChatId = 555;

    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    private FakeTelegramApi Telegram => factory.Network.Telegram;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["Telegram:NotificationChatId"] = ChatId.ToString()});
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
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
    public async Task Press_EventAndCommand()
    {
        await client.GetAsync("/megad/1?pt=0&click=1&cnt=7");

        Assert.That(await NotificationsAsync(2), Is.EqualTo(new[] {"MegaD-1: Hall switch: click, cnt 7", "→ Hall light: toggle — OK"}));
    }

    [Test]
    public async Task OffForLightThatIsOff_Skipped()
    {
        factory.Network.Devices["2"].Set(7, true);

        await client.GetAsync("/megad/2?pt=0&click=1");

        Assert.That(await NotificationsAsync(3), Is.EqualTo(new[]
                                                            {
                                                                "MegaD-2: Living room switch: click",
                                                                "→ Living room chandelier: off — OK",
                                                                "→ Living room spots: off — skipped, already off",
                                                            }));
    }

    [TestCase("/megad/1?pt=5&cnt=12", "MegaD-1: not handled, pt=5&cnt=12", TestName = "Water meter pulse")]
    [TestCase("/megad/1?pt=7&mdid=1&v=1", "MegaD-1: Hall light: on", TestName = "Output changed")]
    [TestCase("/megad/1?st=1", "🔄 MegaD-1 restarted", TestName = "Controller restarted")]
    [TestCase("/megad/1?pt=6&m=1", "MegaD-1: Leak sensor: release", TestName = "Sensor")]
    [TestCase("/megad/1?pt=15", "MegaD-1: hardware input, pt=15", TestName = "Hardware input")]
    [TestCase("/megad/9?pt=0", "megad/9: unknown controller, pt=0", TestName = "Unknown controller")]
    public async Task ControllerRequest_Reported(string url, string expected)
    {
        await client.GetAsync(url);

        Assert.That(await NotificationsAsync(1), Is.EqualTo(new[] {expected}));
    }

    private async Task<List<string>> NotificationsAsync(int count)
    {
        await Telegram.WaitForCallAsync("sendMessage", skip: count - 1);
        await Task.Delay(100);

        return Telegram.Calls.Where(x => x.Method == "sendMessage" && x.Body["chat_id"]!.ToString() == ChatId.ToString())
                       .Select(x => x.Body["text"]!.GetValue<string>())
                       .ToList();
    }
}
