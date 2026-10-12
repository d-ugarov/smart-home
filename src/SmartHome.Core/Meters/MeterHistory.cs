using SmartHome.Core.Storage;

namespace SmartHome.Core.Meters;

/// <param name="Since"> The readings start within the period: the consumption since this day. </param>
public sealed record Consumption(decimal Amount, DateOnly? Since);

/// <summary>
/// Consumption over periods from the readings at the ends of the days. A day without a record kept the reading of the day
/// before; the first reading of a meter has a record on the day before it, so the consumption counts from it.
/// </summary>
public sealed class MeterHistory
{
    private readonly IReadOnlyList<StoredMeterDay> days;
    private readonly decimal? current;
    private readonly DateOnly today;

    /// <param name="days"> By date. </param>
    /// <param name="current"> The reading now: the end of today. </param>
    public MeterHistory(IReadOnlyList<StoredMeterDay> days, decimal? current, DateOnly today)
    {
        this.days = days;
        this.current = current;
        this.today = today;
    }

    /// <summary> The reading at the end of the day. </summary>
    public decimal? At(DateOnly day) => day >= today && current != null ? current : Saved(day);

    /// <summary> From the start of <paramref name="from"/> to the end of <paramref name="to"/>; null - no readings. </summary>
    public Consumption? Between(DateOnly from, DateOnly to)
    {
        if (At(to) is not { } end)
            return null;

        if (Saved(from.AddDays(-1)) is { } start)
            return new Consumption(end - start, null);

        return days.FirstOrDefault(x => x.Date >= from && x.Date <= to) is { } first ? new Consumption(end - first.Reading, first.Date.AddDays(1)) : null;
    }

    private decimal? Saved(DateOnly day) => days.LastOrDefault(x => x.Date <= day)?.Reading;
}
