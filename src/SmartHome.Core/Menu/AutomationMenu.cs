using SmartHome.Core.Automations;
using SmartHome.Core.Common;
using SmartHome.Core.Home;

namespace SmartHome.Core.Menu;

/// <summary>
/// The page of automations: what is scheduled (text) and a switch per automation and for the auto-off. Turning an
/// automation off cancels what it has scheduled. Schedules have their own pages (<see cref="ScheduleMenu"/>).
/// </summary>
public sealed class AutomationMenu(HomeModel home, AutomationEngine engine, TimeProvider timeProvider)
{
    /// <summary> Automations are on in the settings and the house has some. </summary>
    public bool IsAvailable => engine.IsEnabled && (Automations.Any() || home.AutoOffSwitches.Count > 0);

    private IEnumerable<Automation> Automations => home.Automations.Where(x => !x.IsSchedule);

    public MenuPage Render()
    {
        var now = timeProvider.GetUtcNow();
        var pending = engine.Pending.Where(x => x.Work is not DelayedWork.Run).ToList();

        var text = pending.Count == 0
            ? MenuTexts.NothingScheduled
            : string.Join("\n", pending.Select(x => $"• {Texts.Duration(x.Due - now)} — {AutomationEngine.Describe(x)} ({engine.OriginName(x.AutomationId)})")
                                       .Prepend(MenuTexts.Scheduled));

        var buttons = Automations.Select(x => Switch(x.Id, x.Name)).ToList();
        if (home.AutoOffSwitches.Count > 0)
            buttons.Add(Switch(Automation.AutoOffId, AutomationEngine.AutoOffName));

        return new MenuPage(MenuTexts.Automations, [text], buttons, [new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);
    }

    public async Task<MenuPage> ToggleAsync(string automationId, CancellationToken cancellationToken)
    {
        var known = automationId == Automation.AutoOffId ? home.AutoOffSwitches.Count > 0 : home.FindAutomation(automationId) is {IsSchedule: false};
        if (known)
            await engine.SwitchAsync(automationId, !engine.IsOn(automationId), cancellationToken);

        return Render();
    }

    private MenuButton Switch(string automationId, string name) =>
        new($"{(engine.IsOn(automationId) ? MenuTexts.AutomationOn : MenuTexts.AutomationOff)} {name}", new MenuAction.ToggleAutomation(automationId));
}
