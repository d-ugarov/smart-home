using Microsoft.Extensions.DependencyInjection;
using SmartHome.Core.Entities;
using SmartHome.Core.State;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary> Current state of the house in memory: what the controllers report, restarts and periodic polling. </summary>
[NonParallelizable]
public class StateTests
{
    [Test]
    public async Task OutputEvent_UpdatesState_Answers204()
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/megad/1?pt=7&mdid=1&v=1");
        var state = factory.Services.GetRequiredService<HomeState>().Get("light.hall");

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(204));
            Assert.That(state?.Value, Is.EqualTo(new OnOffValue(OnOff.On)));
            Assert.That(state?.Source, Is.EqualTo(StateSource.Device));
        });
    }

    [Test]
    public async Task ControllerRestarted_ItsStateIsReadAgain_AfterTheAnswer()
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/megad/2?st=1");
        await WaitUntilAsync(() => factory.Network.Devices["2"].StatusRequests > 0);

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(204));
            Assert.That(factory.Network.Devices["1"].StatusRequests, Is.Zero);
            Assert.That(factory.Services.GetRequiredService<HomeState>().Get("light.bedroom_night")?.Value, Is.EqualTo(new OnOffValue(OnOff.On)));
        });
    }

    [Test]
    public async Task Polling_ReadsEveryController()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["Home:StatePollInterval"] = "00:00:00.100"});
        _ = factory.Server;

        await WaitUntilAsync(() => factory.Network.Devices.Values.All(x => x.StatusRequests >= 2));

        Assert.That(factory.Services.GetRequiredService<HomeState>().Get("heater.floor")?.Value, Is.EqualTo(new OnOffValue(OnOff.On)));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }
}
