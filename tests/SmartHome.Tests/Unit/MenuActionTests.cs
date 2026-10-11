using SmartHome.Core.Automations;
using SmartHome.Core.Entities;
using SmartHome.Core.Menu;
using SmartHome.Core.Notifications;

namespace SmartHome.Tests.Unit;

public class MenuActionTests
{
    private static readonly MenuAction[] actions =
    [
        new MenuAction.Dashboard(),
        new MenuAction.OpenPage("rooms"),
        new MenuAction.Toggle("hall", "light.hall"),
        new MenuAction.AskValve("water", "valve.bathroom", ValveCommand.Close),
        new MenuAction.RunValve("water", "valve.bathroom", ValveCommand.Open),
        new MenuAction.Automations(),
        new MenuAction.ToggleAutomation("bathroom_fan"),
        new MenuAction.Schedules(),
        new MenuAction.ToggleSchedule("floor_heating"),
        new MenuAction.OpenRun("floor_heating", 4),
        new MenuAction.ChangeRun("floor_heating", 1, new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero), RunChange.EarlierQuarter),
        new MenuAction.Vacation(),
        new MenuAction.ChangeVacation(VacationChange.MinusDay),
        new MenuAction.Cleaning("water", "valve.bathroom"),
        new MenuAction.ChangeCleaning("water", "valve.bathroom", CleaningChange.TwoHours),
        new MenuAction.Notifications(),
        new MenuAction.ChangeNotifications(NotificationCategory.Automation, NotificationMode.Silent),
    ];

    [TestCaseSource(nameof(actions))]
    public void SerializeAndParse_RoundTrip(MenuAction action)
    {
        Assert.That(MenuAction.Parse(action.Serialize()), Is.EqualTo(action));
    }

    // Buttons of messages sent by master carry GUIDs: after the release they open the dashboard.
    [TestCase("6f3a7c2e-2f4b-5d1e-9a3c-1b2c3d4e5f60")]
    [TestCase(null)]
    [TestCase("")]
    [TestCase("t:hall")]
    [TestCase("va:water:valve.bathroom:Explode")]
    [TestCase("a:bathroom_fan:extra")]
    [TestCase("sc:floor_heating:0:99999999999999:Skip", TestName = "Run time past the last date")]
    [TestCase("sc:floor_heating:0:-1:Skip", TestName = "Negative run time")]
    [TestCase("sc:floor_heating:0:1791676800:7", TestName = "Undefined change")]
    [TestCase("sr:floor_heating:first", TestName = "Rule index is not a number")]
    [TestCase("v:17", TestName = "Undefined vacation change")]
    [TestCase("cc:water:valve.bathroom:5", TestName = "Undefined cleaning change")]
    [TestCase("cc:water:valve.bathroom:FiveHours", TestName = "Unknown cleaning duration")]
    [TestCase("c:water", TestName = "Cleaning without a valve")]
    [TestCase("n:Events:Loud", TestName = "Unknown notification mode")]
    [TestCase("n:Events:5", TestName = "Undefined notification mode")]
    [TestCase("n:9:Off", TestName = "Undefined notification category")]
    [TestCase("n:Events", TestName = "Notification category without a mode")]
    public void Unknown_IsDashboard(string? data)
    {
        Assert.That(MenuAction.Parse(data), Is.EqualTo(new MenuAction.Dashboard()));
    }
}
