using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Entities;
using SmartHome.Core.Home;
using SmartHome.Core.Menu;
using SmartHome.Core.Valves;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Cleaning of the bathroom of the example flat: the leak sensor (MegaD-1 input 6) runs the close scenario of the
/// controller, which the server holds back by answering 200 while the cleaning lasts. Local time of the house (London,
/// BST); the tests start on Saturday 10 Oct 2026, 13:00.
/// </summary>
[NonParallelizable]
public class CleaningTests
{
    private const string Leak = "/megad/1?pt=6";
    private const string CleaningButton = "🧽 Cleaning";

    private static readonly DateTimeOffset start = new(2026, 10, 10, 13, 0, 0, TimeSpan.FromHours(1));
    private static readonly MegaDCommand closeValves = new("1", 6, "d");
    private static readonly MegaDCommand openValves = new("1", 15, "d");

    private FakeTimeProvider time = null!;
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;
    private BotSession owner = null!;

    private FakeMegaD MegaD1 => factory.Network.Devices["1"];
    private ValveService Valves => factory.Services.GetRequiredService<ValveService>();
    private CleaningService Cleaning => factory.Services.GetRequiredService<CleaningService>();
    private IReadOnlyList<MegaDCommand> Commands => factory.Network.Commands;

    [SetUp]
    public Task SetUp() => StartAsync(start);

    [TearDown]
    public async Task TearDown() => await StopAsync();

    [Test]
    public async Task NoCleaning_LeakLeftToTheController()
    {
        var response = await client.GetAsync(Leak);

        Assert.That((int)response.StatusCode, Is.EqualTo(204), "the controller closes the valves");
    }

