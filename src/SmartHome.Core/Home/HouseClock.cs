using SmartHome.Core.Automations;

namespace SmartHome.Core.Home;

/// <summary>
/// The local time of the house (<see cref="Configuration.HomeOptions.TimeZone"/>): schedules follow it, not UTC and not the
/// time zone of the machine the server runs on.
/// </summary>
public sealed class HouseClock(HomeModel home, TimeProvider timeProvider)
{
    public TimeZoneInfo Zone => home.TimeZone;

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public DateTimeOffset ToLocal(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone);

    public DateOnly DateOf(DateTimeOffset time) => DateOnly.FromDateTime(ToLocal(time).DateTime);

    public DateOnly Today => DateOf(Now);

    /// <summary> Midnight at the start of the local day. </summary>
    public DateTimeOffset StartOf(DateOnly day) => ToInstant(day.ToDateTime(TimeOnly.MinValue));

    /// <summary> The first run of the schedule strictly after <paramref name="after"/>. </summary>
    public DateTimeOffset Next(AutomationTrigger.TimeOfDay trigger, DateTimeOffset after)
    {
        // A trigger runs at least once a week; the day before covers a local date that a change of the offset moved.
        var day = DateOf(after).AddDays(-1);
        for (var i = 0; i < 9; i++, day = day.AddDays(1))
        {
            if (trigger.IsOn(day.DayOfWeek) && ToInstant(day.ToDateTime(trigger.At)) is var run && run > after)
                return run;
        }

        throw new InvalidOperationException($"The schedule {trigger} does not run within a week");
    }

    public bool IsRunOf(AutomationTrigger.TimeOfDay trigger, DateTimeOffset time) => Next(trigger, time - TimeSpan.FromTicks(1)) == time;

    /// <summary>
    /// A local time as an instant. A time that the clock skips when it springs forward runs at the end of the gap; a time
    /// that the clock passes twice when it falls back runs once, the first time.
    /// </summary>
    private DateTimeOffset ToInstant(DateTime local)
    {
        while (Zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        var offset = Zone.IsAmbiguousTime(local) ? Zone.GetAmbiguousTimeOffsets(local).Max() : Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
