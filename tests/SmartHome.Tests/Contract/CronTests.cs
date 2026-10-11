using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// The schedules of the server repeat the Cron of the controllers one to one: for two weeks both run in parallel, then the
/// Cron is deleted (release 1, steps 4-5).
/// </summary>
public class CronTests
{
    [Test]
    public void RealHouse_SchedulesAreTheCronOfTheControllers()
    {
        House.Real.EnsureAvailable();

        var cron = House.Real.Cron;

        Assert.Multiple(() =>
        {
            Assert.That(cron, Is.Not.Empty, "the dumps have the Cron pages");
            Assert.That(House.Real.Schedules, Is.EquivalentTo(cron));
        });
    }
}
