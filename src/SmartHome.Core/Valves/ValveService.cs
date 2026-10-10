using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Configuration;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using System.Collections.Concurrent;

namespace SmartHome.Core.Valves;

/// <summary>
/// Water valves are driven by a scenario of the controller (one 25 s pulse on one line). The controller runs one
/// scenario with pauses at a time, so a valve gets no new command while a pulse is running: neither while its relays
/// are on, nor between the command and the start of the pulse.
/// </summary>
public sealed class ValveService(
    IDeviceGateway gateway,
    HomeEventBus events,
    TimeProvider timeProvider,
    IOptions<HomeOptions> options,
    ILogger<ValveService> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    private readonly ConcurrentDictionary<string, Pending> pending = new();

    /// <summary> The pulse as seen on the relays, or the command just sent if the pulse has not shown up yet. </summary>
    public ValveActivity ActivityOf(Entity valve, EntityValue? read)
    {
        if (read is ValveValue {Activity: not ValveActivity.Idle} value)
            return value.Activity;

        return pending.TryGetValue(valve.Id, out var command) && command.Until > timeProvider.GetUtcNow()
            ? ToActivity(command.Command)
            : ValveActivity.Idle;
    }

    public async Task<CommandOutcome> RunAsync(Entity valve, ValveCommand command, CancellationToken cancellationToken)
    {
        var sync = locks.GetOrAdd(valve.Id, _ => new SemaphoreSlim(1, 1));
        await sync.WaitAsync(cancellationToken);

        try
        {
            var outcome = await RunLockedAsync(valve, command, cancellationToken);
            logger.LogInformation("Valve {Valve} {Command}: {Outcome}", valve.Id, command, outcome);
            events.Publish(new ValveCommandExecuted(valve, command, outcome));
            return outcome;
        }
        finally
        {
            sync.Release();
        }
    }

    /// <summary> The relay of the command went off: the pulse is over. </summary>
    public void OnPulseFinished(Entity valve, ValveCommand command)
    {
        if (pending.TryGetValue(valve.Id, out var current) && current.Command == command)
            pending.TryRemove(valve.Id, out _);
    }

    private async Task<CommandOutcome> RunLockedAsync(Entity valve, ValveCommand command, CancellationToken cancellationToken)
    {
        var read = await gateway.ReadAsync([valve], cancellationToken);
        if (!read.TryGetValue(valve.Id, out var value))
            return new CommandOutcome.Sent(OperationResult.Failed("the state of the valve relays could not be read"));

        if (ActivityOf(valve, value) != ValveActivity.Idle)
            return new CommandOutcome.Busy();

        var result = await gateway.RunValveAsync(valve, command, cancellationToken);
        if (result.IsSuccess)
            pending[valve.Id] = new Pending(command, timeProvider.GetUtcNow() + options.Value.ValvePulseTimeout);

        return new CommandOutcome.Sent(result);
    }

    private static ValveActivity ToActivity(ValveCommand command) => command == ValveCommand.Open ? ValveActivity.Opening : ValveActivity.Closing;

    private sealed record Pending(ValveCommand Command, DateTimeOffset Until);
}
