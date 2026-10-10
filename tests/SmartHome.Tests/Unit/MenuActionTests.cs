using SmartHome.Core.Entities;
using SmartHome.Core.Menu;

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
    public void Unknown_IsDashboard(string? data)
    {
        Assert.That(MenuAction.Parse(data), Is.EqualTo(new MenuAction.Dashboard()));
    }
}
