using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Entities;
using SmartHome.Core.Home;
using SmartHome.Core.Menu;
using SmartHome.Core.Meters;
using SmartHome.Core.Storage;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;
using System.Text.RegularExpressions;

namespace SmartHome.Tests.Contract;

/// <summary>
/// The meters of the example flat: cold and hot water of the bathroom on MegaD-2 inputs 2 and 3 (P&amp;R: the counter steps
/// on both edges of a pulse, 10 L a pulse), the day and the night of the electricity entered from the menu. Local time of
/// the house (London); the tests start on Saturday 10 Oct 2026, 13:00, the cold water counter at 1200.
/// </summary>
[NonParallelizable]
public class MeterTests
{
    private const string ColdId = "meter.bathroom_cold_water";
    private const string ColdName = "Bathroom, cold";
    private const int ColdPort = 2;

    private static readonly DateTimeOffset start = new(2026, 10, 10, 13, 0, 0, TimeSpan.FromHours(1));
    private static readonly TimeSpan pollInterval = TimeSpan.FromSeconds(10);

    private FakeTimeProvider time = null!;
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;
    private BotSession owner = null!;

    private FakeMegaD MegaD2 => factory.Network.Devices["2"];
    private MeterService Meters => factory.Services.GetRequiredService<MeterService>();
    private Entity Cold => factory.Services.GetRequiredService<HomeModel>().FindEntity(ColdId)!;

    [SetUp]
    public Task SetUp() => StartAsync(start);

    [TearDown]
    public async Task TearDown() => await StopAsync();

