using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Automations;
using SmartHome.Core.Menu;
using SmartHome.Core.Notifications;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Categories of notifications on the example flat: the owner turns Events, Automation and Meters down (silent or off)
/// in the menu; Security (MegaD-1: leak sensor 6, valve inputs 6 and 15, valve relays 12 and 13) and System always
/// arrive with a sound.
/// </summary>
[NonParallelizable]
public class NotificationCategoryTests
{
    // A request of the leak protection: arrives in any case, so everything published before it has been handled.
    private const string Marker = "/megad/1?pt=15";
    private const string MarkerText = "MegaD-1: hardware input, pt=15";

    private static readonly TimeSpan NotificationBurstWindow = TimeSpan.FromMinutes(1);

    private static readonly DateTimeOffset start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private FakeTimeProvider time = null!;
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    private MenuService Menu => factory.Services.GetRequiredService<MenuService>();
    private NotificationPreferences Preferences => factory.Services.GetRequiredService<NotificationPreferences>();
    private FakeMegaD MegaD1 => factory.Network.Devices["1"];

    [SetUp]
    public Task SetUp() => StartAsync();

    [TearDown]
    public async Task TearDown() => await StopAsync();

    [Test]
    public async Task EventsSilent_SentWithoutASound_ProtectionWithASound()
    {
        await SetAsync(NotificationCategory.Events, NotificationMode.Silent);

        await client.GetAsync("/megad/1?pt=0&click=1");
        await client.GetAsync(Marker);

        Assert.That(await SentUntilMarkerAsync(), Is.EqualTo(new[]
                                                            {
                                                                ("MegaD-1: Hall switch: click", true),
                                                                ("→ Hall light: toggle — OK", true),
                                                                (MarkerText, false),
                                                            }));
    }

    [Test]
    public async Task EventsOff_LeakProtectionAndSystemStillArrive()
    {
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);

        await client.GetAsync("/megad/1?pt=0&click=1");
        await client.GetAsync("/megad/1?pt=7&mdid=1&v=1");
        await client.GetAsync("/megad/1?pt=5&cnt=12");
        await client.GetAsync("/megad/1?pt=6");
        await client.GetAsync("/megad/1?pt=13&mdid=1&v=1");
        await client.GetAsync("/megad/9?pt=0");
        await client.GetAsync("/megad/2?st=1");
        await client.GetAsync(Marker);

