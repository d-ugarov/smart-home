using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHome.Core.Common;
using SmartHome.Core.Storage;
using System.Threading.Channels;

namespace SmartHome.Core.Meters;

/// <summary>
/// The readings go to the storage in the order they changed, by one writer: an entered reading is never overwritten by an
/// older count waiting in the queue. What has piled up is written in one transaction: the last record of each meter and of
/// each of its days. On stop the queue is written out before the host goes down.
/// </summary>
public sealed class MeterPersistence(IHomeStorage storage, ILogger<MeterPersistence> logger) : BackgroundService
{
    private readonly Channel<Write> queue = Channel.CreateUnbounded<Write>(new UnboundedChannelOptions {SingleReader = true});

    /// <summary> Completes when written; never throws. </summary>
    public Task<OperationResult> SaveAsync(StoredMeter meter, IReadOnlyList<StoredMeterDay> days)
    {
        var write = new Write(meter, days, new TaskCompletionSource<OperationResult>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (!queue.Writer.TryWrite(write))
            write.Done.TrySetResult(OperationResult.Failed("the server is stopping"));

        return write.Done.Task;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not the stopping token: the queue is written out until StopAsync completes it.
        while (await queue.Reader.WaitToReadAsync(CancellationToken.None))
        {
            var writes = new List<Write>();
            while (queue.Reader.TryRead(out var write))
                writes.Add(write);

            var meters = writes.Select(x => x.Meter).GroupBy(x => x.EntityId).Select(x => x.Last()).ToList();
            var days = writes.SelectMany(x => x.Days)
                             .GroupBy(x => (x.EntityId, x.Date))
                             .Select(x => x.Last() with {Entered = x.Any(d => d.Entered)})
                             .ToList();

            var result = await InvokeOperations.InvokeAsync(logger, $"Save the readings of {meters.Count} meters",
                                                            () => storage.SaveMetersAsync(meters, days, CancellationToken.None));
            foreach (var write in writes)
                write.Done.TrySetResult(result);
        }
    }

    private sealed record Write(StoredMeter Meter, IReadOnlyList<StoredMeterDay> Days, TaskCompletionSource<OperationResult> Done);
}
