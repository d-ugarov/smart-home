using MegaDTelegramRemoteControl.Tests.Infrastructure;
using System.Net;

namespace MegaDTelegramRemoteControl.Tests.Contract;

/// <summary> Local runs with <c>PlatformConfig.UseFakeTelegramBot</c>: the server works, Telegram is never contacted. </summary>
[NonParallelizable]
public class LocalRunTests
{
    [Test]
    public async Task FakeTelegramBot_ServerWorks_TelegramNotContacted()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["PlatformConfig:UseFakeTelegramBot"] = "true"});
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/megad/1?pt=0&click=1&cnt=1");
        await Task.Delay(500);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(factory.Network.Commands, Is.Not.Empty);
            Assert.That(factory.Network.Telegram.Calls, Is.Empty);
        });
    }
}
