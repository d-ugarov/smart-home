using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Automations;
using SmartHome.Core.Entities;
using SmartHome.Core.Menu;
using SmartHome.Core.State;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Automations of the example flat on a fake clock: the bathroom fan follows the light (examples of the owner, 07 §6.2)
/// and forgotten switches are turned off. The light of the bathroom is a group of two lamps on two controllers.
/// </summary>
[NonParallelizable]
public class AutomationTests
{
    private const string FanOff = "fan.bathroom.off";
    private const string FanOnCheck = "held:bathroom_fan:0";

    private static readonly DateTimeOffset start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly MegaDCommand fanOn = new("1", 11, "1");
    private static readonly MegaDCommand fanOff = new("1", 11, "0");

    private FakeTimeProvider time = null!;
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    private AutomationEngine Engine => factory.Services.GetRequiredService<AutomationEngine>();
    private IReadOnlyList<MegaDCommand> Commands => factory.Network.Commands;

    [SetUp]
    public void SetUp() => Start(new FakeTimeProvider(start));

    [TearDown]
    public async Task TearDown() => await StopAsync();

    [Test]
    public async Task LightHolds5Minutes_FanOn()
    {
        await KnownStateAsync(fan: false);

        await LightAsync(true);
        await PendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(5) - HomeState.GroupSettleTime - TimeSpan.FromSeconds(1));
        await QuietAsync();
        var before = Commands.ToList();
        Advance(TimeSpan.FromSeconds(1));

