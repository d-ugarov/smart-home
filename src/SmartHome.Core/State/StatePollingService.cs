using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Configuration;
using SmartHome.Core.Devices;
using SmartHome.Core.Home;

namespace SmartHome.Core.State;

/// <summary> Keeps <see cref="HomeState"/> close to the devices: changes made by the controllers themselves (scenarios, schedules) are not always reported. </summary>
internal sealed class StatePollingService(
    HomeModel home,
    IDeviceGateway gateway,
    HomeState state,
    TimeProvider timeProvider,
    IOptions<HomeOptions> options,
    ILogger<StatePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.StatePollInterval;
        if (interval == TimeSpan.Zero || home.Entities.Count == 0)
        {
            logger.LogInformation("State polling is off");
            return;
        }

        using var timer = new PeriodicTimer(interval, timeProvider);

        do
        {
            var values = await gateway.ReadAsync(home.Entities, stoppingToken);
            state.SetAll(values, StateSource.Poll);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
