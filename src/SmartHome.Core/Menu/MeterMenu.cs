using SmartHome.Core.Entities;
using SmartHome.Core.Home;
using SmartHome.Core.Meters;
using SmartHome.Core.Storage;
using System.Globalization;
using System.Text;

namespace SmartHome.Core.Menu;

/// <summary>
/// The meters in the menu: all of them by kind (water, electricity) with the reading and the consumption for a period, a
/// page per meter, and the keypad of a reading. Months are calendar months of the local time of the house. The pulse
/// meters of a page are read from the devices first, so the page shows the water used a moment ago.
/// </summary>
public sealed class MeterMenu(HomeModel home, MeterService meters, HouseClock clock)
{
    /// <summary> A keypad older than this does not save: a press in an old message must not put an old reading back. </summary>
    public static readonly TimeSpan InputTimeout = TimeSpan.FromMinutes(10);

    private static readonly CultureInfo culture = CultureInfo.InvariantCulture;

    public bool IsAvailable => meters.IsAvailable;

    public Entity? FindMeter(string entityId) => home.FindEntity(entityId) is {Type: EntityType.Meter} meter ? meter : null;

    public async Task<MenuPage> RenderAllAsync(MeterPeriod period, CancellationToken cancellationToken)
    {
        await meters.RefreshAsync(home.Meters, cancellationToken);

        var today = clock.Today;
        var month = FirstOfMonth(today);
        var from = period == MeterPeriod.Year ? month.AddMonths(-11) : month.AddMonths(-1);
        var (histories, problem) = await HistoriesAsync(home.Meters, from, cancellationToken);

        var paragraphs = new List<string>
        {
            period switch
            {
                MeterPeriod.ThisMonth => $"The reading now and the consumption in {MonthName(month)}.",
                MeterPeriod.LastMonth => $"The reading at the end of {MonthName(month.AddMonths(-1))} and the consumption in it.",
                _ => "The consumption by months, all the meters of a kind together.",
            },
        };
        if (problem != null)
            paragraphs.Add(problem);

        var tables = period == MeterPeriod.Year
            ? [YearTotals(histories, month)]
            : home.Meters.GroupBy(x => x.Meter!.IsWater).Select(x => PeriodTable(x.ToList(), histories, period, month)).ToList();

        var buttons = home.Meters.Select(x => new MenuButton($"{Icon(x)} {x.Name}", new MenuAction.Meter(x.Id, Year: false))).ToList();
        IReadOnlyList<MenuButton> periods =
        [
            PeriodButton("This month", MeterPeriod.ThisMonth),
            PeriodButton("Last month", MeterPeriod.LastMonth),
            PeriodButton("12 months", MeterPeriod.Year),
        ];

        return new MenuPage(MenuTexts.Meters, paragraphs, buttons, [new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())])
        {
            Tables = tables,
            Grid = [periods],
        };

