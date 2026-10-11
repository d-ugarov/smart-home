using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Automations;
using SmartHome.Core.Menu;
using SmartHome.Tests.Fakes;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Schedules of the example flat on a fake clock, in the local time of the house (London, BST till 25 Oct): the floor
/// heating is on 21:00-03:00 every night, from 07:00 on weekdays and from 09:00 at the weekend till 11:00. The tests start
/// on Saturday 10 Oct 2026, 13:00.
/// </summary>
[NonParallelizable]
public class ScheduleTests
{
    private const string Schedule = "floor_heating";
    private const string Heating = "🤖 Floor heating → Floor heating:";

    private static readonly DateTimeOffset start = Local(10, 13);
    private static readonly MegaDCommand heatingOn = new("2", 11, "1");
    private static readonly MegaDCommand heatingOff = new("2", 11, "0");

    private FakeTimeProvider time = null!;
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;

    private AutomationEngine Engine => factory.Services.GetRequiredService<AutomationEngine>();
    private MenuService Menu => factory.Services.GetRequiredService<MenuService>();
    private IReadOnlyList<MegaDCommand> Commands => factory.Network.Commands;

    [SetUp]
    public void SetUp() => Start(start);

    [TearDown]
    public async Task TearDown() => await StopAsync();

    [Test]
    public async Task Runs_OverMidnight_ByDaysOfTheWeek()
    {
        (DateTimeOffset At, MegaDCommand Command)[] expected =
        [
            (Local(10, 21), heatingOn),
            (Local(11, 3), heatingOff),
            (Local(11, 9), heatingOn), // Sunday: the weekend rule, not the weekday one
            (Local(11, 11), heatingOff),
            (Local(11, 21), heatingOn),
            (Local(12, 3), heatingOff),
            (Local(12, 7), heatingOn), // Monday
            (Local(12, 11), heatingOff),
        ];
        await ArmedAsync();

        var early = new List<MegaDCommand>();
        foreach (var (at, command) in expected)
        {
            var count = Commands.Count;
            Advance(at - TimeSpan.FromMinutes(1) - time.GetUtcNow());
            await QuietAsync();
            early.AddRange(Commands.Skip(count));

            Advance(TimeSpan.FromMinutes(1));
            await WaitUntilAsync(() => Commands.Count > count, $"{command} at {at:u}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.Empty, "nothing before its time");
            Assert.That(Commands, Is.EqualTo(expected.Select(x => x.Command)));
        });
    }

    [Test]
    public async Task Vacation_NothingTurnedOn_TurningOffGoesOn_EndsOnItsDay()
    {
        await ArmedAsync();
        await Menu.HandleAsync(new MenuAction.ChangeVacation(VacationChange.PlusDay), CancellationToken.None);
        var page = await Menu.HandleAsync(new MenuAction.ChangeVacation(VacationChange.PlusDay), CancellationToken.None);

        await RunUntilAsync(Local(10, 22));
        var vacationOn = await NotificationAsync(Heating);
        // Someone came by and turned it on: turning off still goes on.
        await OutputAsync("2", 11, true);
        await RunUntilAsync(Local(11, 4));
        var inTheVacation = Commands.ToList();
        await RunUntilAsync(Local(12, 8));

        Assert.Multiple(() =>
        {
            Assert.That(page.Paragraphs.Single(), Does.StartWith("Until Mon 12 Oct: schedules turn nothing on"));
            Assert.That(vacationOn, Is.EqualTo($"{Heating} on — skipped, vacation"));
            Assert.That(inTheVacation, Is.EqualTo(new[] {heatingOff}), "Sunday 03:00");
            Assert.That(Commands, Is.EqualTo(new[] {heatingOff, heatingOn}), "on again on Monday 07:00");
            Assert.That(Engine.VacationUntil, Is.Null, "over");
        });
    }

