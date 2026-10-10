using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SmartHome.Core.Common;
using SmartHome.Core.Configuration;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Valves;

namespace SmartHome.Tests.Unit;

/// <summary> A sent valve command blocks the next one until the pulse is over - or, if its end is never reported, a timeout. </summary>
public class ValveServiceTests
{
    private static readonly Entity valve = new("valve.shower", "Shower water", EntityType.Valve, null);

    private readonly FakeTimeProvider time = new();
    private IDeviceGateway gateway = null!;
    private ValveService service = null!;

    [SetUp]
    public void SetUp()
    {
        gateway = Substitute.For<IDeviceGateway>();
        gateway.ReadAsync(default!, default).ReturnsForAnyArgs(new Dictionary<string, EntityValue> {[valve.Id] = new ValveValue(ValveActivity.Idle)});
        gateway.RunValveAsync(default!, default, default).ReturnsForAnyArgs(OperationResult.Ok());

        service = new ValveService(gateway, new HomeEventBus(), time, Options.Create(new HomeOptions {ValvePulseTimeout = TimeSpan.FromSeconds(40)}),
                                   NullLogger<ValveService>.Instance);
    }

    [Test]
    public async Task SecondCommand_Busy_UntilTimeout()
    {
        var first = await service.RunAsync(valve, ValveCommand.Close, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(39));
        var second = await service.RunAsync(valve, ValveCommand.Open, CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(2));
        var third = await service.RunAsync(valve, ValveCommand.Open, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.InstanceOf<CommandOutcome.Sent>());
            Assert.That(second, Is.InstanceOf<CommandOutcome.Busy>());
            Assert.That(third, Is.InstanceOf<CommandOutcome.Sent>());
        });
    }

    [Test]
    public async Task EndOfPulse_OfAnotherLine_DoesNotUnblock()
    {
        await service.RunAsync(valve, ValveCommand.Close, CancellationToken.None);

        service.OnPulseFinished(valve, ValveCommand.Open);
        var blocked = service.ActivityOf(valve, new ValveValue(ValveActivity.Idle));
        service.OnPulseFinished(valve, ValveCommand.Close);
        var free = service.ActivityOf(valve, new ValveValue(ValveActivity.Idle));

        Assert.Multiple(() =>
        {
            Assert.That(blocked, Is.EqualTo(ValveActivity.Closing));
            Assert.That(free, Is.EqualTo(ValveActivity.Idle));
        });
    }

    [Test]
    public async Task RelayOn_Busy_NoCommand()
    {
        gateway.ReadAsync(default!, default).ReturnsForAnyArgs(new Dictionary<string, EntityValue> {[valve.Id] = new ValveValue(ValveActivity.Opening)});

        var outcome = await service.RunAsync(valve, ValveCommand.Close, CancellationToken.None);

        Assert.That(outcome, Is.InstanceOf<CommandOutcome.Busy>());
        await gateway.DidNotReceiveWithAnyArgs().RunValveAsync(default!, default, default);
    }

    [Test]
    public async Task FailedCommand_DoesNotBlock()
    {
        gateway.RunValveAsync(default!, default, default).ReturnsForAnyArgs(OperationResult.Failed("offline"));

        await service.RunAsync(valve, ValveCommand.Close, CancellationToken.None);

        Assert.That(service.ActivityOf(valve, new ValveValue(ValveActivity.Idle)), Is.EqualTo(ValveActivity.Idle));
    }
}
