using SmartHome.Core.Devices;

namespace SmartHome.Core.Meters;

public enum CounterStep
{
    Forward,

    /// <summary> An older count arrived after a newer one: a read of the device and its own request crossed. </summary>
    Stale,

    /// <summary> The counter went round its modulus. </summary>
    Wrapped,

    /// <summary> The device restarted and its counter started from zero. </summary>
    Restarted,

    /// <summary> The counter went back without a known restart: counted as one. </summary>
    Reset,
}

/// <summary>
/// The water is counted by the increase of the counter of the input, not by the number of its requests: a lost request
/// does not lose a pulse, a repeated one does not add one.
/// </summary>
public static class PulseCounting
{
    /// <summary> Two reads of the device (the polling and a page) may come in either order: a count a little behind the last one is the older of the two. </summary>
    public const int StaleWindow = 16;

    /// <param name="restarted"> The device restarted since <paramref name="last"/> was seen. </param>
    /// <param name="maxWrapSteps"> The most a round may add (<see cref="MeterService.MaxWrap"/>); more is taken for a restart. </param>
    /// <returns> The steps the counter made since <paramref name="last"/>. </returns>
    public static (int Steps, CounterStep Step) Next(int last, int count, PulseCounter counter, bool restarted, int maxWrapSteps)
    {
        if (count >= last)
            return (count - last, CounterStep.Forward);

        if (last - count <= StaleWindow)
            return (0, CounterStep.Stale);

        if (restarted)
            return (count, CounterStep.Restarted);

        // A round seen by the server is a few steps. A restart taken for a round would add water that was never used; a round
        // taken for a restart loses at most the limit, and the owner is told to check the dial.
        var round = counter.Modulus - last + count;
        return round <= maxWrapSteps ? (round, CounterStep.Wrapped) : (count, CounterStep.Reset);
    }
}