        await CommandAsync(fanOn);
        Assert.That(before, Is.Empty);
    }

    [Test]
    public async Task FanAlreadyOn_NotSentAgain()
    {
        await KnownStateAsync(fan: true);

        await LightAsync(true);
        await PendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(6));
        await NoPendingAsync(FanOnCheck);
        await QuietAsync();

        Assert.That(Commands, Is.Empty);
    }

    [TestCase(10, 30, TestName = "Short visit, fan off in 30 min")]
    [TestCase(20, 30, TestName = "20 min is still a short visit")]
    [TestCase(25, 60, TestName = "Long visit, fan off in 60 min")]
    public async Task LightOff_FanOffLater(int visitMinutes, int fanMinutes)
    {
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(visitMinutes) - HomeState.GroupSettleTime);

        var lightOff = await LightAsync(false);
        var due = (await PendingAsync(FanOff)).Due;
        Advance(TimeSpan.FromMinutes(fanMinutes) - HomeState.GroupSettleTime - TimeSpan.FromSeconds(1));
        await QuietAsync();
        var before = Commands.ToList();
        Advance(TimeSpan.FromSeconds(1));

        await CommandAsync(fanOff);
        Assert.Multiple(() =>
        {
            Assert.That(due, Is.EqualTo(lightOff + TimeSpan.FromMinutes(fanMinutes)), "counts from the moment the light went out");
            Assert.That(before, Is.Empty);
        });
    }

    [Test]
    public async Task LightOnAgain_FanOffCancelled()
    {
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await LightAsync(false);
        await PendingAsync(FanOff);

        await LightAsync(true);
        await NoPendingAsync(FanOff);
        Advance(TimeSpan.FromHours(1));
        await QuietAsync();

        Assert.That(Commands, Is.Empty);
    }

    [Test]
    public async Task FanSwitchedByPerson_FanOffCancelled()
    {
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await LightAsync(false);
        await PendingAsync(FanOff);

        await OutputAsync("1", 11, false);
        await OutputAsync("1", 11, true);
        await NoPendingAsync(FanOff);
        Advance(TimeSpan.FromHours(1));
        await QuietAsync();

        Assert.That(Commands, Is.Empty);
    }

    [Test]
    public async Task FanTurnedOffByPerson_NotTurnedOnAgainUntilTheLightComesAgain()
    {
        await KnownStateAsync(fan: false);
        await LightAsync(true);
        await PendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(5));
        await CommandAsync(fanOn);

        Advance(TimeSpan.FromMinutes(2));
        await OutputAsync("1", 11, false);
        Advance(TimeSpan.FromMinutes(30));
        await QuietAsync();
        var whileTheLightIsOn = Commands.ToList();

        await LightAsync(false);
        await LightAsync(true);
        await PendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(5));

        await CommandAsync(fanOn, count: 2);
        Assert.That(whileTheLightIsOn, Is.EqualTo(new[] {fanOn}));
    }

    [Test]
    public async Task FanSwitchedByPersonBefore5Minutes_NotTouched()
    {
        await KnownStateAsync(fan: false);
        await LightAsync(true);
        await PendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(2));

        await OutputAsync("1", 11, true);
        await OutputAsync("1", 11, false);
        await NoPendingAsync(FanOnCheck);
        Advance(TimeSpan.FromMinutes(10));
        await QuietAsync();

        Assert.That(Commands, Is.Empty);
    }

    [Test]
    public async Task OtherLampOfTheGroup_TheVisitGoesOn()
    {
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(18));

        // A rule swaps the lamps: one goes off a moment before the other comes on.
        await OutputAsync("1", 10, false);
        await OutputAsync("2", 12, true);
        Advance(TimeSpan.FromMinutes(7));
        var afterSwap = Engine.Pending.Select(x => x.Key).ToList();
        var lightOff = await OutputAsync("2", 12, false);
        Advance(HomeState.GroupSettleTime);

        var off = await PendingAsync(FanOff);
        Assert.Multiple(() =>
        {
            Assert.That(afterSwap, Does.Not.Contain(FanOff));
            Assert.That(off.Due, Is.EqualTo(lightOff + TimeSpan.FromHours(1)), "25 min of light is a long visit");
        });
    }

    [Test]
    public async Task ForgottenLight_TurnedOffAfterItsTime_OwnerNotified()
    {
        await OutputAsync("1", 7, true);
        await PendingAsync("auto_off:light.hall");
        Advance(TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1));
        await QuietAsync();
        var before = Commands.ToList();
        Advance(TimeSpan.FromSeconds(1));

        await CommandAsync(new MegaDCommand("1", 7, "0"));
        var notification = await NotificationAsync("🤖");
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Empty);
            Assert.That(notification, Is.EqualTo("🤖 Auto-off → Hall light: off — OK"));
        });
    }

    [Test]
    public async Task LightTurnedOff_AutoOffCancelled()
    {
        await OutputAsync("1", 7, true);
        await PendingAsync("auto_off:light.hall");

        await OutputAsync("1", 7, false);
        await NoPendingAsync("auto_off:light.hall");
        Advance(TimeSpan.FromHours(9));
        await QuietAsync();

        Assert.That(Commands, Is.Empty);
    }

    [Test]
    public async Task Restart_ScheduledActionAndStateSurvive()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(new FakeTimeProvider(start), data);

        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await LightAsync(false);
        var due = (await PendingAsync(FanOff)).Due;
        var lightOn = await OutputAsync("1", 7, true);

        await StopAsync();
        Start(new FakeTimeProvider(start + TimeSpan.FromMinutes(20)), data);

        var restored = Engine.Pending.SingleOrDefault(x => x.Key == FanOff);
        var hall = factory.Services.GetRequiredService<HomeState>().Get("light.hall");
        Advance(due - time.GetUtcNow());

        await CommandAsync(fanOff);
        Assert.Multiple(() =>
        {
            Assert.That(restored?.Due, Is.EqualTo(due));
            Assert.That(hall, Is.EqualTo(new EntityState(new OnOffValue(OnOff.On), lightOn, StateSource.Device)), "on since before the restart");
        });
        TempDirectory.Delete(data);
    }

    [TestCase(11, true, TestName = "Late within MaxLateness - done")]
    [TestCase(13, false, TestName = "Late past MaxLateness - dropped")]
    public async Task Restart_ServerWasDown_LateAction(int hoursDown, bool done)
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(new FakeTimeProvider(start), data);
        // The auto-off of the fan falls due during the downtime as well: off, so it does not mix in.
        await Engine.SwitchAsync(Automation.AutoOffId, false, CancellationToken.None);
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await LightAsync(false);
        await PendingAsync(FanOff);

        await StopAsync();
        Start(new FakeTimeProvider(start + TimeSpan.FromHours(hoursDown)), data);

        if (done)
        {
            await CommandAsync(fanOff);
        }
        else
        {
            Assert.That(await NotificationAsync("⚠️"), Is.EqualTo("⚠️ Bathroom fan follows the light: Bathroom fan: off not done, 12 h 20 min late (the server was down)"));
            Assert.That(Commands, Is.Empty);
        }

        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Menu_ShowsScheduled_TurningOffCancelsIt()
    {
        var menu = factory.Services.GetRequiredService<MenuService>();
        await KnownStateAsync(fan: true);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await LightAsync(false);
        await PendingAsync(FanOff);
        Advance(TimeSpan.FromMinutes(5));

        var dashboard = await menu.HandleAsync(new MenuAction.Dashboard(), CancellationToken.None);
        var page = await menu.HandleAsync(new MenuAction.Automations(), CancellationToken.None);
        var turnedOff = await menu.HandleAsync(new MenuAction.ToggleAutomation("bathroom_fan"), CancellationToken.None);
        Advance(TimeSpan.FromHours(1));
        await QuietAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Buttons.Last().Text, Is.EqualTo("⚙️ Automations"));
            Assert.That(page.Paragraphs.Single(), Does.StartWith("Scheduled:\n• 25 min — Bathroom fan: off (Bathroom fan follows the light)"));
            Assert.That(page.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"🟢 Bathroom fan follows the light", "🟢 Auto-off"}));
            Assert.That(turnedOff.Buttons[0].Text, Is.EqualTo("🔘 Bathroom fan follows the light"));
            Assert.That(turnedOff.Paragraphs.Single(), Does.Not.Contain("(Bathroom fan follows the light)"));
            Assert.That(Commands, Is.Empty);
        });
    }

    [Test]
    public async Task AutomationTurnedOff_StaysOffAfterRestart()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(new FakeTimeProvider(start), data);
        await factory.Services.GetRequiredService<MenuService>().HandleAsync(new MenuAction.ToggleAutomation("bathroom_fan"), CancellationToken.None);

        await StopAsync();
        Start(new FakeTimeProvider(start), data);
        await KnownStateAsync(fan: false);
        await LightAsync(true);
        Advance(TimeSpan.FromMinutes(10));
        await QuietAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Engine.IsOn("bathroom_fan"), Is.False);
            Assert.That(Commands, Is.Empty);
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task AutomationsOff_NothingHappens_NoMenu()
    {
        await StopAsync();
        Start(new FakeTimeProvider(start), settings: new Dictionary<string, string?> {["Automation:Enabled"] = "false"});

        await KnownStateAsync(fan: false);
        await LightAsync(true);
        await OutputAsync("1", 7, true);
        Advance(TimeSpan.FromHours(9));
        await QuietAsync();
        var dashboard = await factory.Services.GetRequiredService<MenuService>().HandleAsync(new MenuAction.Automations(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(Engine.Pending, Is.Empty);
            Assert.That(Commands, Is.Empty);
            Assert.That(dashboard.Buttons.Select(x => x.Text), Does.Not.Contain("⚙️ Automations"));
        });
    }

    private void Start(FakeTimeProvider clock, string? dataDirectory = null, Dictionary<string, string?>? settings = null)
    {
        time = clock;
        var all = new Dictionary<string, string?> {["Automation:Enabled"] = "true"};
        if (dataDirectory != null)
            all["Storage:DatabasePath"] = Path.Combine(dataDirectory, "home.db");
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        factory = new HomeServerFactory(House.Example, all, services => services.AddSingleton<TimeProvider>(clock));
        client = factory.CreateClient();
    }

    private async Task StopAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    private void Advance(TimeSpan delta) => time.Advance(delta);

    /// <summary> States are known only after a reading or a report: without this the first change is not a change. </summary>
    private async Task KnownStateAsync(bool fan)
    {
        await OutputAsync("1", 10, false);
        await OutputAsync("2", 12, false);
        await OutputAsync("1", 11, fan);
        Advance(HomeState.GroupSettleTime);
        await WaitUntilAsync(() => factory.Services.GetRequiredService<HomeState>().Get("group.bathroom_light") != null, "the group is known");
    }

    /// <summary> The bathroom light (one lamp of the group); the group follows after <see cref="HomeState.GroupSettleTime"/>. </summary>
    private async Task<DateTimeOffset> LightAsync(bool on)
    {
        var at = await OutputAsync("1", 10, on);
        Advance(HomeState.GroupSettleTime);
        return at;
    }

    /// <summary> The controller reports an output switched without the server (its own scenario, a person); returns the time. </summary>
    private async Task<DateTimeOffset> OutputAsync(string device, int port, bool on)
    {
        factory.Network.Devices[device].Set(port, on);
        await client.GetAsync($"/megad/{device}?pt={port}&mdid=1&v={(on ? 1 : 0)}");
        return time.GetUtcNow();
    }

    private async Task<DelayedAction> PendingAsync(string key)
    {
        await WaitUntilAsync(() => Engine.Pending.Any(x => x.Key == key), $"{key} scheduled");
        return Engine.Pending.Single(x => x.Key == key);
    }

    private Task NoPendingAsync(string key) => WaitUntilAsync(() => Engine.Pending.All(x => x.Key != key), $"{key} cancelled");

    private Task CommandAsync(MegaDCommand command, int count = 1) =>
        WaitUntilAsync(() => Commands.Count(x => x == command) >= count, $"{count} × {command}");

    /// <summary> Lets the background loops catch up before checking that nothing happened. </summary>
    private static Task QuietAsync() => Task.Delay(300);

    private async Task<string> NotificationAsync(string prefix)
    {
        var call = await factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage" && x.Body["text"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal),
                                                                   description: $"notification {prefix}");
        return call.Body["text"]!.GetValue<string>();
    }

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