        Assert.That(await SentUntilMarkerAsync(), Is.EqualTo(new[]
                                                            {
                                                                ("MegaD-1: Leak sensor: press", false),
                                                                ("MegaD-1: Bathroom water, close relay: on", false),
                                                                ("megad/9: unknown controller, pt=0", false),
                                                                ("🔄 MegaD-2 restarted", false),
                                                                (MarkerText, false),
                                                            }));
    }

    [Test]
    public async Task EventsOff_FailedCommandStillArrives()
    {
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);
        MegaD1.IsOffline = true;

        await client.GetAsync("/megad/1?pt=1&click=1");
        await client.GetAsync(Marker);

        var sent = await SentUntilMarkerAsync();
        Assert.Multiple(() =>
        {
            Assert.That(sent, Has.Count.EqualTo(2));
            Assert.That(sent[0].Text, Does.StartWith("→ Kitchen light: toggle — ⚠️ failed"));
            Assert.That(sent[0].Silent, Is.False);
        });
    }

    [Test]
    public async Task AutomationOff_CommandDone_NotReported()
    {
        await SetAsync(NotificationCategory.Automation, NotificationMode.Off);
        MegaD1.Set(7, true);
        await client.GetAsync("/megad/1?pt=7&mdid=1&v=1");
        await WaitUntilAsync(() => factory.Services.GetRequiredService<AutomationEngine>().Pending.Any(), "the auto-off scheduled");

        time.Advance(TimeSpan.FromHours(8));
        await WaitUntilAsync(() => factory.Network.Commands.Contains(new MegaDCommand("1", 7, "0")), "the auto-off done");
        await client.GetAsync(Marker);

        Assert.That((await SentUntilMarkerAsync()).Select(x => x.Text), Has.None.StartsWith("🤖"));
    }

    [Test]
    public async Task Menu_PressSwitchesOnSilentOff_SecurityAndSystemNotOffered()
    {
        var owner = new BotSession(factory.Network.Telegram);

        var dashboard = await owner.SendTextAsync();
        var page = await owner.PressAsync(dashboard, MenuTexts.NotificationsButton);
        var silent = await owner.PressAsync(page, "🔔 Events — on");
        var off = await owner.PressAsync(silent, "🔕 Events — silent");
        var on = await owner.PressAsync(off, "🚫 Events — off");
        var forged = await Menu.HandleAsync(new MenuAction.ChangeNotifications(NotificationCategory.Security, NotificationMode.Off), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Does.StartWith("🔔 Notifications\n\nSecurity (leaks, valves, the cleaning) and System"));
            Assert.That(page.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"🔔 Events — on", "🔔 Automation — on", "🔔 Meters — on"}));
            Assert.That(silent.Buttons.First().Text, Is.EqualTo("🔕 Events — silent"));
            Assert.That(off.Buttons.First().Text, Is.EqualTo("🚫 Events — off"));
            Assert.That(on.Buttons.First().Text, Is.EqualTo("🔔 Events — on"));
            Assert.That(forged.Title, Is.EqualTo(MenuTexts.Notifications));
            Assert.That(Preferences.ModeOf(NotificationCategory.Security), Is.EqualTo(NotificationMode.On));
        });
    }

    [Test]
    public async Task Restart_ChoiceKept()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Storage:DatabasePath"] = Path.Combine(data, "home.db")});
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);
        await SetAsync(NotificationCategory.Automation, NotificationMode.Silent);
        await SetAsync(NotificationCategory.Meters, NotificationMode.Silent);
        await SetAsync(NotificationCategory.Meters, NotificationMode.On);

        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Storage:DatabasePath"] = Path.Combine(data, "home.db")});

        var page = await Menu.HandleAsync(new MenuAction.Notifications(), CancellationToken.None);

        Assert.That(page.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"🚫 Events — off", "🔕 Automation — silent", "🔔 Meters — on"}));
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task DatabaseUnavailable_ChoiceWorksInMemory()
    {
        // A directory in place of the database file: the storage cannot be opened.
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Storage:DatabasePath"] = data});
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);

        await client.GetAsync("/megad/1?pt=0&click=1");
        await client.GetAsync(Marker);

        // System: the owner is told the database is unavailable.
        Assert.That((await SentUntilMarkerAsync()).Select(x => x.Text).Where(x => !x.StartsWith("⚠️ Database unavailable", StringComparison.Ordinal)),
                    Is.EqualTo(new[] {MarkerText}));
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Disabled_EverythingWithASound_ChoiceIgnored_NoPage()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Storage:DatabasePath"] = Path.Combine(data, "home.db")});
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);

        await StopAsync();
        await StartAsync(new Dictionary<string, string?>
                         {
                             ["Storage:DatabasePath"] = Path.Combine(data, "home.db"),
                             ["Notifications:CategoriesEnabled"] = "false",
                         });
        var dashboard = await Menu.HandleAsync(new MenuAction.Dashboard(), CancellationToken.None);
        var forged = await Menu.HandleAsync(new MenuAction.ChangeNotifications(NotificationCategory.Events, NotificationMode.Silent), CancellationToken.None);
        await client.GetAsync("/megad/1?pt=0&click=1");
        await client.GetAsync(Marker);
        var sent = await SentUntilMarkerAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Buttons.Select(x => x.Text), Has.None.EqualTo(MenuTexts.NotificationsButton));
            Assert.That(forged.Title, Is.EqualTo(MenuTexts.Dashboard));
            Assert.That(sent, Is.EqualTo(new[] {("MegaD-1: Hall switch: click", false), ("→ Hall light: toggle — OK", false), (MarkerText, false)}));
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task LeakSensorBurst_FirstAtOnce_RepeatsAsOneLineAMinute_ThenAtOnceAgain()
    {
        // The burst recorded on the shower sensor (2026-10-11), on the sensor of the example flat.
        string[] burst = ["cnt=4", "cnt=4", "cnt=5", "cnt=6", "cnt=7", "cnt=8", "cnt=9", "cnt=10", "cnt=11", "m=2&cnt=11"];
        foreach (var query in burst)
            await client.GetAsync($"/megad/1?pt=6&{query}&mdid=1");
        await client.GetAsync(Marker);
        var atOnce = await SentUntilMarkerAsync();

        time.Advance(NotificationBurstWindow);
        await SentAsync(3);
        time.Advance(NotificationBurstWindow);
        await Task.Delay(200);
        await client.GetAsync("/megad/1?pt=6&cnt=12&mdid=1");
        var sent = await SentAsync(4);

        Assert.Multiple(() =>
        {
            Assert.That(atOnce, Is.EqualTo(new[] {("MegaD-1: Leak sensor: press, cnt 4", false), (MarkerText, false)}));
            Assert.That(sent.Skip(2).Select(x => x.Text), Is.EqualTo(new[] {"MegaD-1: Leak sensor: 9 more times in a minute", "MegaD-1: Leak sensor: press, cnt 12"}));
        });
    }

    [Test]
    public async Task Topics_EachCategoryInItsTopic_WithoutATopicInTheGeneralOne()
    {
        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Telegram:NotificationTopics:Security"] = "2", ["Telegram:NotificationTopics:Events"] = "3"});

        await client.GetAsync("/megad/1?pt=0&click=1");
        await client.GetAsync("/megad/1?pt=6");
        await client.GetAsync("/megad/2?st=1");

        await SentAsync(4);
        var topics = factory.Network.Telegram.Calls.Where(IsNotification)
                            .Select(x => (x.Body["text"]!.GetValue<string>(), x.Body["message_thread_id"]?.GetValue<int>()))
                            .ToList();
        Assert.That(topics, Is.EqualTo(new (string, int?)[]
                                       {
                                           ("MegaD-1: Hall switch: click", 3),
                                           ("→ Hall light: toggle — OK", 3),
                                           ("MegaD-1: Leak sensor: press", 2),
                                           ("🔄 MegaD-2 restarted", null),
                                       }));
    }

    [Test]
    public async Task TopicUnavailable_SentToTheGeneralTopic_WithAWarning()
    {
        // The owner deleted or closed the topic of Security: Telegram rejects messages to it.
        await StopAsync();
        await StartAsync(new Dictionary<string, string?> {["Telegram:NotificationTopics:Security"] = "2"});
        factory.Network.Telegram.RejectSend = x => x["message_thread_id"]?.GetValue<int>() == 2 ? "Bad Request: message thread not found" : null;

        await client.GetAsync("/megad/1?pt=6");

        var delivered = await factory.Network.Telegram.WaitForCallAsync(x => IsNotification(x) && x.Body["message_thread_id"] == null,
                                                                        timeout: TimeSpan.FromSeconds(5), description: "the leak in the general topic");
        Assert.Multiple(() =>
        {
            Assert.That(delivered.Body["text"]!.GetValue<string>(), Does.StartWith("MegaD-1: Leak sensor: press").And.Contain("⚠️ Not delivered to the topic of Security"));
            Assert.That(delivered.Body["disable_notification"]?.GetValue<bool>(), Is.Not.True);
        });
    }

    [Test]
    public async Task RealHome_EventsOff_LeakProtectionStillArrives()
    {
        House.Real.EnsureAvailable();
        await StopAsync();
        await StartAsync(house: House.Real);
        await SetAsync(NotificationCategory.Events, NotificationMode.Off);

        // Shower: leak sensor 16, the open button 4, the open relay 22; then the guest bathroom leak sensor 18.
        await client.GetAsync("/megad/1?pt=16");
        await client.GetAsync("/megad/1?pt=4");
        await client.GetAsync("/megad/1?pt=22&mdid=1&v=1");
        await client.GetAsync("/megad/1?pt=18");

        Assert.That(await SentAsync(4), Has.Count.EqualTo(4).And.None.Matches<(string Text, bool Silent)>(x => x.Silent));
    }

    private async Task StartAsync(Dictionary<string, string?>? settings = null, House? house = null)
    {
        time = new FakeTimeProvider(start);
        var all = new Dictionary<string, string?> {["Notifications:CategoriesEnabled"] = "true", ["Automation:Enabled"] = "true"};
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        factory = new HomeServerFactory(house ?? House.Example, all, services => services.AddSingleton<TimeProvider>(time));
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
    }

    private async Task StopAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    private Task SetAsync(NotificationCategory category, NotificationMode mode) =>
        Menu.HandleAsync(new MenuAction.ChangeNotifications(category, mode), CancellationToken.None);

    /// <summary> The notifications up to the marker, which is sent last and with a sound. </summary>
    private async Task<List<(string Text, bool Silent)>> SentUntilMarkerAsync()
    {
        await factory.Network.Telegram.WaitForCallAsync(x => IsNotification(x) && x.Body["text"]!.GetValue<string>() == MarkerText, description: "the marker");
        return Notifications();
    }

    private async Task<List<(string Text, bool Silent)>> SentAsync(int count)
    {
        await factory.Network.Telegram.WaitForCallAsync(IsNotification, skip: count - 1, description: $"{count} notifications");
        await Task.Delay(100);
        return Notifications();
    }

    private List<(string Text, bool Silent)> Notifications() =>
        factory.Network.Telegram.Calls.Where(IsNotification)
               .Select(x => (x.Body["text"]!.GetValue<string>(), x.Body["disable_notification"]?.GetValue<bool>() == true))
               .ToList();

    private static bool IsNotification(TelegramCall call) =>
        call.Method == "sendMessage" && call.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId;

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (timeout.IsCancellationRequested)
                Assert.Fail($"Timed out waiting: {what}");
            await Task.Delay(20, CancellationToken.None);
        }
    }
}