    [Test]
    public async Task Vacation_ShortenedAndEndedFromTheMenu_SurvivesRestart()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        await Menu.HandleAsync(new MenuAction.ChangeVacation(VacationChange.PlusWeek), CancellationToken.None);
        var shortened = await Menu.HandleAsync(new MenuAction.ChangeVacation(VacationChange.MinusDay), CancellationToken.None);
        await StopAsync();
        Start(start, data);
        var restored = Engine.VacationUntil;
        var schedules = await Menu.HandleAsync(new MenuAction.Schedules(), CancellationToken.None);
        var ended = await Menu.HandleAsync(new MenuAction.ChangeVacation(VacationChange.End), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(shortened.Paragraphs.Single(), Does.StartWith("Until Fri 16 Oct"));
            Assert.That(restored, Is.EqualTo(new DateOnly(2026, 10, 16)));
            Assert.That(schedules.Paragraphs[0], Is.EqualTo("🏖 Vacation until Fri 16 Oct: schedules turn nothing on, turning off goes on."));
            Assert.That(ended.Paragraphs.Single(), Does.StartWith("No vacation."));
            Assert.That(ended.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"+1 day", "+1 week"}));
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task RunMovedOnce_RunsAtTheNewTime_NextDayAsScheduled()
    {
        await ArmedAsync();
        var page = await Menu.HandleAsync(new MenuAction.Schedules(), CancellationToken.None);
        var run = await Menu.HandleAsync(page.Buttons[0].Action, CancellationToken.None);
        var later = await Menu.HandleAsync(run.Buttons.Single(x => x.Text == "+1 h").Action, CancellationToken.None);
        await Menu.HandleAsync(later.Buttons.Single(x => x.Text == "−1 h").Action, CancellationToken.None);
        var moved = await Menu.HandleAsync(later.Buttons.Single(x => x.Text == "−1 h").Action, CancellationToken.None);
        var oldButton = later.Buttons.Single(x => x.Text == "+1 h").Action;

        Advance(Local(10, 19, 59) - time.GetUtcNow());
        await QuietAsync();
        var before = Commands.ToList();
        Advance(TimeSpan.FromMinutes(1));
        await CommandAsync(heatingOn);
        await WaitUntilAsync(() => Engine.Pending.Any(x => x.Key == $"time:{Schedule}:0" && x.Due > time.GetUtcNow()), "the next run");
        await Menu.HandleAsync(oldButton, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(page.Buttons[0].Text, Is.EqualTo("today 21:00 — Floor heating: on"));
            Assert.That(run.Paragraphs, Is.EqualTo(new[] {"today 21:00 — Floor heating: on", "Changes are for this run only."}));
            Assert.That(later.Paragraphs.Take(2), Is.EqualTo(new[] {"today 22:00 — Floor heating: on", "Moved from 21:00 of the schedule."}));
            Assert.That(moved.Paragraphs[0], Is.EqualTo("today 20:00 — Floor heating: on"));
            Assert.That(moved.Buttons.Last().Text, Is.EqualTo("↺ As scheduled"));
            Assert.That(before, Is.Empty);
            Assert.That(Engine.Pending.Single(x => x.Key == $"time:{Schedule}:0").Due, Is.EqualTo(Local(11, 21)), "the next day as scheduled, the old button changed nothing");
        });
    }

    [Test]
    public async Task RunSkippedOnce_NotDone_OwnerNotified_NextDayAsScheduled()
    {
        await ArmedAsync();
        await OutputAsync("2", 11, true);
        var run = await Menu.HandleAsync(new MenuAction.OpenRun(Schedule, 1), CancellationToken.None);
        var skipped = await Menu.HandleAsync(run.Buttons.Single(x => x.Text == "⏭ Skip once").Action, CancellationToken.None);
        var schedules = await Menu.HandleAsync(new MenuAction.Schedules(), CancellationToken.None);

        await RunUntilAsync(Local(11, 4));
        var notification = await NotificationAsync($"{Heating} off");

        Assert.Multiple(() =>
        {
            Assert.That(skipped.Paragraphs, Does.Contain("Skipped once."));
            Assert.That(skipped.Buttons.Select(x => x.Text), Does.Contain("▶️ Do not skip").And.Contain("↺ As scheduled"));
            Assert.That(schedules.Buttons.Select(x => x.Text), Does.Contain("tomorrow 03:00 — Floor heating: off (skipped)"));
            Assert.That(notification, Is.EqualTo($"{Heating} off — skipped once from the menu"));
            Assert.That(Commands, Is.Empty, "the heating is already on at 21:00; nothing at 03:00");
            Assert.That(Engine.Pending.Single(x => x.Key == $"time:{Schedule}:1").Due, Is.EqualTo(Local(12, 3)));
        });
    }

    [TestCase(21, 1, TestName = "Restart at 21:00 - on right after the start")]
    [TestCase(23, 0, TestName = "Down for the evening - on after the start")]
    public async Task Restart_MissedRunDoneAfterStart(int hour, int minute)
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        await StopAsync();
        Start(Local(10, hour, minute), data);

        await CommandAsync(heatingOn);
        Assert.That(await NotificationAsync(Heating), Is.EqualTo($"{Heating} on — OK"));
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Restart_WhileTheRunIsBeingDone_DoneAfterTheStart()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        // A deployment at 21:00: the server stops while the controller is answering.
        var held = new TaskCompletionSource();
        factory.Network.BeforeDeviceAnswers = async (device, cancellationToken) =>
        {
            if (device.DeviceId != "2")
                return;
            held.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };
        Advance(Local(10, 21) - time.GetUtcNow());
        await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopAsync();

        Start(Local(10, 21, 2), data);

        await CommandAsync(heatingOn);
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Restart_DownForTheNight_LastRunsDone_TooLateDropped()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        // Down from Saturday 13:00 to Sunday 10:00: 21:00 is 13 h late, 03:00 - 7 h, 09:00 - 1 h.
        await StopAsync();
        Start(Local(11, 10), data);

        await CommandAsync(heatingOn);
        var dropped = await NotificationAsync("⚠️");
        await QuietAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.EqualTo("⚠️ Floor heating: Floor heating: on not done, 13 h late (the server was down)"));
            Assert.That(Commands, Is.EqualTo(new[] {heatingOn}), "03:00 found it off, 09:00 turned it on");
            Assert.That(Engine.Pending.Single(x => x.Key == $"time:{Schedule}:0").Due, Is.EqualTo(Local(11, 21)));
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Restart_DownForMoreThanADay_TheSecondEveningDone()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        // Down from Saturday 13:00 to Sunday 23:00: Saturday 21:00 is dropped, Sunday 21:00 is 2 h late.
        await StopAsync();
        Start(Local(11, 23), data);

        await CommandAsync(heatingOn);
        await QuietAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Commands, Is.EqualTo(new[] {heatingOn}));
            Assert.That(Engine.Pending.Single(x => x.Key == $"time:{Schedule}:0").Due, Is.EqualTo(Local(12, 21)));
        });
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task Restart_TimeChangedInTheConfig_RunsAtTheNewTime()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        Start(start, data);
        await ArmedAsync();

        await StopAsync();
        Start(start, data, new Dictionary<string, string?> {["Home:Automations:1:Rules:0:Trigger:At"] = "22:00"});
        await ArmedAsync();

        Assert.That(Engine.Pending.Single(x => x.Key == $"time:{Schedule}:0").Due, Is.EqualTo(Local(10, 22)));
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task DatabaseUnavailable_SchedulesStillRun()
    {
        var data = TempDirectory.Create();
        await StopAsync();
        // The path of the database is a directory: it cannot be opened.
        Start(start, settings: new Dictionary<string, string?> {["Storage:DatabasePath"] = data});

        await ArmedAsync();
        await RunUntilAsync(Local(10, 21, 1));

        await CommandAsync(heatingOn);
        TempDirectory.Delete(data);
    }

    [Test]
    public async Task TurnedOffFromTheMenu_NothingRuns_TurnedOnAgain_FromNow()
    {
        await ArmedAsync();
        var off = await Menu.HandleAsync(new MenuAction.ToggleSchedule(Schedule), CancellationToken.None);
        Advance(Local(10, 21, 30) - time.GetUtcNow());
        await QuietAsync();
        var whileOff = Commands.ToList();

        var on = await Menu.HandleAsync(new MenuAction.ToggleSchedule(Schedule), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(off.Buttons.Select(x => x.Text), Does.Contain("🔘 Floor heating").And.Not.Contain("today 21:00 — Floor heating: on"));
            Assert.That(off.Paragraphs, Is.EqualTo(new[] {"Nothing is scheduled."}));
            Assert.That(whileOff, Is.Empty);
            Assert.That(on.Buttons[0].Text, Is.EqualTo("tomorrow 03:00 — Floor heating: off"), "21:00 was missed while off: not done");
            Assert.That(Commands, Is.Empty);
        });
    }

    [Test]
    public async Task PersonSwitchesTheHeating_ScheduleStays()
    {
        await ArmedAsync();

        await OutputAsync("2", 11, true);
        await OutputAsync("2", 11, false);
        await QuietAsync();
        await RunUntilAsync(Local(10, 21, 1));

        await CommandAsync(heatingOn);
    }

    [Test]
    public async Task Menu_SchedulesApart_FromAutomations()
    {
        await ArmedAsync();

        var dashboard = await Menu.HandleAsync(new MenuAction.Dashboard(), CancellationToken.None);
        var schedules = await Menu.HandleAsync(new MenuAction.Schedules(), CancellationToken.None);
        var automations = await Menu.HandleAsync(new MenuAction.Automations(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Buttons.Select(x => x.Text).TakeLast(2), Is.EqualTo(new[] {"⚙️ Automations", "🕒 Schedules"}));
            Assert.That(schedules.Title, Is.EqualTo("🕒 Schedules"));
            Assert.That(schedules.Buttons.Select(x => x.Text), Is.EqualTo(new[]
            {
                "today 21:00 — Floor heating: on",
                "tomorrow 03:00 — Floor heating: off",
                "tomorrow 09:00 — Floor heating: on",
                "tomorrow 11:00 — Floor heating: off",
                "Mon 12 Oct 07:00 — Floor heating: on",
                "🟢 Floor heating",
                "🏖 Vacation",
            }));
            Assert.That(automations.Buttons.Select(x => x.Text), Is.EqualTo(new[] {"🟢 Bathroom fan follows the light", "🟢 Auto-off"}));
            Assert.That(automations.Paragraphs.Single(), Is.EqualTo("Nothing is scheduled."));
        });
    }

    [Test]
    public async Task SchedulesOff_NothingRuns_NoMenu()
    {
        await StopAsync();
        Start(start, settings: new Dictionary<string, string?> {["Automation:SchedulesEnabled"] = "false"});

        Advance(TimeSpan.FromDays(2));
        await QuietAsync();
        var dashboard = await Menu.HandleAsync(new MenuAction.Schedules(), CancellationToken.None);
        var automations = await Menu.HandleAsync(new MenuAction.Automations(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(Engine.Pending, Is.Empty);
            Assert.That(Commands, Is.Empty);
            Assert.That(dashboard.Buttons.Select(x => x.Text), Does.Contain("⚙️ Automations").And.Not.Contain("🕒 Schedules"));
            Assert.That(automations.Buttons.Select(x => x.Text), Does.Not.Contain("🟢 Floor heating"));
        });
    }

    /// <summary> Local time of the house, October 2026 (BST, UTC+1). </summary>
    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(1));

    private void Start(DateTimeOffset now, string? dataDirectory = null, Dictionary<string, string?>? settings = null)
    {
        time = new FakeTimeProvider(now);
        var all = new Dictionary<string, string?> {["Automation:Enabled"] = "true", ["Automation:SchedulesEnabled"] = "true"};
        if (dataDirectory != null)
            all["Storage:DatabasePath"] = Path.Combine(dataDirectory, "home.db");
        foreach (var (key, value) in settings ?? [])
            all[key] = value;

        factory = new HomeServerFactory(House.Example, all, services => services.AddSingleton<TimeProvider>(time));
        // The snapshot of the controller has the heating on.
        factory.Network.Devices["2"].Set(11, false);
        client = factory.CreateClient();
    }

    private async Task StopAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    private void Advance(TimeSpan delta) => time.Advance(delta);

    private Task ArmedAsync() => WaitUntilAsync(() => Engine.Pending.Count(x => x.Work is DelayedWork.Run) == 5, "the next runs scheduled");

    /// <summary> Moves the clock from run to run, as the time passes, up to <paramref name="end"/>. </summary>
    private async Task RunUntilAsync(DateTimeOffset end)
    {
        while (Engine.Pending.FirstOrDefault(x => x.Work is DelayedWork.Run) is { } next && next.Due <= end)
        {
            Advance(next.Due - time.GetUtcNow());
            await WaitUntilAsync(() => Engine.Pending.Any(x => x.Key == next.Key && x.Due > next.Due), $"{next.Key} done");
        }

        if (end > time.GetUtcNow())
            Advance(end - time.GetUtcNow());
        await QuietAsync();
    }

    private async Task OutputAsync(string device, int port, bool on)
    {
        factory.Network.Devices[device].Set(port, on);
        await client.GetAsync($"/megad/{device}?pt={port}&mdid=1&v={(on ? 1 : 0)}");
    }

    private Task CommandAsync(MegaDCommand command) => WaitUntilAsync(() => Commands.Contains(command), $"{command}");

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
