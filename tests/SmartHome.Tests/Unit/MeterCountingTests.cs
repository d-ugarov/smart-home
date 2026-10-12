using SmartHome.Core.Devices;
using SmartHome.Core.Menu;
using SmartHome.Core.Meters;
using SmartHome.Core.Storage;

namespace SmartHome.Tests.Unit;

public class MeterCountingTests
{
    private static readonly PulseCounter megaD = new(65536, 2);

    [TestCase(43563, 43564, false, 1, CounterStep.Forward, TestName = "Next edge")]
    [TestCase(43563, 43563, false, 0, CounterStep.Forward, TestName = "Same count (the hold of a pulse)")]
    [TestCase(43563, 43600, false, 37, CounterStep.Forward, TestName = "Requests lost: the increase counts")]
    [TestCase(43564, 43563, false, 0, CounterStep.Stale, TestName = "A read older than the last one")]
    [TestCase(43564, 43548, false, 0, CounterStep.Stale, TestName = "At the edge of the stale window")]
    [TestCase(43564, 43547, false, 43547, CounterStep.Reset, TestName = "Back past the window without a restart")]
    [TestCase(65534, 1, false, 3, CounterStep.Wrapped, TestName = "Round from the top")]
    [TestCase(65530, 3, false, 9, CounterStep.Wrapped, TestName = "Round within 50 L")]
    [TestCase(65530, 4, false, 10, CounterStep.Wrapped, TestName = "Round of exactly 50 L")]
    [TestCase(65530, 5, false, 5, CounterStep.Reset, TestName = "Round of more than 50 L: a restart nobody saw")]
    [TestCase(61440, 4095, false, 4095, CounterStep.Reset, TestName = "A restart near the top is not a round of 40 m³")]
    [TestCase(65534, 1, true, 1, CounterStep.Restarted, TestName = "A known restart is not a round")]
    [TestCase(27377, 2, true, 2, CounterStep.Restarted, TestName = "Restart: counted from zero")]
    [TestCase(27377, 27370, true, 0, CounterStep.Stale, TestName = "An old read after a restart is still old")]
    public void Next(int last, int count, bool restarted, int steps, CounterStep step)
    {
        // 10 L a pulse, 2 steps a pulse: 50 L is 10 steps.
        Assert.That(PulseCounting.Next(last, count, megaD, restarted, maxWrapSteps: 10), Is.EqualTo((steps, step)));
    }

    private static readonly DateOnly today = new(2026, 11, 2);

    private static StoredMeterDay Day(int month, int day, decimal reading, bool entered = false) => new("meter.x", new DateOnly(2026, month, day), reading, entered);

    [Test]
    public void History_DaysWithoutRecords_KeepTheReadingOfTheDayBefore()
    {
        var history = new MeterHistory([Day(10, 9, 100m, true), Day(10, 10, 100.1m, true), Day(10, 11, 100.15m)], current: 100.17m, today);

        Assert.Multiple(() =>
        {
            Assert.That(history.At(new DateOnly(2026, 10, 20)), Is.EqualTo(100.15m));
            Assert.That(history.At(today), Is.EqualTo(100.17m), "today - the reading now");
            Assert.That(history.Between(today, today), Is.EqualTo(new Consumption(0.02m, null)));
            Assert.That(history.Between(today.AddDays(-1), today.AddDays(-1)), Is.EqualTo(new Consumption(0m, null)), "nothing used yesterday");
            Assert.That(history.Between(new DateOnly(2026, 11, 1), today), Is.EqualTo(new Consumption(0.02m, null)));
            Assert.That(history.Between(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Is.EqualTo(new Consumption(0.15m, new DateOnly(2026, 10, 10))),
                        "the first reading entered on 10.10: since then");
            Assert.That(history.Between(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), Is.Null, "no readings yet");
        });
    }

    [Test]
    public void History_EnteredMonthly_ConsumptionBetweenTheEntries()
    {
        var history = new MeterHistory([Day(9, 23, 12000m, true), Day(9, 24, 12000m, true), Day(10, 25, 12210m, true)], current: 12210m, today);

        Assert.Multiple(() =>
        {
            Assert.That(history.Between(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), Is.EqualTo(new Consumption(210m, null)));
            Assert.That(history.Between(new DateOnly(2026, 11, 1), today), Is.EqualTo(new Consumption(0m, null)), "nothing entered in November yet");
        });
    }

    [TestCase("", '5', "5")]
    [TestCase("", '.', "0.")]
    [TestCase("0", '7', "7", TestName = "No leading zero")]
    [TestCase("0", '0', "0", TestName = "No second zero")]
    [TestCase("12", '.', "12.")]
    [TestCase("12.", '.', "12.", TestName = "One point")]
    [TestCase("12.345", '6', "12.345", TestName = "Three digits after the point")]
    [TestCase("123456789", '1', "123456789", TestName = "Nine digits before the point")]
    [TestCase("123456789", '.', "123456789.")]
    public void Keypad_Add(string digits, char key, string expected)
    {
        Assert.That(MeterReadingInput.Add(digits, key), Is.EqualTo(expected));
    }

    [TestCase("", null)]
    [TestCase("0.", 0)]
    [TestCase("12.5", 12.5)]
    [TestCase("123456789.123", 123456789.123)]
    public void Keypad_Value(string digits, decimal? expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MeterReadingInput.Value(digits), Is.EqualTo(expected));
            Assert.That(MeterReadingInput.Erase(digits), Is.EqualTo(digits.Length == 0 ? "" : digits[..^1]));
        });
    }
}
