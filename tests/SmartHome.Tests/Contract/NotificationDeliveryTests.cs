using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SmartHome.Core.Notifications;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary> Telegram being slow or unreachable must not hold up the rest of the house. </summary>
[NonParallelizable]
public class NotificationDeliveryTests
{
    [Test]
    public async Task TelegramHangs_ControllerRestartStillHandled()
    {
        var hanging = Substitute.For<INotificationChannel>();
        hanging.SendAsync(default!, default).ReturnsForAnyArgs(x => Task.Delay(Timeout.Infinite, x.Arg<CancellationToken>()));

        await using var factory = new HomeServerFactory(House.Example, testServices: x => x.AddSingleton(hanging));
        using var client = factory.CreateClient();

        // Kitchen rules read controller 1 only.
        await client.GetAsync("/megad/1?pt=1&click=1");
        await client.GetAsync("/megad/2?st=1");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (factory.Network.Devices["2"].StatusRequests == 0 && !timeout.IsCancellationRequested)
            await Task.Delay(20);

        Assert.That(factory.Network.Devices["2"].StatusRequests, Is.Positive, "the state of the restarted controller was not read");
    }
}
