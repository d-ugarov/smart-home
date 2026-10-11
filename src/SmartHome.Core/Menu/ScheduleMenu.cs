using SmartHome.Core.Automations;
using SmartHome.Core.Entities;
using SmartHome.Core.Home;
using System.Globalization;

namespace SmartHome.Core.Menu;

/// <summary>
/// The pages of schedules: the next runs (a run can be moved or skipped once), a switch per schedule and the vacation,
/// when schedules turn nothing on. Times are the local time of the house.
/// </summary>
public sealed class ScheduleMenu(HomeModel home, AutomationEngine engine, HouseClock clock)
{
    /// <summary> Schedules are on in the settings and the house has some. </summary>
    public bool IsAvailable => engine.SchedulesEnabled && Schedules.Any();

    private IEnumerable<Automation> Schedules => home.Automations.Where(x => x.IsSchedule);

    public MenuPage Render()
    {
        var runs = Schedules.Where(x => engine.IsOn(x.Id))
                            .SelectMany(x => x.Rules)
                            .Select(x => (Rule: x, Run: engine.NextRun(x)))
                            .Where(x => x.Run != null)
                            .OrderBy(x => x.Run!.Due)
                            .ToList();

        var paragraphs = new List<string>();
        if (engine.VacationUntil is { } until)
            paragraphs.Add($"🏖 Vacation until {Day(until)}: schedules turn nothing on, turning off goes on.");
        paragraphs.Add(runs.Count == 0 ? MenuTexts.NothingScheduled : "Next runs:");

        var buttons = runs.Select(x => new MenuButton(RunText(x.Run!), new MenuAction.OpenRun(x.Rule.Automation.Id, x.Rule.Index))).ToList();
        buttons.AddRange(Schedules.Select(x => new MenuButton($"{(engine.IsOn(x.Id) ? MenuTexts.AutomationOn : MenuTexts.AutomationOff)} {x.Name}",
                                                              new MenuAction.ToggleSchedule(x.Id))));
        buttons.Add(new MenuButton(MenuTexts.VacationButton, new MenuAction.Vacation()));

        return new MenuPage(MenuTexts.Schedules, paragraphs, buttons, [new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);
    }

    public async Task<MenuPage> ToggleAsync(string automationId, CancellationToken cancellationToken)
    {
        if (home.FindAutomation(automationId) is {IsSchedule: true})
            await engine.SwitchAsync(automationId, !engine.IsOn(automationId), cancellationToken);

        return Render();
    }

    public MenuPage RenderRun(string automationId, int ruleIndex)
    {
        if (FindRule(automationId, ruleIndex) is not { } rule || engine.NextRun(rule) is not {Work: DelayedWork.Run run} action)
            return Render();

        var paragraphs = new List<string> {$"{When(action.Due)} — {AutomationEngine.Describe(rule.Commands)}"};
        if (action.Due != run.Time)
            paragraphs.Add($"Moved from {Time(run.Time)} of the schedule.");
        if (run.Skip)
            paragraphs.Add("Skipped once.");
        if (engine.VacationUntil is { } until && action.Due < clock.StartOf(until) && rule.Commands.Any(x => x.State == OnOff.On))
            paragraphs.Add("Vacation: turning on is skipped.");
        paragraphs.Add("Changes are for this run only.");

        MenuButton Change(string text, RunChange change) => new(text, new MenuAction.ChangeRun(automationId, ruleIndex, run.Time, change));

        var buttons = new List<MenuButton>
                      {
                          Change("−1 h", RunChange.EarlierHour),
                          Change("−15 min", RunChange.EarlierQuarter),
                          Change("+15 min", RunChange.LaterQuarter),
                          Change("+1 h", RunChange.LaterHour),
                          run.Skip ? Change("▶️ Do not skip", RunChange.Unskip) : Change("⏭ Skip once", RunChange.Skip),
                      };
        if (action.Due != run.Time || run.Skip)
            buttons.Add(Change("↺ As scheduled", RunChange.Reset));

        return new MenuPage(rule.Automation.Name, paragraphs, buttons,
                            [new MenuButton(MenuTexts.BackButton, new MenuAction.Schedules()), new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);
    }

    public async Task<MenuPage> ChangeRunAsync(MenuAction.ChangeRun change, CancellationToken cancellationToken)
    {
        if (FindRule(change.AutomationId, change.RuleIndex) is { } rule)
            await engine.ChangeRunAsync(rule, change.RunTime, change.Change, cancellationToken);

        return RenderRun(change.AutomationId, change.RuleIndex);
    }

    public MenuPage RenderVacation()
    {
        var text = engine.VacationUntil is { } until
            ? $"Until {Day(until)}: schedules turn nothing on, turning off goes on. From that day they work as usual."
            : "No vacation. During a vacation schedules turn nothing on, turning off goes on.";

        var buttons = new List<MenuButton>
                      {
                          new("+1 day", new MenuAction.ChangeVacation(VacationChange.PlusDay)),
                          new("+1 week", new MenuAction.ChangeVacation(VacationChange.PlusWeek)),
                      };
        if (engine.VacationUntil != null)
        {
            buttons.Add(new MenuButton("−1 day", new MenuAction.ChangeVacation(VacationChange.MinusDay)));
            buttons.Add(new MenuButton("✖️ End the vacation", new MenuAction.ChangeVacation(VacationChange.End)));
        }

        return new MenuPage(MenuTexts.Vacation, [text], buttons,
                            [new MenuButton(MenuTexts.BackButton, new MenuAction.Schedules()), new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);
    }

    public async Task<MenuPage> ChangeVacationAsync(VacationChange change, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var current = engine.VacationUntil ?? today;

        DateOnly? until = change switch
        {
            VacationChange.PlusDay => current.AddDays(1),
            VacationChange.PlusWeek => current.AddDays(7),
            VacationChange.MinusDay when current.AddDays(-1) > today => current.AddDays(-1),
            _ => null,
        };

        await engine.SetVacationAsync(until, cancellationToken);
        return RenderVacation();
    }

    private AutomationRule? FindRule(string automationId, int ruleIndex) =>
        home.FindAutomation(automationId) is {IsSchedule: true} automation ? automation.Rules.ElementAtOrDefault(ruleIndex) : null;

    private string RunText(DelayedAction action)
    {
        var run = (DelayedWork.Run)action.Work;
        var marks = run.Skip ? " (skipped)" : action.Due != run.Time ? " (moved)" : "";
        return $"{When(action.Due)} — {AutomationEngine.Describe(run.Rule.Commands)}{marks}";
    }

    /// <summary> "today 21:00", "tomorrow 03:00", "Mon 12 Oct 07:00". </summary>
    private string When(DateTimeOffset time)
    {
        var day = clock.DateOf(time);
        var today = clock.Today;
        var name = day == today ? "today" : day == today.AddDays(1) ? "tomorrow" : Day(day);
        return $"{name} {Time(time)}";
    }

    private string Time(DateTimeOffset time) => clock.ToLocal(time).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
}