    [Test]
    public async Task Dial_ThenAPulse_TwoStepsOfTheCounter()
    {
        var saved = await EnterAsync(ColdName, "✏️ By the dial", "100");

        int[] answers =
        [
            await PulseAsync(1201),
            await PulseAsync(1201, "&m=2"),
            await PulseAsync(1202, "&m=1"),
        ];
        await ReadingAsync(Cold, 100.010m);
        var page = await MeterPageAsync(ColdName);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Text, Does.StartWith($"💧 {ColdName}\n\n✅ Saved: 100.000 m³.\n\nNow: 100.000 m³"));
            Assert.That(answers, Is.All.EqualTo(204), "the meters have no scenarios, the controller is answered as in master");
            Assert.That(page.Text, Does.Contain("Now: 100.010 m³"));
        });
    }

    [Test]
    public async Task LostRequests_NoWaterLost_ARepeatedOne_NotCountedTwice()
    {
        await EnterAsync(ColdName, "✏️ By the dial", "100");

        await PulseAsync(1210);
        await PulseAsync(1210);
        await ReadingAsync(Cold, 100.050m);
        var page = await MeterPageAsync(ColdName);

        Assert.That(page.Text, Does.Contain("Now: 100.050 m³"));
    }

    [Test]
    public async Task ForgedRequest_AnyCount_ReadingFollowsTheController()
    {
        await EnterAsync(ColdName, "✏️ By the dial", "100");

        // Anyone in the network can call /megad: the count of a request is not the controller's word.
        await PulseAsync(21200, setCounter: false);
        await PulseAsync(-5, setCounter: false);
        MegaD2.SetCounter(ColdPort, 1202);
        var page = await MeterPageAsync(ColdName);

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Does.Contain("Now: 100.010 m³"));
            Assert.That(Notifications(), Has.None.Contains("went back"));
        });
    }

    [Test]
    public async Task CounterGoesRound_FromTheTopToZero_Counted()
    {
        MegaD2.SetCounter(ColdPort, 65534);
        await EnterAsync(ColdName, "✏️ By the dial", "100");

        await PulseAsync(65535);
        await ReadingAsync(Cold, 100.005m);
        await PulseAsync(1);

        await ReadingAsync(Cold, 100.015m);
    }

    [Test]
    public async Task ControllerRestarted_CounterFromZero_CountedFromIt_NoWarning()
    {
        await EnterAsync(ColdName, "✏️ By the dial", "100");

        MegaD2.SetCounter(ColdPort, 0);
        await client.GetAsync("/megad/2?st=1");
        await NotificationAsync("🔄 MegaD-2 restarted");
        await PulseAsync(2);
        await ReadingAsync(Cold, 100.010m);
        await NotificationAsync($"MegaD-2: {ColdName}: press, cnt 2");

        Assert.That(Notifications(), Has.None.Contains("went back"));
    }

    [Test]
    public async Task CounterWentBack_WithoutARestart_CountedAsOne_OwnerWarned()
    {
        await EnterAsync(ColdName, "✏️ By the dial", "100");

        await PulseAsync(40);
        await ReadingAsync(Cold, 100.200m);
        var warning = await NotificationAsync($"⚠️ {ColdName}");

        Assert.That(warning.Body["text"]!.GetValue<string>(),
                    Does.StartWith($"⚠️ {ColdName}: the counter of the input went back from 1200 to 40 without a restart of the controller the server saw"));
    }

    [Test]
    public async Task ServerRestarted_ReadingBack_PulsesWhileItWasDown_Counted()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, data);
        await EnterAsync(ColdName, "✏️ By the dial", "100");
        await PulseAsync(1204);
        await ReadingAsync(Cold, 100.020m);

        await StopAsync();
        await StartAsync(start.AddHours(1), data);
        var restored = Meters.ReadingOf(Cold).Value;
        MegaD2.SetCounter(ColdPort, 1300);
        var page = await MeterPageAsync(ColdName);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.EqualTo(100.020m));
            Assert.That(page.Text, Does.Contain("Now: 100.500 m³"));
        });
        await StopAsync();
        TempDirectory.Delete(data);
        await StartAsync(start);
    }

    [Test]
    public async Task Statistics_TodayYesterdayMonths_FromTheEndsOfTheDays()
    {
        await EnterAsync(ColdName, "✏️ By the dial", "100");
        await PulseAsync(1220);
        await ReadingAsync(Cold, 100.100m);
        time.Advance(TimeSpan.FromDays(1));
        await PulseAsync(1230);
        await ReadingAsync(Cold, 100.150m);
        time.Advance(TimeSpan.FromDays(22));
        await PulseAsync(1234);
        await ReadingAsync(Cold, 100.170m);

        var meter = await MeterPageAsync(ColdName);
        var year = await owner.PressAsync(meter, "📅 12 months");
        var all = await MetersPageAsync();
        var lastMonth = await owner.PressAsync(all, "📅 Last month");
        var totals = await owner.PressAsync(lastMonth, "📅 12 months");

        Assert.Multiple(() =>
        {
            Assert.That(Squeeze(meter.Text), Does.Contain("Today 0.020\nYesterday 0.000\nNovember 2026 0.020\nOctober 2026 0.150*\n\n* since 10.10.2026"));
            Assert.That(Squeeze(year.Text), Does.Contain(" used reading\nNov 2026 0.020 100.170\nOct 2026 0.150* 100.150\n\n* since 10.10.2026"));
            Assert.That(Squeeze(all.Text), Does.Contain("💧 Water, m³ now November\nBathroom, cold 100.170 0.020\nBathroom, hot — —"));
            Assert.That(Squeeze(all.Text), Does.Contain("⚡ Electricity, kWh now November\nElectricity, day (T1) — —\nElectricity, night (T2) — —\n\n" +
                                                         "Electricity, day (T1): not entered yet\nElectricity, night (T2): not entered yet"));
            Assert.That(Squeeze(lastMonth.Text), Does.Contain("💧 Water, m³ 31.10 October\nBathroom, cold 100.150 0.150*\nBathroom, hot — —\n\n* since 10.10.2026"));
            Assert.That(Squeeze(totals.Text), Does.Contain(" cold, m³ hot, m³ kWh\nNov 2026 0.020 — —\nOct 2026 0.150* — —\n\n* since 10.10.2026"));
            Assert.That(all.Buttons.Select(x => x.Text), Is.EqualTo(new[]
                                                                {
                                                                    "💧 Bathroom, cold", "💧 Bathroom, hot", "⚡ Electricity, day (T1)", "⚡ Electricity, night (T2)",
                                                                }));
        });
    }

    [Test]
    public async Task Electricity_DayThenNight_EnteredOneAfterTheOther()
    {
        var day = await EnterAsync("Electricity, day (T1)", "✏️ Enter the reading", "12345.6");
        var keypad = await owner.PressAsync(day, "✏️ Next: Electricity, night (T2)");
        var night = await SaveAsync(await TypeAsync(keypad, "6789"));
        var entered = await NotificationAsync("✏️ Electricity, day (T1)");
        var all = await MetersPageAsync();

        Assert.Multiple(() =>
        {
            Assert.That(day.Text, Does.StartWith("⚡ Electricity, day (T1)\n\n✅ Saved: 12345.6 kWh.\n\nEntered 10.10.2026: 12345.6 kWh"));
            Assert.That(keypad.Text, Does.StartWith("✏️ Electricity, night (T2)\n\nThe reading, kWh:"));
            Assert.That(night.Text, Does.StartWith("⚡ Electricity, night (T2)\n\n✅ Saved: 6789 kWh."));
            Assert.That(night.Buttons.Select(x => x.Text), Has.None.StartsWith("✏️ Next"), "the night is the last one");
            Assert.That(entered.Body["text"]!.GetValue<string>(), Is.EqualTo("✏️ Electricity, day (T1): 12345.6 kWh entered"));
            Assert.That(Squeeze(all.Text), Does.Contain("Electricity, day (T1) 12345.6 0*\nElectricity, night (T2) 6789 0*\n\n* since 10.10.2026\n" +
                                                         "Electricity, day (T1): entered 10.10.2026\nElectricity, night (T2): entered 10.10.2026"));
        });
    }

    [Test]
    public async Task Keypad_Typing()
    {
        var keypad = await OpenKeypadAsync(ColdName, "✏️ By the dial");
        var typed = await TypeAsync(keypad, "0.1234");
        var erased = await owner.PressAsync(typed, "⌫");

        Assert.Multiple(() =>
        {
            Assert.That(keypad.Text, Is.EqualTo($"✏️ {ColdName}\n\nThe reading of the dial, m³:\n\n➡️ …"));
            Assert.That(keypad.Rows.Select(x => string.Join(" ", x.Select(b => b.Text))),
                        Is.EqualTo(new[] {"1 2 3", "4 5 6", "7 8 9", ". 0 ⌫", "✖️ Cancel"}), "no save before a digit");
            Assert.That(typed.Text, Does.EndWith("➡️ 0.123"), "three digits after the point");
            Assert.That(typed.Rows.SelectMany(x => x).Select(x => x.Text), Does.Contain("✅ Save 0.123 m³"));
            Assert.That(erased.Text, Does.EndWith("➡️ 0.12"));
        });
    }

    [Test]
    public async Task KeypadOfAnOldMessage_DoesNotSave()
    {
        var keypad = await TypeAsync(await OpenKeypadAsync(ColdName, "✏️ By the dial"), "100");

        time.Advance(MeterMenu.InputTimeout + TimeSpan.FromMinutes(1));
        var page = await SaveAsync(keypad);

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Is.EqualTo($"✏️ {ColdName}\n\nThis keypad is out of date. Open it again."));
            Assert.That(Meters.ReadingOf(Cold).Value, Is.Null);
        });
    }

    [Test]
    public async Task Dial_ControllerDoesNotAnswer_NotSaved_TheKeypadStays()
    {
        var keypad = await TypeAsync(await OpenKeypadAsync(ColdName, "✏️ By the dial"), "100");

        MegaD2.IsOffline = true;
        var page = await SaveAsync(keypad);

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Does.StartWith($"✏️ {ColdName}\n\n⚠️ Not saved: the controller does not answer"));
            Assert.That(page.Rows.SelectMany(x => x).Select(x => x.Text), Does.Contain("✅ Save 100.000 m³"));
            Assert.That(Meters.ReadingOf(Cold).Value, Is.Null);
        });
    }

    [Test]
    public async Task DatabaseUnavailable_KeptInMemory_TheOwnerSeesIt()
    {
        var notDatabase = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Storage:DatabasePath"] = notDatabase});

        var page = await EnterAsync(ColdName, "✏️ By the dial", "100");

        Assert.Multiple(() =>
        {
            Assert.That(page.Text, Does.StartWith($"💧 {ColdName}\n\n⚠️ 100.000 m³ is not saved in the database: it is kept until a restart of the server."));
            Assert.That(page.Text, Does.Contain("⚠️ The history is unavailable: the database is unavailable"));
            Assert.That(Meters.ReadingOf(Cold).Value, Is.EqualTo(100m));
        });
        await StopAsync();
        TempDirectory.Delete(notDatabase);
        await StartAsync(start);
    }

    [Test]
    public async Task DatabaseBackAfterTheStart_TheSavedReadingNotOverwritten()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        await StartAsync(start, data);
        await EnterAsync(ColdName, "✏️ By the dial", "100");
        await StopAsync();

        // Not opened at the start, but working a moment later: the reading unknown in memory must not be written over the saved one.
        await StartAsync(start.AddHours(1), data, services: OpenedLate);
        await PulseAsync(1210);
        await MeterPageAsync(ColdName);
        await StopAsync();
        await StartAsync(start.AddHours(2), data);
        var restored = Meters.ReadingOf(Cold).Value;

        Assert.That(restored, Is.EqualTo(100m));
        await StopAsync();
        TempDirectory.Delete(data);
        await StartAsync(start);
    }

    [Test]
    public async Task Pulse_InTheTopicOfTheMeters()
    {
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Telegram:NotificationTopics:Meters"] = "6"});

        await PulseAsync(1201);
        var pulse = await NotificationAsync($"MegaD-2: {ColdName}: press, cnt 1201");

        Assert.That(pulse.Body["message_thread_id"]?.GetValue<int>(), Is.EqualTo(6));
    }

    [Test]
    public async Task Disabled_AsMaster()
    {
        await StopAsync();
        await StartAsync(start, settings: new Dictionary<string, string?> {["Meters:Enabled"] = "false"});

        var dashboard = await owner.SendTextAsync();
        var answer = await PulseAsync(1201);
        var reported = await NotificationAsync("MegaD-2: not handled, pt=2&cnt=1201&mdid=2");
        var saved = await factory.Services.GetRequiredService<IHomeStorage>().LoadMetersAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Buttons.Select(x => x.Text), Has.None.Contains("Meters"));
            Assert.That(answer, Is.EqualTo(204));
            Assert.That(reported.Body["text"]!.GetValue<string>(), Is.EqualTo("MegaD-2: not handled, pt=2&cnt=1201&mdid=2"));
            Assert.That(saved, Is.Empty, "nothing counted");
        });
    }

    [Test]
    public async Task RealHome_FourWaterMeters_TheLoggedPulsesOfTheShower_Counted()
    {
        House.Real.EnsureAvailable();
        await StopAsync();
        await StartAsync(start, house: House.Real);
        var megaD1 = factory.Network.Devices["1"];
        megaD1.SetCounter(1, 43563);
        // Names of the real home are not in this repository: they come from its files.
        var home = factory.Services.GetRequiredService<HomeModel>();
        var meter = home.FindEntity("meter.shower_cold_water")!;

        var shower = await EnterAsync(meter.Name, "✏️ By the dial", "123.456");
        // The cold water of the shower in the request log of 2026-10-11, 03:37: a pulse is the closing, the hold and the opening.
        string[] logged = ["pt=1&cnt=43565&mdid=1", "pt=1&m=2&cnt=43565&mdid=1", "pt=1&m=1&cnt=43566&mdid=1"];
        foreach (var query in logged)
        {
            megaD1.SetCounter(1, int.Parse(Regex.Match(query, @"cnt=(\d+)").Groups[1].Value));
            await client.GetAsync($"/megad/1?{query}");
        }

        time.Advance(pollInterval);
        await ReadingAsync(meter, 123.471m);
        var all = await MetersPageAsync();
        string[] ids =
        [
            "meter.shower_hot_water", "meter.shower_cold_water", "meter.guest_bathroom_hot_water", "meter.guest_bathroom_cold_water",
            "meter.electricity_day", "meter.electricity_night",
        ];

        Assert.Multiple(() =>
        {
            Assert.That(shower.Text, Does.StartWith($"💧 {meter.Name}\n\n✅ Saved: 123.456 m³."));
            Assert.That(all.Buttons.Select(x => x.Text), Is.EqualTo(ids.Select(x => home.FindEntity(x)!).Select(x => $"{MeterMenu.Icon(x)} {x.Name}")));
            Assert.That(Squeeze(all.Text), Does.Contain($"{meter.Name} 123.471"));
        });
    }

    private async Task StartAsync(DateTimeOffset now, string? dataDirectory = null, Dictionary<string, string?>? settings = null, House? house = null,
                                  Action<IServiceCollection>? services = null)
    {
        time = new FakeTimeProvider(now);
        var all = new Dictionary<string, string?> {["Meters:Enabled"] = "true", ["Home:StatePollInterval"] = "00:00:10"};
        if (dataDirectory != null)
            all["Storage:DatabasePath"] = Path.Combine(dataDirectory, "home.db");
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        factory = new HomeServerFactory(house ?? House.Example, all, x =>
        {
            x.AddSingleton<TimeProvider>(time);
            services?.Invoke(x);
        });
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
        owner = new BotSession(factory.Network.Telegram);
    }

    private async Task StopAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    /// <summary>
    /// The request of the controller, and with <paramref name="setCounter"/> its counter and the next poll: the meters are
    /// counted by the reads of the controller, not by the requests.
    /// </summary>
    private async Task<int> PulseAsync(int count, string press = "", bool setCounter = true)
    {
        if (setCounter)
            MegaD2.SetCounter(ColdPort, count);
        var response = await client.GetAsync($"/megad/2?pt={ColdPort}{press}&cnt={count}&mdid=2");
        if (setCounter)
            time.Advance(pollInterval);
        return (int)response.StatusCode;
    }

    private async Task<BotPage> MetersPageAsync()
    {
        var dashboard = await owner.SendTextAsync();
        return await owner.PressAsync(dashboard, MenuTexts.MetersButton);
    }

    private async Task<BotPage> MeterPageAsync(string name) => await owner.PressAsync(await MetersPageAsync(), name);

    private async Task<BotPage> OpenKeypadAsync(string name, string button) => await owner.PressAsync(await MeterPageAsync(name), button);

    private async Task<BotPage> EnterAsync(string name, string button, string digits) => await SaveAsync(await TypeAsync(await OpenKeypadAsync(name, button), digits));

    private async Task<BotPage> TypeAsync(BotPage keypad, string digits)
    {
        foreach (var key in digits)
            keypad = await owner.PressAsync(keypad, key.ToString());
        return keypad;
    }

    private Task<BotPage> SaveAsync(BotPage keypad) => owner.PressAsync(keypad, keypad.Rows.SelectMany(x => x).Single(x => x.Text.StartsWith("✅ Save")).Text);

    private async Task ReadingAsync(Entity meter, decimal expected)
    {
        for (var i = 0; i < 100 && Meters.ReadingOf(meter).Value != expected; i++)
            await Task.Delay(50, CancellationToken.None);

        Assert.That(Meters.ReadingOf(meter).Value, Is.EqualTo(expected));
    }

    private Task<TelegramCall> NotificationAsync(string prefix) =>
        factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage" && x.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId &&
                                                       x.Body["text"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal),
                                                  timeout: TimeSpan.FromSeconds(5), description: $"notification {prefix}");

    private List<string> Notifications() =>
        factory.Network.Telegram.Calls.Where(x => x.Method == "sendMessage" && x.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId)
               .Select(x => x.Body["text"]!.GetValue<string>())
               .ToList();

    /// <summary> The storage of the server fails to open at the start and works afterwards (e.g. a disk mounted late). </summary>
    private static void OpenedLate(IServiceCollection services)
    {
        var original = services.Single(x => x.ServiceType == typeof(IHomeStorage));
        services.Remove(original);
        services.AddSingleton<IHomeStorage>(x => new OpenedLateStorage((IHomeStorage)ActivatorUtilities.CreateInstance(x, original.ImplementationType!)));
    }

    private sealed class OpenedLateStorage(IHomeStorage storage) : IHomeStorage
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => throw new IOException("The disk is not mounted yet");

        public Task<IReadOnlyList<StoredState>> LoadStatesAsync(CancellationToken cancellationToken) => storage.LoadStatesAsync(cancellationToken);

        public Task SaveStatesAsync(IReadOnlyCollection<StoredState> states, CancellationToken cancellationToken) => storage.SaveStatesAsync(states, cancellationToken);

        public Task<IReadOnlyList<StoredDelayedAction>> LoadDelayedActionsAsync(CancellationToken cancellationToken) => storage.LoadDelayedActionsAsync(cancellationToken);

        public Task SaveDelayedActionAsync(StoredDelayedAction action, CancellationToken cancellationToken) => storage.SaveDelayedActionAsync(action, cancellationToken);

        public Task DeleteDelayedActionAsync(string key, CancellationToken cancellationToken) => storage.DeleteDelayedActionAsync(key, cancellationToken);

        public Task<IReadOnlyDictionary<string, bool>> LoadAutomationSwitchesAsync(CancellationToken cancellationToken) =>
            storage.LoadAutomationSwitchesAsync(cancellationToken);

        public Task SaveAutomationSwitchAsync(string automationId, bool isOn, CancellationToken cancellationToken) =>
            storage.SaveAutomationSwitchAsync(automationId, isOn, cancellationToken);

        public Task<string?> LoadSettingAsync(string key, CancellationToken cancellationToken) => storage.LoadSettingAsync(key, cancellationToken);

        public Task SaveSettingAsync(string key, string? value, CancellationToken cancellationToken) => storage.SaveSettingAsync(key, value, cancellationToken);

        public Task<IReadOnlyList<StoredMeter>> LoadMetersAsync(CancellationToken cancellationToken) => storage.LoadMetersAsync(cancellationToken);

        public Task SaveMetersAsync(IReadOnlyCollection<StoredMeter> meters, IReadOnlyCollection<StoredMeterDay> days, CancellationToken cancellationToken) =>
            storage.SaveMetersAsync(meters, days, cancellationToken);

        public Task<IReadOnlyList<StoredMeterDay>> LoadMeterDaysAsync(string entityId, DateOnly from, CancellationToken cancellationToken) =>
            storage.LoadMeterDaysAsync(entityId, from, cancellationToken);
    }

    /// <summary> The tables are aligned by spaces: the tests compare the cells, not the widths. </summary>
    private static string Squeeze(string text) => Regex.Replace(text.Replace("<pre>", "").Replace("</pre>", ""), " {2,}", " ");
}
