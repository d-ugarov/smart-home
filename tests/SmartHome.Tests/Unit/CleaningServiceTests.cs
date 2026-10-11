using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SmartHome.Core.Configuration;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Storage;
using SmartHome.Core.Valves;
using System.Globalization;

namespace SmartHome.Tests.Unit;

/// <summary> How long the leak protection is held back does not depend on the timer of the end or on a saved value. </summary>
public class CleaningServiceTests
{
    private static readonly DateTimeOffset start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly HomeModel home = new(new HomeOptions
                                          {
                                              Entities =
                                              [
                                                  new EntityOptions {Id = "valve.shower", Name = "Shower water", Type = EntityType.Valve},
                                                  new EntityOptions {Id = "sensor.shower_leak", Name = "Shower leak", Type = EntityType.Sensor},
                                              ],
                                          });

    private IDeviceGateway gateway = null!;
    private IHomeStorage storage = null!;

    private Entity Valve => home.FindEntity("valve.shower")!;
    private Entity Sensor => home.FindEntity("sensor.shower_leak")!;

    [SetUp]
    public void SetUp()
    {
        gateway = Substitute.For<IDeviceGateway>();
        gateway.LeakSensorOf(Valve).Returns(Sensor);
        storage = Substitute.For<IHomeStorage>();
    }

    [Test]
    public async Task TimerLate_OverByTheClock()
    {
        var time = new TimersNeverFire(start);
        var service = Create(time);
        await service.StartCleaningAsync(Valve, TimeSpan.FromMinutes(30), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(29));
        var during = service.HoldsBack(Valve, Sensor);
        time.Advance(TimeSpan.FromMinutes(1));
        var after = service.HoldsBack(Valve, Sensor);

        Assert.Multiple(() =>
        {
            Assert.That(during, Is.True);
            Assert.That(after, Is.False);
            Assert.That(service.UntilOf(Valve), Is.Null);
        });
    }

    [Test]
    public async Task Restored_EndFurtherThanTheLongestCleaning_Shortened()
    {
        // The clock of the machine was set back after the cleaning started.
        var time = new TimersNeverFire(start);
        storage.LoadSettingAsync("cleaning_until:valve.shower", Arg.Any<CancellationToken>())
               .Returns((start + TimeSpan.FromHours(10)).ToString("O", CultureInfo.InvariantCulture));
        var service = Create(time);

        await service.RestoreAsync(CancellationToken.None);

        Assert.That(service.UntilOf(Valve), Is.EqualTo(start + CleaningService.MaxDuration));
    }

    [Test]
    public void LongerThanTheLongestCleaning_Refused()
    {
        var service = Create(new TimersNeverFire(start));

        Assert.That(() => service.StartCleaningAsync(Valve, CleaningService.MaxDuration + TimeSpan.FromMinutes(1), CancellationToken.None),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    private CleaningService Create(TimeProvider time)
    {
        var events = new HomeEventBus();
        var valves = new ValveService(gateway, events, time, Options.Create(new HomeOptions()), NullLogger<ValveService>.Instance);
        return new CleaningService(home, gateway, valves, storage, events, time, Options.Create(new CleaningSettings {Enabled = true}), NullLogger<CleaningService>.Instance);
    }

    private sealed class TimersNeverFire(DateTimeOffset now) : FakeTimeProvider(now)
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => base.CreateTimer(_ => { }, state, dueTime, period);
    }
}