        MenuButton PeriodButton(string text, MeterPeriod value) => new(value == period ? $"✓ {text}" : $"📅 {text}", new MenuAction.Meters(value));
    }

    public async Task<MenuPage> RenderMeterAsync(Entity meter, bool year, CancellationToken cancellationToken, string? result = null)
    {
        var read = await meters.RefreshAsync([meter], cancellationToken);

        var today = clock.Today;
        var month = FirstOfMonth(today);
        var (histories, problem) = await HistoriesAsync([meter], year ? month.AddMonths(-11) : month.AddMonths(-1), cancellationToken);
        var history = histories[meter.Id];
        var spec = meter.Meter!;

        var paragraphs = new List<string>();
        if (result != null)
            paragraphs.Add(result);
        paragraphs.Add(ReadingText(meter, read.Contains(meter.Id)));
        if (problem != null)
            paragraphs.Add(problem);

        var notes = new Notes();
        var rows = new List<string[]>();
        if (year)
        {
            rows.Add(["", "used", "reading"]);
            for (var m = month; m >= month.AddMonths(-11); m = m.AddMonths(-1))
            {
                var end = m == month ? today : m.AddMonths(1).AddDays(-1);
                if (history.At(end) is not { } reading)
                    break;
                rows.Add([ShortMonth(m), notes.Mark(spec, history.Between(m, end)), Format(spec, reading)]);
            }
        }
        else
        {
            if (spec.CountsPulses)
            {
                rows.Add(["Today", notes.Mark(spec, history.Between(today, today))]);
                rows.Add(["Yesterday", notes.Mark(spec, history.Between(today.AddDays(-1), today.AddDays(-1)))]);
            }

            rows.Add([MonthName(month), notes.Mark(spec, history.Between(month, today))]);
            rows.Add([MonthName(month.AddMonths(-1)), notes.Mark(spec, history.Between(month.AddMonths(-1), month.AddDays(-1)))]);
        }

        var buttons = new List<MenuButton>();
        if (result != null && NextToEnter(meter) is { } next)
            buttons.Add(new MenuButton($"✏️ Next: {next.Name}", new MenuAction.MeterInput(next.Id, clock.Now, "")));
        buttons.Add(year
                        ? new MenuButton("📅 This month", new MenuAction.Meter(meter.Id, Year: false))
                        : new MenuButton("📅 12 months", new MenuAction.Meter(meter.Id, Year: true)));
        buttons.Add(new MenuButton(spec.CountsPulses ? "✏️ By the dial" : "✏️ Enter the reading", new MenuAction.MeterInput(meter.Id, clock.Now, "")));

        return new MenuPage($"{Icon(meter)} {meter.Name}", paragraphs, buttons, Footer())
        {
            Tables = [Table(rows) + notes],
        };
    }

    /// <param name="message"> Why the keypad is shown again, e.g. the save failed. </param>
    public MenuPage RenderInput(Entity meter, DateTimeOffset opened, string digits, string? message = null)
    {
        if (IsOutOfDate(opened))
            return OutOfDate(meter);

        var spec = meter.Meter!;
        var current = meters.ReadingOf(meter).Value;
        var value = MeterReadingInput.Value(digits);

        var paragraphs = new List<string>();
        if (message != null)
            paragraphs.Add(message);
        paragraphs.Add(spec.CountsPulses ? $"The reading of the dial, {spec.Unit}:" : $"The reading, {spec.Unit}:");
        paragraphs.Add($"➡️ {(digits.Length > 0 ? digits : "…")}");
        if (current != null)
            paragraphs.Add($"Now: {Format(spec, current.Value)} {spec.Unit}{(value < current ? " — ⚠️ the typed one is less" : "")}");

        IReadOnlyList<IReadOnlyList<MenuButton>> keypad =
        [
            [Key('1'), Key('2'), Key('3')],
            [Key('4'), Key('5'), Key('6')],
            [Key('7'), Key('8'), Key('9')],
            [Key('.'), Key('0'), new MenuButton("⌫", new MenuAction.MeterInput(meter.Id, opened, MeterReadingInput.Erase(digits)))],
        ];
        var save = value is { } typed
            ? new List<MenuButton> {new($"✅ Save {Format(spec, typed)} {spec.Unit}", new MenuAction.SaveMeter(meter.Id, opened, digits))}
            : [];

        return new MenuPage($"✏️ {meter.Name}", paragraphs, [], [new MenuButton("✖️ Cancel", new MenuAction.Meter(meter.Id, Year: false))])
        {
            Grid = [..keypad, ..save.Select(x => (IReadOnlyList<MenuButton>)[x])],
        };

        MenuButton Key(char key) => new(key.ToString(), new MenuAction.MeterInput(meter.Id, opened, MeterReadingInput.Add(digits, key)));
    }

    public async Task<MenuPage> SaveAsync(Entity meter, DateTimeOffset opened, string digits, CancellationToken cancellationToken)
    {
        if (IsOutOfDate(opened))
            return OutOfDate(meter);

        if (MeterReadingInput.Value(digits) is not { } value)
            return RenderInput(meter, opened, digits);

        var spec = meter.Meter!;
        return await meters.EnterAsync(meter, value, cancellationToken) switch
        {
            EntryResult.Saved => await RenderMeterAsync(meter, year: false, cancellationToken, $"✅ Saved: {Format(spec, value)} {spec.Unit}."),
            EntryResult.NotSaved => await RenderMeterAsync(meter, year: false, cancellationToken,
                                                           $"⚠️ {Format(spec, value)} {spec.Unit} is not saved in the database: it is kept until a restart of the server."),
            _ => RenderInput(meter, opened, digits, "⚠️ Not saved: the controller does not answer, the pulses of now are unknown. Try again."),
        };
    }

    private bool IsOutOfDate(DateTimeOffset opened) => clock.Now - opened > InputTimeout || opened > clock.Now + TimeSpan.FromMinutes(1);

    private MenuPage OutOfDate(Entity meter) =>
        new($"✏️ {meter.Name}",
            ["This keypad is out of date. Open it again."],
            [new MenuButton(meter.Meter!.CountsPulses ? "✏️ By the dial" : "✏️ Enter the reading", new MenuAction.MeterInput(meter.Id, clock.Now, ""))],
            Footer());

    /// <summary> The next meter of the same resource without pulses: the day and the night of the electricity are entered one after another. </summary>
    private Entity? NextToEnter(Entity meter)
    {
        if (meter.Meter!.CountsPulses)
            return null;

        return home.Meters.SkipWhile(x => x != meter).Skip(1).FirstOrDefault(x => x.Meter!.Resource == meter.Meter.Resource && !x.Meter.CountsPulses);
    }

    private string ReadingText(Entity meter, bool read)
    {
        var spec = meter.Meter!;
        var reading = meters.ReadingOf(meter);

        if (reading.Value is not { } value)
            return spec.CountsPulses
                ? "The reading is not set: enter the reading of the dial, the pulses are counted from it."
                : "No reading entered yet.";

        if (!spec.CountsPulses)
            return $"Entered {Day(reading.EnteredOn)}: {Format(spec, value)} {spec.Unit}";

        return read
            ? $"Now: {Format(spec, value)} {spec.Unit}"
            : $"Now: {Format(spec, value)} {spec.Unit} — the controller does not answer, as of {Time(reading.UpdatedAt)}";
    }

    private string PeriodTable(IReadOnlyList<Entity> group, IReadOnlyDictionary<string, MeterHistory> histories, MeterPeriod period, DateOnly month)
    {
        var today = clock.Today;
        var (from, to) = period == MeterPeriod.ThisMonth ? (month, today) : (month.AddMonths(-1), month.AddDays(-1));
        var spec = group[0].Meter!;
        var notes = new Notes();

        List<string[]> rows =
        [
            [$"{Icon(group[0])} {(spec.IsWater ? "Water" : "Electricity")}, {spec.Unit}", period == MeterPeriod.ThisMonth ? "now" : to.ToString("dd.MM", culture),
             from.ToString("MMMM", culture)],
        ];
        foreach (var meter in group)
        {
            var history = histories[meter.Id];
            rows.Add([meter.Name, history.At(to) is { } reading ? Format(meter.Meter!, reading) : "—", notes.Mark(meter.Meter!, history.Between(from, to))]);
        }

        foreach (var meter in group.Where(x => !x.Meter!.CountsPulses))
            notes.Add($"{meter.Name}: {(meters.ReadingOf(meter).EnteredOn is { } day ? $"entered {Day(day)}" : "not entered yet")}");

        return Table(rows) + notes;
    }

    /// <summary> A column per resource: the meters of a resource summed up. </summary>
    private string YearTotals(IReadOnlyDictionary<string, MeterHistory> histories, DateOnly month)
    {
        var today = clock.Today;
        var resources = home.Meters.Select(x => x.Meter!.Resource).Distinct().Order().ToList();
        var notes = new Notes();

        List<string[]> rows = [["", ..resources.Select(Header)]];
        for (var m = month; m >= month.AddMonths(-11); m = m.AddMonths(-1))
        {
            var end = m == month ? today : m.AddMonths(1).AddDays(-1);
            var cells = resources.Select(r =>
            {
                var used = home.Meters.Where(x => x.Meter!.Resource == r).Select(x => histories[x.Id].Between(m, end)).OfType<Consumption>().ToList();
                if (used.Count == 0)
                    return null;

                var spec = home.Meters.First(x => x.Meter!.Resource == r).Meter!;
                return notes.Mark(spec, new Consumption(used.Sum(x => x.Amount), used.Max(x => x.Since)));
            }).ToList();

            if (cells.All(x => x == null))
                break;
            rows.Add([ShortMonth(m), ..cells.Select(x => x ?? "—")]);
        }

        return Table(rows) + notes;

        static string Header(MeterResource resource) => resource switch
        {
            MeterResource.ColdWater => "cold, m³",
            MeterResource.HotWater => "hot, m³",
            _ => "kWh",
        };
    }

    private async Task<(IReadOnlyDictionary<string, MeterHistory> Histories, string? Problem)> HistoriesAsync(IReadOnlyList<Entity> list, DateOnly from,
                                                                                                             CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var result = new Dictionary<string, MeterHistory>();
        string? problem = null;

        foreach (var meter in list)
        {
            var days = await meters.DaysAsync(meter, from, cancellationToken);
            if (!days.IsSuccess)
                problem = $"⚠️ The history is unavailable: {days.Error}";

            result[meter.Id] = new MeterHistory(days.Data ?? [], meters.ReadingOf(meter).Value, today);
        }

        return (result, problem);
    }

    private static IReadOnlyList<MenuButton> Footer() =>
        [new MenuButton(MenuTexts.BackButton, new MenuAction.Meters(MeterPeriod.ThisMonth)), new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())];

    public static string Icon(Entity meter) => meter.Meter!.IsWater ? MenuTexts.Water : MenuTexts.Electricity;

    public static string Format(MeterSpec spec, decimal value) => value.ToString(spec.IsWater ? "0.000" : "0.##", culture);

    /// <summary> The first column to the left, the others to the right. </summary>
    private static string Table(IReadOnlyList<string[]> rows)
    {
        var widths = Enumerable.Range(0, rows.Max(x => x.Length)).Select(i => rows.Max(r => i < r.Length ? r[i].Length : 0)).ToList();
        return string.Join("\n", rows.Select(r => string.Join("  ", r.Select((cell, i) => i == 0 ? cell.PadRight(widths[i]) : cell.PadLeft(widths[i]))).TrimEnd()));
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

    private static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", culture);

    private static string ShortMonth(DateOnly month) => month.ToString("MMM yyyy", culture);

    private static string Day(DateOnly? day) => day?.ToString("dd.MM.yyyy", culture) ?? "?";

    private string Time(DateTimeOffset? time) => time is { } t ? clock.ToLocal(t).ToString("dd.MM HH:mm", culture) : "?";

    /// <summary> Consumption with a mark when it is only since a day within the period; the marks are explained under the table. </summary>
    private sealed class Notes
    {
        private readonly List<string> lines = [];

        public string Mark(MeterSpec spec, Consumption? consumption)
        {
            if (consumption is null)
                return "—";

            var text = Format(spec, consumption.Amount);
            if (consumption.Since is not { } since)
                return text;

            var note = $"* since {since.ToString("dd.MM.yyyy", culture)}";
            if (!lines.Contains(note))
                lines.Add(note);
            return text + "*";
        }

        public void Add(string line) => lines.Add(line);

        public override string ToString() => lines.Count == 0 ? "" : "\n\n" + string.Join("\n", lines);
    }
}
