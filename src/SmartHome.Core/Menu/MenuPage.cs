namespace SmartHome.Core.Menu;

/// <summary>
/// A page of the menu as data: information (title and text, read only) and actions (buttons, one per row, and the
/// footer row). Clients only draw it - the Telegram bot now, the REST API later.
/// </summary>
public sealed record MenuPage(string Title, IReadOnlyList<string> Paragraphs, IReadOnlyList<MenuButton> Buttons, IReadOnlyList<MenuButton> Footer);

public sealed record MenuButton(string Text, MenuAction Action);

public static class MenuTexts
{
    public const string Dashboard = "Dashboard";
    public const string DashboardButton = "🔼 Dashboard";
    public const string BackButton = "↩ Back";
    public const string SubPageMark = " ▶";
    public const string Yes = "✅ Yes";
    public const string No = "✖️ No";
    public const string PulseRunning = "⏳";
    public const string Automations = "⚙️ Automations";
    public const string AutomationsButton = "⚙️ Automations";
    public const string NothingScheduled = "Nothing is scheduled.";
    public const string Scheduled = "Scheduled:";
    public const string AutomationOn = "🟢";
    public const string AutomationOff = "🔘";
}
