using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Storage;
using System.Collections.Concurrent;
using System.Globalization;

namespace SmartHome.Core.Valves;

/// <summary>
/// Cleaning of a bathroom: for a while its leak sensor does not close the valves, so the floor can be washed.
/// <para>
/// The controller closes the valves by itself unless the server answers 200 to the sensor, so the server answers 200
/// only while the cleaning lasts: a server that is down, hung or unreachable leaves the protection on. The end is
/// checked against the clock on every event; the timer only does the check below. The sensor reports when it gets wet,
/// not while it is wet: at the end the server reads it (<see cref="EndReads"/> times: the contact flickers) and closes the
/// valves when it is still wet. The end is saved and
/// restored by <see cref="Storage.HomeStartup"/>; one that passed while the server was down is checked after the start.
/// </para>
/// </summary>
public sealed class CleaningService(
    HomeModel home,
    IDeviceGateway gateway,
    ValveService valves,
    IHomeStorage storage,
    HomeEventBus events,
    TimeProvider timeProvider,
    IOptions<CleaningSettings> settings,
    ILogger<CleaningService> logger) : IHostedService
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(2);

    /// <summary> The contact of a wet sensor flickers: the end reads it this many times, a second apart; one "on" is wet. </summary>
    public const int EndReads = 3;

    /// <summary> Long enough for a pulse of the other line (25 s) to end and for a short drop of the network. </summary>
    public const int CloseAttempts = 12;

    public static readonly TimeSpan CloseRetryDelay = TimeSpan.FromSeconds(10);

    private const string KeyPrefix = "cleaning_until:";

    private readonly Lock sync = new();
    private readonly Dictionary<string, Cleaning> cleanings = [];

    // Starts and ends of a valve one at a time, with their reads and writes: an end that is still checking the sensor does
    // not delete the saved end of a cleaning started after it. Per valve: closing one bathroom does not hold up the other.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> changes = new();
    private readonly CancellationTokenSource stopping = new();

    public bool IsAvailable => settings.Value.Enabled;

    /// <summary> The end of the cleaning of this valve; null when there is none. </summary>
    public DateTimeOffset? UntilOf(Entity valve)
    {
        lock (sync)
            return cleanings.GetValueOrDefault(valve.Id) is { } cleaning && cleaning.Until > timeProvider.GetUtcNow() ? cleaning.Until : null;
    }

    /// <summary> True - the leak sensor fired during the cleaning: the controller must not close the valve. </summary>
    public bool HoldsBack(Entity valve, Entity sensor)
    {
        if (!IsAvailable || UntilOf(valve) == null)
            return false;

        logger.LogWarning("Leak sensor {Sensor} held back: {Valve} is being cleaned", sensor.Id, valve.Id);
        events.Publish(new LeakHeldBack(valve, sensor));
        return true;
    }

    /// <summary> Starts the cleaning or moves its end: <paramref name="duration"/> from now. </summary>
    public async Task StartCleaningAsync(Entity valve, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!IsAvailable || gateway.LeakSensorOf(valve) == null)
            throw new InvalidOperationException($"{valve.Id} cannot be cleaned");

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duration, MaxDuration);

        var valveChanges = ChangesOf(valve);
        await valveChanges.WaitAsync(cancellationToken);
        try
        {
            var until = timeProvider.GetUtcNow() + duration;
            lock (sync)
                Arm(valve, until);

            logger.LogInformation("Cleaning of {Valve} until {Until:O}", valve.Id, until);
            events.Publish(new CleaningStarted(valve, until));
            await InvokeOperations.InvokeAsync(logger, $"Save the cleaning of {valve.Id}",
                                               () => storage.SaveSettingAsync(Key(valve), until.ToString("O", CultureInfo.InvariantCulture), CancellationToken.None));
        }
        finally
        {
            valveChanges.Release();
        }
    }

    /// <summary>
    /// When this returns, the leak sensor closes the valves again. The sensor is checked in the background: the bot is not held up
    /// while the valves are being closed.
    /// </summary>
    public async Task EndCleaningAsync(Entity valve, CancellationToken cancellationToken)
    {
        // Nothing to end, or the timer is ending it already.
        if (UntilOf(valve) == null)
            return;

        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => EndAsync(valve, expected: null, stopping.Token, removed), CancellationToken.None);
        await removed.Task.WaitAsync(cancellationToken);
    }

    /// <summary> The cleanings saved before the restart; also when <see cref="IsAvailable"/> is off, so that their ends are checked. </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        foreach (var valve in home.Entities.Where(x => x.Type == EntityType.Valve && gateway.LeakSensorOf(x) != null))
        {
            if (await storage.LoadSettingAsync(Key(valve), cancellationToken) is not { } text ||
                !DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
                continue;

            // A clock set back must not stretch the cleaning.
            var latest = timeProvider.GetUtcNow() + MaxDuration;
            lock (sync)
                Arm(valve, until < latest ? until : latest);

            logger.LogInformation("Cleaning of {Valve} restored, until {Until:O}", valve.Id, until);
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        stopping.Cancel();
        lock (sync)
        {
            foreach (var cleaning in cleanings.Values)
                cleaning.Timer.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary> Under <see cref="sync"/>. An end in the past is checked right away. </summary>
    private void Arm(Entity valve, DateTimeOffset until)
    {
        if (cleanings.Remove(valve.Id, out var previous))
            previous.Timer.Dispose();

        // Not on the thread of the timer: a timer due now may fire inside CreateTimer, before the cleaning is in the list.
        var dueIn = until - timeProvider.GetUtcNow();
        var timer = timeProvider.CreateTimer(_ => Task.Run(() => EndAsync(valve, until, stopping.Token)), null, dueIn > TimeSpan.Zero ? dueIn : TimeSpan.Zero,
                                             Timeout.InfiniteTimeSpan);
        cleanings[valve.Id] = new Cleaning(until, timer);
    }

    /// <param name="expected"> The end the timer was set for; null - ended from the menu. </param>
    /// <param name="removed"> Set once the cleaning no longer holds the sensor back, or there was nothing to end. </param>
    private async Task EndAsync(Entity valve, DateTimeOffset? expected, CancellationToken cancellationToken, TaskCompletionSource? removed = null)
    {
        try
        {
            var valveChanges = ChangesOf(valve);
            try
            {
                await valveChanges.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                lock (sync)
                {
                    // Ended from the menu already, or moved: the timer of the new end does it.
                    if (!cleanings.TryGetValue(valve.Id, out var cleaning) || expected != null && cleaning.Until != expected)
                        return;

                    cleanings.Remove(valve.Id);
                    cleaning.Timer.Dispose();
                }

                removed?.TrySetResult();

                // Stopped in the middle: the saved end stays and is checked again after the start.
                var result = await InvokeOperations.InvokeAsync(logger, $"End the cleaning of {valve.Id}", () => CheckSensorAsync(valve, cancellationToken));
                if (result.IsSuccess)
                {
                    await InvokeOperations.InvokeAsync(logger, $"Delete the cleaning of {valve.Id}",
                                                       () => storage.SaveSettingAsync(Key(valve), null, CancellationToken.None));
                }
            }
            finally
            {
                valveChanges.Release();
            }
        }
        finally
        {
            removed?.TrySetResult();
        }
    }

    private SemaphoreSlim ChangesOf(Entity valve) => changes.GetOrAdd(valve.Id, _ => new SemaphoreSlim(1, 1));

    private async Task CheckSensorAsync(Entity valve, CancellationToken cancellationToken)
    {
        var sensor = gateway.LeakSensorOf(valve)!;
        var end = CleaningEnd.SensorNotRead;
        for (var i = 0; i < EndReads && end != CleaningEnd.SensorWet; i++)
        {
            if (i > 0)
                await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, cancellationToken);

            var read = await gateway.ReadAsync([sensor], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            end = read.GetValueOrDefault(sensor.Id) switch
            {
                OnOffValue {State: OnOff.On} => CleaningEnd.SensorWet,
                OnOffValue => CleaningEnd.SensorDry,
                _ => end,
            };
        }

        logger.LogInformation("Cleaning of {Valve} is over: {End}", valve.Id, end);
        events.Publish(new CleaningEnded(valve, end));

        if (end == CleaningEnd.SensorWet)
            await CloseAsync(valve, cancellationToken);
    }

    /// <summary>
    /// A wet sensor does not fire again, so the valves must close now: a pulse still running (the water opened from the
    /// bot just before the end) or a command that did not get through is tried again. Closing valves that are closed already
    /// is harmless - the controller does it on every closure of the sensor. Not closed in the end - the saved end stays and
    /// is checked again after the start.
    /// </summary>
    private async Task CloseAsync(Entity valve, CancellationToken cancellationToken)
    {
        for (var attempt = 1;; attempt++)
        {
            var outcome = await valves.RunAsync(valve, ValveCommand.Close, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (outcome is CommandOutcome.Sent {Result.IsSuccess: true})
                return;

            if (attempt == CloseAttempts)
                throw new InvalidOperationException($"{valve.Id} is not closed after {CloseAttempts} attempts: {outcome}");

            await Task.Delay(CloseRetryDelay, timeProvider, cancellationToken);
        }
    }

    private static string Key(Entity valve) => KeyPrefix + valve.Id;

    private sealed record Cleaning(DateTimeOffset Until, ITimer Timer);
}
