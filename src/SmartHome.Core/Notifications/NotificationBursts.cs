namespace SmartHome.Core.Notifications;

/// <summary>
/// A wet leak sensor closes its contact again and again - eight times in eight seconds on the shower sensor (checked
/// 2026-10-11) - and every closure is a request of the controller. The first notification of a sensor goes at once; the
/// next ones within <see cref="Window"/> are only counted, and the count goes as one line at its end. While the sensor
/// goes on, that is a line a minute; a window without repeats closes, and the next closure is sent at once again.
/// </summary>
internal sealed class NotificationBursts(NotificationQueue queue, TimeProvider timeProvider)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Lock sync = new();
    private readonly Dictionary<string, Burst> bursts = [];

    /// <param name="summary"> The line for the repeats of the window, by their count; null - not sent. </param>
    /// <returns> True - the first of a burst: send it now. </returns>
    public bool Start(string key, Func<int, Notification?> summary)
    {
        lock (sync)
        {
            if (bursts.TryGetValue(key, out var burst))
            {
                burst.Repeats++;
                return false;
            }

            Open(key, summary);
            return true;
        }
    }

    /// <summary> Under <see cref="sync"/>. </summary>
    private void Open(string key, Func<int, Notification?> summary)
    {
        var burst = new Burst();
        bursts[key] = burst;
        // Not on the thread of the timer: a timer may fire inside CreateTimer, before the burst is in the list.
        burst.Timer = timeProvider.CreateTimer(_ => Task.Run(() => Close(key, burst, summary)), null, Window, Timeout.InfiniteTimeSpan);
    }

    private void Close(string key, Burst burst, Func<int, Notification?> summary)
    {
        Notification? line = null;
        lock (sync)
        {
            if (!bursts.TryGetValue(key, out var current) || current != burst)
                return;

            bursts.Remove(key);
            burst.Timer?.Dispose();

            if (burst.Repeats > 0)
            {
                line = summary(burst.Repeats);
                Open(key, summary);
            }
        }

        if (line != null)
            queue.Enqueue(line);
    }

    private sealed class Burst
    {
        public int Repeats { get; set; }

        public ITimer? Timer { get; set; }
    }
}
