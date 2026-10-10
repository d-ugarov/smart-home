using SmartHome.Core.Entities;

namespace SmartHome.Core.Common;

/// <summary> Small texts for people, shared by the menu and the notifications. </summary>
public static class Texts
{
    public static string Of(OnOff state) => state == OnOff.On ? "on" : "off";

    /// <summary> Rounded up to a minute: "25 min", "1 h 5 min", "8 h". </summary>
    public static string Duration(TimeSpan value)
    {
        var minutes = (long)Math.Ceiling(value.TotalMinutes);

        return (minutes / 60, minutes % 60) switch
        {
            (0, <= 0) => "<1 min",
            (0, var m) => $"{m} min",
            (var h, 0) => $"{h} h",
            (var h, var m) => $"{h} h {m} min",
        };
    }
}
