using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Storage;
using System.Globalization;

namespace SmartHome.Core.Valves;

/// <summary>
/// Cleaning of a bathroom: for a while its leak sensor does not close the valves, so the floor can be washed.
/// <para>
/// The controller closes the valves by itself unless the server answers 200 to the sensor, so the server answers 200
/// only while the cleaning lasts: a server that is down, hung or unreachable leaves the protection on. The end is
/// checked against the clock on every event; the timer only does the check below. The sensor reports when it gets wet,
/// not while it is wet: at the end the server reads it and closes the valves when it is still wet. The end is saved and
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

    private const string KeyPrefix = "cleaning_until:";

    private readonly Lock sync = new();
    private readonly Dictionary<string, Cleaning> cleanings = [];

    // Starts and ends one at a time, with their reads and writes: an end that is still checking the sensor does not
    // delete the saved end of a cleaning started after it.
    private readonly SemaphoreSlim changes = new(1, 1);
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

        await changes.WaitAsync(cancellationToken);
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
            changes.Release();
        }
    }

    public Task EndCleaningAsync(Entity valve, CancellationToken cancellationToken) => EndAsync(valve, expected: null, cancellationToken);

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
    private async Task EndAsync(Entity valve, DateTimeOffset? expected, CancellationToken cancellationToken)
    {
        try
        {
            await changes.WaitAsync(cancellationToken);
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
            changes.Release();
        }
    }

    private async Task CheckSensorAsync(Entity valve, CancellationToken cancellationToken)
    {
        var sensor = gateway.LeakSensorOf(valve)!;
        var read = await gateway.ReadAsync([sensor], cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var end = read.GetValueOrDefault(sensor.Id) switch
        {
            OnOffValue {State: OnOff.On} => CleaningEnd.SensorWet,
            OnOffValue => CleaningEnd.SensorDry,
            _ => CleaningEnd.SensorNotRead,
        };

        logger.LogInformation("Cleaning of {Valve} is over: {End}", valve.Id, end);
        events.Publish(new CleaningEnded(valve, end));

        if (end == CleaningEnd.SensorWet)
        {
            await valves.RunAsync(valve, ValveCommand.Close, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static string Key(Entity valve) => KeyPrefix + valve.Id;

    private sealed record Cleaning(DateTimeOffset Until, ITimer Timer);
}