    [Test]
    public async Task Leak_DuringTheCleaning_ValvesNotClosed_ClosedFromTheNotification()
    {
        var water = await OpenWaterPageAsync();
        var page = await owner.PressAsync(water, CleaningButton);
        var started = await owner.PressAsync(page, "30 min");
        var waterAgain = await owner.PressAsync(started, MenuTexts.BackButton);

        var response = await client.GetAsync(Leak);
        var alert = await NotificationAsync("🚨");
        var question = await owner.PressAsync(BotPage.From(alert.Body, messageId: 77), "Close valves");
        await owner.PressAsync(question, MenuTexts.Yes);

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Does.StartWith("🧽 Bathroom water\n\nThe leak sensor closes the valves."));
            Assert.That(page.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"30 min", "1 h", "2 h"}));
            Assert.That(started.Text, Does.StartWith("🧽 Bathroom water\n\nUntil 13:30 the leak sensor does not close the valves."));
            Assert.That(started.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"30 min", "1 h", "2 h", "⏹ End the cleaning"}));
            Assert.That(waterAgain.Buttons.Select(x => x.Text), Does.Contain("🧽 Cleaning — until 13:30"));
            Assert.That((int)response.StatusCode, Is.EqualTo(200), "the controller does not close the valves");
            Assert.That(alert.Body["text"]!.GetValue<string>(), Is.EqualTo("🚨 Leak sensor fired during the cleaning — Bathroom water not closed"));
            Assert.That(question.Text, Is.EqualTo("Bathroom water\n\nClose valves?"));
            Assert.That(Commands, Is.EqualTo(new[] {closeValves}));
        });
    }

    [Test]
    public async Task DuringTheCleaning_OnlyTheLeakHeldBack()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);

        var release = await client.GetAsync(Leak + "&m=1");
        var closeRelay = await client.GetAsync("/megad/1?pt=13&mdid=1&v=1");

        Assert.Multiple(() =>
        {
            Assert.That((int)release.StatusCode, Is.EqualTo(204));
            Assert.That((int)closeRelay.StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task Over_SensorDry_NothingClosed_TheSensorClosesTheValvesAgain()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);
        var during = await client.GetAsync(Leak);
        MegaD1.Set(6, false);

        time.Advance(TimeSpan.FromMinutes(30));
        var over = await EndNotificationAsync("🧽 Bathroom water: cleaning over");
        var after = await client.GetAsync(Leak);

        Assert.Multiple(() =>
        {
            Assert.That((int)during.StatusCode, Is.EqualTo(200));
            Assert.That(over.Body["text"]!.GetValue<string>(), Is.EqualTo("🧽 Bathroom water: cleaning over, the leak sensor closes the valves again"));
            Assert.That(Commands, Is.Empty);
            Assert.That((int)after.StatusCode, Is.EqualTo(204));
            Assert.That(Cleaning.UntilOf(Valve), Is.Null);
        });
    }

    [Test]
    public async Task Over_SensorStillWet_ValvesClosed()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);
        MegaD1.Set(6, true);
        await client.GetAsync(Leak);

        time.Advance(TimeSpan.FromMinutes(30));
        await CommandAsync(closeValves);
        var over = await NotificationAsync("🚨 Bathroom water");

        Assert.That(over.Body["text"]!.GetValue<string>(), Is.EqualTo("🚨 Bathroom water: cleaning over, the leak sensor is still wet — closing the valves"));
    }

    [Test]
    public async Task Over_ContactFlickers_WetOnTheSecondRead_ValvesClosed()
    {
        // A wet contact reads "off" now and then (the shower sensor, 2026-10-11): one "on" of the end reads is wet.
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Home:StatePollInterval"] = "1.00:00:00"});
        await StartCleaningAsync(CleaningChange.HalfHour);
        var reads = 0;
        factory.Network.BeforeDeviceAnswers = (device, _) =>
        {
            if (device.DeviceId == "1" && Interlocked.Increment(ref reads) == 2)
                MegaD1.Set(6, true);
            return Task.CompletedTask;
        };

        time.Advance(TimeSpan.FromMinutes(30));
        var over = await EndNotificationAsync("🚨 Bathroom water");

        await CommandAsync(closeValves);
        Assert.That(over.Body["text"]!.GetValue<string>(), Does.Contain("still wet"));
    }

    [Test]
    public async Task Over_SensorWet_WhileTheValvesAreOpening_ClosedAfterThePulse()
    {
        // The owner opened the water from the bot ten seconds before the end: the open pulse is still running.
        await StartCleaningAsync(CleaningChange.HalfHour);
        time.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(50));
        await Valves.RunAsync(Valve, ValveCommand.Open, CancellationToken.None);
        MegaD1.Set(6, true);

        time.Advance(TimeSpan.FromSeconds(10));
        await EndNotificationAsync("🚨 Bathroom water");
        await AdvanceUntilAsync(() => Commands.Contains(closeValves), TimeSpan.FromMinutes(2));

        Assert.That(Commands, Is.EqualTo(new[] {openValves, closeValves}));
    }

    [Test]
    public async Task Over_SensorWet_CloseFails_RetriedUntilClosed()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);
        MegaD1.Set(6, true);
        var requests = 0;
        factory.Network.BeforeDeviceAnswers = (device, _) =>
        {
            // The sensor is read, then the controller drops off for a while: the close does not get through.
            if (device.DeviceId == "1" && Interlocked.Increment(ref requests) == 2)
                MegaD1.IsOffline = true;
            return Task.CompletedTask;
        };

        time.Advance(TimeSpan.FromMinutes(30));
        await NotificationAsync("💧 Bathroom water: close — ⚠️ failed");
        MegaD1.IsOffline = false;
        await AdvanceUntilAsync(() => Commands.Contains(closeValves), TimeSpan.FromMinutes(2));

        Assert.That(Commands, Is.EqualTo(new[] {closeValves}));
    }

    [Test]
    public async Task LeakBurst_DuringTheCleaning_OneAlertThenTheCount()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);

        foreach (var cnt in new[] {4, 4, 5, 6})
            await client.GetAsync($"{Leak}&cnt={cnt}&mdid=1");
        var first = await NotificationAsync("🚨");
        time.Advance(TimeSpan.FromMinutes(1));
        var count = await NotificationAsync("🚨 Leak sensor fired 3 more times");

        Assert.Multiple(() =>
        {
            Assert.That(Alerts(), Has.Count.EqualTo(2), "one alert and one count, not four alerts");
            Assert.That(first.Body["text"]!.GetValue<string>(), Is.EqualTo("🚨 Leak sensor fired during the cleaning — Bathroom water not closed"));
            Assert.That(count.Body["text"]!.GetValue<string>(), Is.EqualTo("🚨 Leak sensor fired 3 more times during the cleaning — Bathroom water not closed"));
            Assert.That(BotPage.From(count.Body, messageId: 78).Buttons.Select(x => x.Text), Is.EqualTo(new[] {"Close valves"}));
        });
    }

    [Test]
    public async Task LeakWhileTheEndIsBeingChecked_LeftToTheController()
    {
        // The check at the end waits for the controller; a leak in the meantime is the controller's already.
        await StartCleaningAsync(CleaningChange.HalfHour);
        var held = new TaskCompletionSource();
        factory.Network.BeforeDeviceAnswers = async (device, ct) =>
        {
            if (device.DeviceId == "1")
                await held.Task.WaitAsync(ct);
        };

        time.Advance(TimeSpan.FromMinutes(30));
        var response = await client.GetAsync(Leak);
        held.SetResult();

        Assert.That((int)response.StatusCode, Is.EqualTo(204));
    }

    [Test]
    public async Task EndedFromTheMenu_SensorCheckedAtOnce()
    {
        var page = await StartCleaningAsync(CleaningChange.TwoHours);
        MegaD1.Set(6, true);

        var ended = await Menu.HandleAsync(page.Buttons.Single(x => x.Text == "⏹ End the cleaning").Action, CancellationToken.None);
        var after = await client.GetAsync(Leak);
        await CommandAsync(closeValves);

        Assert.Multiple(() =>
        {
            Assert.That(ended.Paragraphs.Single(), Does.StartWith("The leak sensor closes the valves."));
            Assert.That(Commands, Is.EqualTo(new[] {closeValves}));
            Assert.That((int)after.StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task EndedFromTheMenu_ControllerSlow_TheBotNotHeldUp_ProtectionBackAtOnce()
    {
        // The bot handles one press at a time: the end must not wait for the sensor and the valves.
        var page = await StartCleaningAsync(CleaningChange.TwoHours);
        MegaD1.Set(6, true);
        var controller = new TaskCompletionSource();
        factory.Network.BeforeDeviceAnswers = async (device, ct) =>
        {
            if (device.DeviceId == "1")
                await controller.Task.WaitAsync(ct);
        };

        var ending = Menu.HandleAsync(page.Buttons.Single(x => x.Text == "⏹ End the cleaning").Action, CancellationToken.None);
        var returned = await Task.WhenAny(ending, Task.Delay(TimeSpan.FromSeconds(3))) == ending;
        var heldBack = Cleaning.HoldsBack(Valve, factory.Services.GetRequiredService<HomeModel>().FindEntity("sensor.bathroom_leak")!);
        controller.SetResult();

        await CommandAsync(closeValves);
        Assert.Multiple(() =>
        {
            Assert.That(returned, Is.True, "the press returned while the controller was still answering");
            Assert.That(heldBack, Is.False, "the sensor closes the valves again");
        });
    }

    [Test]
    public async Task PressedAgain_TheEndSetFromNow()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);
        time.Advance(TimeSpan.FromMinutes(20));
        var moved = await StartCleaningAsync(CleaningChange.Hour);

        time.Advance(TimeSpan.FromMinutes(15));
        var afterTheFirstEnd = await client.GetAsync(Leak);
        time.Advance(TimeSpan.FromMinutes(45));
        await EndNotificationAsync("🧽 Bathroom water: cleaning over");
        var afterTheNewEnd = await client.GetAsync(Leak);

        Assert.Multiple(() =>
        {
            Assert.That(moved.Paragraphs.Single(), Does.StartWith("Until 14:20 "));
            Assert.That((int)afterTheFirstEnd.StatusCode, Is.EqualTo(200), "13:35: still cleaning");
            Assert.That((int)afterTheNewEnd.StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task Restart_CleaningGoesOn_EndPassedWhileDown_CheckedAfterTheStart()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, data);
        await StartCleaningAsync(CleaningChange.HalfHour);

        await StopAsync();
        await StartAsync(start + TimeSpan.FromMinutes(10), data);
        var afterRestart = await client.GetAsync(Leak);

        await StopAsync();
        await StartAsync(start + TimeSpan.FromHours(1), data, sensorWet: true);
        await CommandAsync(closeValves);

        Assert.That((int)afterRestart.StatusCode, Is.EqualTo(200), "13:10: still cleaning");
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Restart_WhileTheEndIsBeingChecked_CheckedAgainAfterTheStart()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, data);
        await StartCleaningAsync(CleaningChange.HalfHour);
        var reading = new TaskCompletionSource();
        factory.Network.BeforeDeviceAnswers = async (device, ct) =>
        {
            reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        };

        time.Advance(TimeSpan.FromMinutes(30));
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopAsync();
        await StartAsync(start + TimeSpan.FromMinutes(32), data, sensorWet: true);

        await CommandAsync(closeValves);
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task DatabaseUnavailable_CleaningWorksInMemory()
    {
        // A directory in place of the database file: the storage cannot be opened.
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Storage:DatabasePath"] = data});

        await StartCleaningAsync(CleaningChange.HalfHour);
        var during = await client.GetAsync(Leak);
        time.Advance(TimeSpan.FromMinutes(30));
        await EndNotificationAsync("🧽 Bathroom water: cleaning over");
        var after = await client.GetAsync(Leak);

        Assert.Multiple(() =>
        {
            Assert.That((int)during.StatusCode, Is.EqualTo(200));
            Assert.That((int)after.StatusCode, Is.EqualTo(204));
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task ControllerUnreachableAtTheEnd_OwnerWarned()
    {
        await StartCleaningAsync(CleaningChange.HalfHour);
        MegaD1.IsOffline = true;

        time.Advance(TimeSpan.FromMinutes(30));
        var warning = await EndNotificationAsync("⚠️ Bathroom water");

        Assert.That(warning.Body["text"]!.GetValue<string>(),
                    Is.EqualTo("⚠️ Bathroom water: cleaning over, the leak sensor could not be read — check it: a sensor still wet does not close the valves"));
    }

    [Test]
    public async Task Disabled_NoButton_LeakLeftToTheController_ForgedButtonOpensTheDashboard()
    {
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Cleaning:Enabled"] = "false"});

        var water = await OpenWaterPageAsync();
        var forged = await Menu.HandleAsync(new MenuAction.ChangeCleaning("bathroom_water", "valve.bathroom", CleaningChange.TwoHours), CancellationToken.None);
        var response = await client.GetAsync(Leak);

        Assert.Multiple(() =>
        {
            Assert.That(water.Buttons.Where(x => x.Text.Contains("Cleaning", StringComparison.Ordinal)), Is.Empty);
            Assert.That(forged.Title, Is.EqualTo(MenuTexts.Dashboard));
            Assert.That(Cleaning.UntilOf(Valve), Is.Null);
            Assert.That((int)response.StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task RealHome_ShowerCleaned_GuestBathroomStillProtected()
    {
        House.Real.EnsureAvailable();
        await StopAsync();
        await StartAsync(start, house: House.Real);

        await Menu.HandleAsync(new MenuAction.ChangeCleaning("shower_water", "valve.shower", CleaningChange.HalfHour), CancellationToken.None);
        var shower = await client.GetAsync("/megad/1?pt=16");
        var guestBathroom = await client.GetAsync("/megad/1?pt=18");

        Assert.Multiple(() =>
        {
            Assert.That((int)shower.StatusCode, Is.EqualTo(200));
            Assert.That((int)guestBathroom.StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task RealHome_RecordedShowerLeak_HeldBackDuringTheCleaning_LeftToTheControllerAfter()
    {
        // Requests of the shower sensor shorted with foil (MegaD-1, 2026-10-11): presses with a counter, the same press
        // once more after the scenario turned the close relay on, a hold (m=2). Only presses run the close scenario.
        House.Real.EnsureAvailable();
        await StopAsync();
        await StartAsync(start, house: House.Real);
        string[] recorded = ["pt=16&cnt=4&mdid=1", "pt=16&cnt=4&mdid=1", "pt=16&cnt=5&mdid=1", "pt=16&m=2&cnt=11&mdid=1"];

        await Menu.HandleAsync(new MenuAction.ChangeCleaning("shower_water", "valve.shower", CleaningChange.HalfHour), CancellationToken.None);
        var during = new List<int>();
        foreach (var query in recorded)
            during.Add((int)(await client.GetAsync($"/megad/1?{query}")).StatusCode);
        // The dry sensor is read three times a second apart: the fake clock is moved on while the end waits.
        var ending = Menu.HandleAsync(new MenuAction.ChangeCleaning("shower_water", "valve.shower", CleaningChange.End), CancellationToken.None);
        for (var i = 0; i < 50 && !ending.IsCompleted; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50, CancellationToken.None);
        }
        await ending.WaitAsync(TimeSpan.FromSeconds(5));
        var after = new List<int>();
        foreach (var query in recorded)
            after.Add((int)(await client.GetAsync($"/megad/1?{query}")).StatusCode);

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.EqualTo(new[] {200, 200, 200, 204}), "a hold does not run the scenario: 204 is safe");
            Assert.That(after, Is.EqualTo(new[] {204, 204, 204, 204}));
        });
    }

    private List<TelegramCall> Alerts() => factory.Network.Telegram.Calls.Where(x => IsNotification(x, "🚨")).ToList();

    private MenuService Menu => factory.Services.GetRequiredService<MenuService>();

    private Entity Valve => factory.Services.GetRequiredService<HomeModel>().FindEntity("valve.bathroom")!;

    private async Task StartAsync(DateTimeOffset now, string? dataDirectory = null, Dictionary<string, string?>? settings = null, bool sensorWet = false,
                                  House? house = null)
    {
        time = new FakeTimeProvider(now);
        var all = new Dictionary<string, string?> {["Cleaning:Enabled"] = "true"};
        if (dataDirectory != null)
            all["Storage:DatabasePath"] = Path.Combine(dataDirectory, "home.db");
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        factory = new HomeServerFactory(house ?? House.Example, all, services => services.AddSingleton<TimeProvider>(time));
        if (house == null)
            MegaD1.Set(6, sensorWet);
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
        owner = new BotSession(factory.Network.Telegram);
    }

    private async Task StopAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    private Task<MenuPage> StartCleaningAsync(CleaningChange change) =>
        Menu.HandleAsync(new MenuAction.ChangeCleaning("bathroom_water", "valve.bathroom", change), CancellationToken.None);

    private async Task<BotPage> OpenWaterPageAsync()
    {
        var dashboard = await owner.SendTextAsync();
        var rooms = await owner.PressAsync(dashboard, "🏠 Rooms");
        var bathroom = await owner.PressAsync(rooms, "Bathroom" + BotPage.NavigationMark);
        return await owner.PressAsync(bathroom, "💧 Water" + BotPage.NavigationMark);
    }

    private Task<TelegramCall> NotificationAsync(string prefix) =>
        factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage" && x.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId &&
                                                       x.Body["text"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal),
                                                  timeout: TimeSpan.FromSeconds(5), description: $"notification {prefix}");

    /// <summary>
    /// A dry or unread sensor is read <see cref="CleaningService.EndReads"/> times a second apart: the fake clock is moved on
    /// until the end is reported. A second too many does not matter - the cleaning is over by then.
    /// </summary>
    private async Task<TelegramCall> EndNotificationAsync(string prefix)
    {
        for (var i = 0; i < CleaningService.EndReads * 5; i++)
        {
            await Task.Delay(100, CancellationToken.None);
            if (factory.Network.Telegram.Calls.FirstOrDefault(x => IsNotification(x, prefix)) is { } found)
                return found;
            time.Advance(TimeSpan.FromSeconds(1));
        }

        return await NotificationAsync(prefix);
    }

    /// <summary> Moves the fake clock a second at a time while the server works through its retries. </summary>
    private async Task AdvanceUntilAsync(Func<bool> condition, TimeSpan limit)
    {
        for (var passed = TimeSpan.Zero; !condition() && passed < limit; passed += TimeSpan.FromSeconds(1))
        {
            await Task.Delay(20, CancellationToken.None);
            time.Advance(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(100, CancellationToken.None);
    }

    private static bool IsNotification(TelegramCall call, string prefix) =>
        call.Method == "sendMessage" && call.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId &&
        call.Body["text"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal);

    private Task CommandAsync(MegaDCommand command) => WaitUntilAsync(() => Commands.Contains(command), $"{command}");

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
