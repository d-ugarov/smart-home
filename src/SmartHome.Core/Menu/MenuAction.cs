using SmartHome.Core.Automations;
using SmartHome.Core.Entities;
using SmartHome.Core.Notifications;
using System.Globalization;
using System.Text;

namespace SmartHome.Core.Menu;

/// <summary>
/// What a menu button does. Serialized into a short string (Telegram callback data, 64 bytes at most) built from ids of
/// the config: buttons of old messages keep working while the config does not change; anything unknown opens the dashboard.
/// </summary>
public abstract record MenuAction
{
    public const int MaxLength = 64;

    private const char separator = ':';

    public sealed record Dashboard : MenuAction;

    public sealed record OpenPage(string PageId) : MenuAction;

    public sealed record Toggle(string PageId, string EntityId) : MenuAction;

    /// <summary> First press of a valve button: asks for a confirmation. </summary>
    public sealed record AskValve(string PageId, string EntityId, ValveCommand Command) : MenuAction;

    public sealed record RunValve(string PageId, string EntityId, ValveCommand Command) : MenuAction;

    /// <summary> The page of automations: on/off switches and what is scheduled. </summary>
    public sealed record Automations : MenuAction;

    public sealed record ToggleAutomation(string AutomationId) : MenuAction;

    /// <summary> The page of schedules: their next runs, on/off switches and the vacation. </summary>
    public sealed record Schedules : MenuAction;

    public sealed record ToggleSchedule(string AutomationId) : MenuAction;

    /// <summary> The page of the next run of a schedule rule. </summary>
    public sealed record OpenRun(string AutomationId, int RuleIndex) : MenuAction;

    /// <param name="RunTime"> The run the page showed: an old message does not change the run after it. </param>
    public sealed record ChangeRun(string AutomationId, int RuleIndex, DateTimeOffset RunTime, RunChange Change) : MenuAction;

    public sealed record Vacation : MenuAction;

    public sealed record ChangeVacation(VacationChange Change) : MenuAction;

    /// <summary> The page of the cleaning of a bathroom, opened from the valve item of the page. </summary>
    public sealed record Cleaning(string PageId, string EntityId) : MenuAction;

    public sealed record ChangeCleaning(string PageId, string EntityId, CleaningChange Change) : MenuAction;

    /// <summary> The page of notifications: how each category is sent. </summary>
    public sealed record Notifications : MenuAction;

    /// <param name="Mode"> The mode the button sets, not "the next one": a press on an old message sets what it showed. </param>
    public sealed record ChangeNotifications(NotificationCategory Category, NotificationMode Mode) : MenuAction;

    public string Serialize() => this switch
    {
        Dashboard => "home",
        OpenPage x => Join("p", x.PageId),
        Toggle x => Join("t", x.PageId, x.EntityId),
        AskValve x => Join("va", x.PageId, x.EntityId, x.Command.ToString()),
        RunValve x => Join("vr", x.PageId, x.EntityId, x.Command.ToString()),
        Automations => "a",
        ToggleAutomation x => Join("a", x.AutomationId),
        Schedules => "s",
        ToggleSchedule x => Join("s", x.AutomationId),
        OpenRun x => Join("sr", x.AutomationId, Number(x.RuleIndex)),
        ChangeRun x => Join("sc", x.AutomationId, Number(x.RuleIndex), Number(x.RunTime.ToUnixTimeSeconds()), x.Change.ToString()),
        Vacation => "v",
        ChangeVacation x => Join("v", x.Change.ToString()),
        Cleaning x => Join("c", x.PageId, x.EntityId),
        ChangeCleaning x => Join("cc", x.PageId, x.EntityId, x.Change.ToString()),
        Notifications => "n",
        ChangeNotifications x => Join("n", x.Category.ToString(), x.Mode.ToString()),
        _ => throw new NotSupportedException(GetType().Name),
    };

    /// <summary> Unknown or malformed data is the dashboard. </summary>
    public static MenuAction Parse(string? data)
    {
        var parts = (data ?? "").Split(separator);

        return parts switch
        {
            ["p", var page] => new OpenPage(page),
            ["t", var page, var entity] => new Toggle(page, entity),
            ["va", var page, var entity, var command] when Enum.TryParse<ValveCommand>(command, out var c) => new AskValve(page, entity, c),
            ["vr", var page, var entity, var command] when Enum.TryParse<ValveCommand>(command, out var c) => new RunValve(page, entity, c),
            ["a"] => new Automations(),
            ["a", var automation] => new ToggleAutomation(automation),
            ["s"] => new Schedules(),
            ["s", var automation] => new ToggleSchedule(automation),
            ["sr", var automation, var rule] when int.TryParse(rule, CultureInfo.InvariantCulture, out var index) => new OpenRun(automation, index),
            ["sc", var automation, var rule, var time, var change]
                when int.TryParse(rule, CultureInfo.InvariantCulture, out var index) && long.TryParse(time, CultureInfo.InvariantCulture, out var seconds) &&
                     seconds >= 0 && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds() && Enum.TryParse<RunChange>(change, out var c) && Enum.IsDefined(c)
                => new ChangeRun(automation, index, DateTimeOffset.FromUnixTimeSeconds(seconds), c),
            ["v"] => new Vacation(),
            ["v", var change] when Enum.TryParse<VacationChange>(change, out var c) && Enum.IsDefined(c) => new ChangeVacation(c),
            ["c", var page, var entity] => new Cleaning(page, entity),
            ["cc", var page, var entity, var change] when Enum.TryParse<CleaningChange>(change, out var c) && Enum.IsDefined(c) => new ChangeCleaning(page, entity, c),
            ["n"] => new Notifications(),
            ["n", var category, var mode]
                when Enum.TryParse<NotificationCategory>(category, out var c) && Enum.IsDefined(c) && Enum.TryParse<NotificationMode>(mode, out var m) && Enum.IsDefined(m)
                => new ChangeNotifications(c, m),
            _ => new Dashboard(),
        };
    }

    public static int ByteLength(MenuAction action) => Encoding.UTF8.GetByteCount(action.Serialize());

    private static string Join(params string[] parts) => string.Join(separator, parts);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

public enum VacationChange
{
    /// <summary> One more day; without a vacation - the rest of today. </summary>
    PlusDay,

    PlusWeek,

    /// <summary> One day less; the vacation ends when nothing is left. </summary>
    MinusDay,

    End,
}

/// <summary> The cleaning lasts this long from the press; a press during the cleaning moves its end. </summary>
public enum CleaningChange
{
    HalfHour,
    Hour,
    TwoHours,
    End,
}
