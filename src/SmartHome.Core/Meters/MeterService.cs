using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.State;
using SmartHome.Core.Storage;

namespace SmartHome.Core.Meters;

/// <param name="Value"> Null - not entered yet. </param>
/// <param name="EnteredOn"> The local day of the last reading entered from the menu. </param>
public sealed record MeterReading(decimal? Value, DateTimeOffset? UpdatedAt, DateOnly? EnteredOn);

public enum EntryResult
{
    Saved,

    /// <summary> Kept in memory: the database is unavailable or the write failed. </summary>
    NotSaved,

    /// <summary> A pulse meter: its counter could not be read, the reading would not match the dial. Nothing changed. </summary>
    CounterNotRead,
}

/// <summary>
/// The readings of the meters. A pulse meter: the reading entered by the dial plus the pulses since, by the increase of the
/// counter of its input (<see cref="PulseCounting"/>) - from the reads of the device (the polling, the pages of the menu)
/// through <see cref="EntityStateChanged"/>, never from the count of a request of the device: those can be forged. A meter
/// without pulses: the readings entered from the menu.
/// <para>
/// The reading is saved with the count it includes, so the pulses made while the server was down are counted after the
/// start; and with the reading at the end of its local day, which the statistics are built from (<see cref="MeterHistory"/>).
/// When the storage could not be opened at the start, nothing is saved: a reading unknown in memory must not overwrite the
/// saved one.
/// </para>
/// </summary>
public sealed class MeterService(
    HomeModel home,
    IDeviceGateway gateway,
    HomeState state,
    IHomeStorage storage,
    HouseClock clock,
    HomeEventBus events,
    IOptions<MeterSettings> settings,
    MeterPersistence persistence,
    ILogger<MeterService> logger) : IHomeEventHandler
{
    /// <summary>
    /// The most water a round of the counter may add, in the unit of the meter: 50 L (the owner, 2026-10-12). A bigger "round"
    /// is taken for a restart of the device the server did not see, and the owner is told to check the dial.
    /// </summary>
    public const decimal MaxWrap = 0.05m;

    private readonly Lock sync = new();
    private readonly Dictionary<string, Current> meters = [];

    // Off until the saved readings are back.
    private bool saving;

    public bool IsAvailable => settings.Value.Enabled && home.Meters.Count > 0;

    public MeterReading ReadingOf(Entity meter)
    {
        lock (sync)
            return meters.TryGetValue(meter.Id, out var x) ? new MeterReading(x.Reading, x.UpdatedAt, x.EnteredOn) : new MeterReading(null, null, null);
    }

    /// <summary> By <see cref="HomeStartup"/>; from now on the changes are saved. </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
            return;

        var saved = await storage.LoadMetersAsync(cancellationToken);
        lock (sync)
        {
            foreach (var meter in saved.Where(x => home.FindEntity(x.EntityId)?.Type == EntityType.Meter))
                meters[meter.EntityId] = new Current {Reading = meter.Reading, Counter = meter.Counter, UpdatedAt = meter.UpdatedAt, EnteredOn = meter.EnteredOn};

            saving = true;
        }

        logger.LogInformation("Readings of {Count} meters restored", saved.Count);
    }

    /// <summary> The device of these meters restarted: their counters start from zero. </summary>
    public void CountersRestarted(IEnumerable<Entity> restarted)
    {
        lock (sync)
        {
            foreach (var meter in restarted.Where(x => x.Meter?.CountsPulses == true))
                Get(meter.Id).CounterRestarted = true;
        }
    }

    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (IsAvailable && homeEvent is EntityStateChanged {Current.Value: CounterValue counter} change && home.FindEntity(change.EntityId) is {Meter.CountsPulses: true} meter)
            Count(meter, counter.Count);

        return Task.CompletedTask;
    }

    /// <summary> Reads the counters of the pulse meters from the devices, so a page shows the water used a moment ago. </summary>
    /// <returns> The meters read. </returns>
    public async Task<IReadOnlySet<string>> RefreshAsync(IReadOnlyCollection<Entity> toRead, CancellationToken cancellationToken)
    {
        var pulseMeters = toRead.Where(x => x.Meter?.CountsPulses == true).ToList();
        if (pulseMeters.Count == 0)
            return new HashSet<string>();

        var values = await gateway.ReadAsync(pulseMeters, cancellationToken);
        state.SetAll(values, StateSource.Poll);

        // Counted here, before the page is drawn; the same counts coming through the events add nothing.
        var read = new HashSet<string>();
        foreach (var meter in pulseMeters)
        {
            if (values.GetValueOrDefault(meter.Id) is not CounterValue counter)
                continue;

            Count(meter, counter.Count);
            read.Add(meter.Id);
        }

        return read;
    }

    /// <summary> A reading entered from the menu: the dial of a pulse meter, or the reading of a meter without pulses. </summary>
    public async Task<EntryResult> EnterAsync(Entity meter, decimal reading, CancellationToken cancellationToken)
    {
        if (meter.Meter!.CountsPulses)
        {
            // The dial shows the water up to now: the reading must include the counter of now, not of the last poll.
            var values = await gateway.ReadAsync([meter], cancellationToken);
            if (values.GetValueOrDefault(meter.Id) is not CounterValue counter)
                return EntryResult.CounterNotRead;

            state.Set(meter.Id, counter, StateSource.Poll);
            Count(meter, counter.Count);
        }

        decimal? previous;
        Task<OperationResult> saved;
        lock (sync)
        {
            var current = Get(meter.Id);
            previous = current.Reading;
            var now = clock.Now;
            var today = clock.DateOf(now);

            current.Reading = reading;
            current.UpdatedAt = now;
            current.EnteredOn = today;

            // The first reading of a meter is where its consumption starts: it is also the end of the day before.
            List<StoredMeterDay> days = previous == null ? [new StoredMeterDay(meter.Id, today.AddDays(-1), reading, Entered: true)] : [];
            days.Add(new StoredMeterDay(meter.Id, today, reading, Entered: true));
            saved = Save(meter, current, days);
        }

        logger.LogInformation("Meter {Meter}: {Reading} entered, was {Previous}", meter.Id, reading, previous);
        events.Publish(new MeterReadingEntered(meter, reading, previous));

        return (await saved).IsSuccess ? EntryResult.Saved : EntryResult.NotSaved;
    }

    /// <summary> The readings at the ends of the days from <paramref name="from"/> on and the last one before it. </summary>
    public async Task<OperationResult<IReadOnlyList<StoredMeterDay>>> DaysAsync(Entity meter, DateOnly from, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (!saving)
                return OperationResult<IReadOnlyList<StoredMeterDay>>.Failed("the database is unavailable");
        }

        return await InvokeOperations.InvokeAsync(logger, $"Load the readings of {meter.Id}", () => storage.LoadMeterDaysAsync(meter.Id, from, cancellationToken));
    }

    private void Count(Entity meter, int count)
    {
        if (gateway.CounterOf(meter) is not { } counter)
            return;

        lock (sync)
        {
            var current = Get(meter.Id);
            if (current.Counter is not { } last)
            {
                // The first count seen: the pulses are counted from it.
                current.Counter = count;
                current.UpdatedAt = clock.Now;
                _ = Save(meter, current, []);
                return;
            }

            var stepWeight = meter.Meter!.PulseWeight!.Value / counter.CountsPerPulse;
            var (steps, step) = PulseCounting.Next(last, count, counter, current.CounterRestarted, (int)(MaxWrap / stepWeight));
            if (step == CounterStep.Stale || (step == CounterStep.Forward && steps == 0))
                return;

            current.CounterRestarted = false;
            current.Counter = count;
            current.UpdatedAt = clock.Now;
            if (current.Reading is { } reading)
                current.Reading = reading + steps * stepWeight;

            _ = Save(meter, current, current.Reading is { } value ? [new StoredMeterDay(meter.Id, clock.DateOf(current.UpdatedAt.Value), value, Entered: false)] : []);

            if (step != CounterStep.Forward)
                logger.LogWarning("Meter {Meter}: the counter went from {Last} to {Count}: {Step}, {Steps} steps counted", meter.Id, last, count, step, steps);
            if (step == CounterStep.Reset)
                events.Publish(new MeterCounterReset(meter, last, count));
        }
    }

    /// <summary> Under <see cref="sync"/>: the writes are queued in the order of the changes. </summary>
    private Task<OperationResult> Save(Entity meter, Current current, IReadOnlyList<StoredMeterDay> days)
    {
        if (!saving)
            return Task.FromResult(OperationResult.Failed("the database is unavailable"));

        return persistence.SaveAsync(new StoredMeter(meter.Id, current.Reading, current.Counter, current.UpdatedAt!.Value, current.EnteredOn), days);
    }

    private Current Get(string meterId)
    {
        if (!meters.TryGetValue(meterId, out var current))
            meters[meterId] = current = new Current();

        return current;
    }

    private sealed class Current
    {
        public decimal? Reading { get; set; }
        public int? Counter { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateOnly? EnteredOn { get; set; }

        /// <summary> Not saved: a restart seen before the server went down is guessed from the counts. </summary>
        public bool CounterRestarted { get; set; }
    }
}
