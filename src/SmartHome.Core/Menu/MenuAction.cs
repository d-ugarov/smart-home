using SmartHome.Core.Entities;
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

    public string Serialize() => this switch
    {
        Dashboard => "home",
        OpenPage x => Join("p", x.PageId),
        Toggle x => Join("t", x.PageId, x.EntityId),
        AskValve x => Join("va", x.PageId, x.EntityId, x.Command.ToString()),
        RunValve x => Join("vr", x.PageId, x.EntityId, x.Command.ToString()),
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
            _ => new Dashboard(),
        };
    }

    public static int ByteLength(MenuAction action) => Encoding.UTF8.GetByteCount(action.Serialize());

    private static string Join(params string[] parts) => string.Join(separator, parts);
}
