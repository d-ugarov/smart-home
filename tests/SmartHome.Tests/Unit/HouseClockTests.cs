using Microsoft.Extensions.Time.Testing;
using SmartHome.Core.Automations;
using SmartHome.Core.Configuration;
using SmartHome.Core.Home;

namespace SmartHome.Tests.Unit;

/// <summary> Runs of schedules in the local time of the house; 10 Oct 2026 is a Saturday, London is on BST (UTC+1) till 25 Oct. </summary>
public class HouseClockTests
{
    [TestCase("Europe/London", "21:00", "", "2026-10-10T12:00:00Z", "2026-10-10T20:00:00Z", TestName = "Later today, local time")]
    [TestCase("Europe/London", "21:00", "", "2026-10-10T20:00:00Z", "2026-10-11T20:00:00Z", TestName = "Strictly after: the run itself is not the next one")]
    [TestCase("Europe/London", "03:00", "", "2026-10-10T20:00:00Z", "2026-10-11T02:00:00Z", TestName = "Over midnight")]
    [TestCase("Europe/London", "07:00", "Monday,Tuesday,Wednesday,Thursday,Friday", "2026-10-09T06:00:00Z", "2026-10-12T06:00:00Z",
              TestName = "Weekdays: from Friday to Monday")]
    [TestCase("Europe/London", "09:00", "Saturday,Sunday", "2026-10-11T08:00:00Z", "2026-10-17T08:00:00Z", TestName = "Weekend: from Sunday to Saturday")]
    [TestCase("Europe/London", "06:00", "Sunday", "2026-10-11T05:00:00Z", "2026-10-18T05:00:00Z", TestName = "Once a week")]
    [TestCase("Europe/London", "01:30", "", "2026-10-24T12:00:00Z", "2026-10-25T00:30:00Z", TestName = "Clock falls back: the first of the two 01:30")]
    [TestCase("Europe/London", "01:30", "", "2026-10-25T00:30:00Z", "2026-10-26T01:30:00Z", TestName = "Clock falls back: the second 01:30 is not a run")]
    [TestCase("Europe/London", "03:00", "", "2026-10-24T02:00:00Z", "2026-10-25T03:00:00Z", TestName = "Clock falls back: 25 h between runs")]
    [TestCase("Europe/London", "01:30", "", "2026-03-28T12:00:00Z", "2026-03-29T01:00:00Z", TestName = "Clock springs forward: a skipped time runs at the end of the gap")]
    [TestCase("Europe/Moscow", "21:00", "", "2026-10-10T12:00:00Z", "2026-10-10T18:00:00Z", TestName = "Moscow, UTC+3")]
    public void Next(string zone, string at, string days, string after, string expected)
    {
        var clock = Clock(zone);
        var trigger = Trigger(at, days);

        var next = clock.Next(trigger, DateTimeOffset.Parse(after));

        Assert.Multiple(() =>
        {
            Assert.That(next, Is.EqualTo(DateTimeOffset.Parse(expected)));
            Assert.That(clock.IsRunOf(trigger, next), Is.True);
            Assert.That(clock.IsRunOf(trigger, next.AddMinutes(1)), Is.False);
        });
    }

    [Test]
    public void LocalDay()
    {
        var clock = Clock("Europe/London", DateTimeOffset.Parse("2026-10-10T23:30:00Z"));

        Assert.Multiple(() =>
        {
            Assert.That(clock.Today, Is.EqualTo(new DateOnly(2026, 10, 11)), "00:30 BST");
            Assert.That(clock.StartOf(new DateOnly(2026, 10, 11)), Is.EqualTo(DateTimeOffset.Parse("2026-10-10T23:00:00Z")));
        });
    }

    private static HouseClock Clock(string zone, DateTimeOffset? now = null) =>
        new(new HomeModel(new HomeOptions {TimeZone = zone}), new FakeTimeProvider(now ?? DateTimeOffset.Parse("2026-10-10T12:00:00Z")));

    private static AutomationTrigger.TimeOfDay Trigger(string at, string days) =>
        new(TimeOnly.Parse(at), days.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<DayOfWeek>).ToHashSet());
}
