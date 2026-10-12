using SmartHome.Tests.Infrastructure;
using System.Net;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Local runs: <c>Telegram:UseFakeBot</c> - Telegram is never contacted, the menu is at <c>/debug/menu</c>;
/// <c>MegaD:UseSimulator</c> - controllers live in memory, nothing goes to the network.
/// </summary>
[NonParallelizable]
public class LocalRunTests
{
    [Test]
    public async Task FakeTelegramBot_ServerWorks_TelegramNotContacted()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["Telegram:UseFakeBot"] = "true"});
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

    [Test]
    public async Task Simulator_DebugMenu_NothingLeavesTheMachine()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?>
                                                                       {
                                                                           ["Telegram:UseFakeBot"] = "true",
                                                                           ["MegaD:UseSimulator"] = "true",
                                                                       });
        using var client = factory.CreateClient();

        var before = await client.GetStringAsync("/debug/menu?action=p:hall");
        var press = await client.GetAsync("/megad/1?pt=0&click=1");
        var after = await client.GetStringAsync("/debug/menu?action=p:hall");
        var toggled = await client.GetStringAsync("/debug/menu?action=t:hall:light.hall");

        Assert.Multiple(() =>
        {
            Assert.That(before, Does.StartWith("Hall").And.Contain("[🔘 Light]  /debug/menu?action=t%3Ahall%3Alight.hall"));
            Assert.That(press.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after, Does.Contain("[🟢 Light]"));
            Assert.That(toggled, Does.Contain("[🔘 Light]"));
            Assert.That(factory.Network.Commands, Is.Empty);
            Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
            Assert.That(factory.Network.Telegram.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task Simulator_Schedules_InTheDebugMenu()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?>
                                                                       {
                                                                           ["Telegram:UseFakeBot"] = "true",
                                                                           ["MegaD:UseSimulator"] = "true",
                                                                           ["Automation:Enabled"] = "true",
                                                                           ["Automation:SchedulesEnabled"] = "true",
                                                                       });
        using var client = factory.CreateClient();

        var dashboard = await client.GetStringAsync("/debug/menu");
        var schedules = await client.GetStringAsync("/debug/menu?action=s");
        var vacation = await client.GetStringAsync("/debug/menu?action=v%3APlusWeek");

        Assert.Multiple(() =>
        {
            Assert.That(dashboard, Does.Contain("[🕒 Schedules]  /debug/menu?action=s"));
            Assert.That(schedules, Does.Contain("Next runs:").And.Contain("— Floor heating: on]").And.Contain("[🟢 Floor heating]").And.Contain("[🏖 Vacation]"));
            Assert.That(vacation, Does.Contain("Until ").And.Contain("[✖️ End the vacation]"));
            Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
        });
    }

    [Test]
    public async Task Simulator_Meters_InTheDebugMenu()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?>
                                                                       {
                                                                           ["Telegram:UseFakeBot"] = "true",
                                                                           ["MegaD:UseSimulator"] = "true",
                                                                           ["Meters:Enabled"] = "true",
                                                                       });
        using var client = factory.CreateClient();

        var dashboard = await client.GetStringAsync("/debug/menu");
        var meter = await client.GetStringAsync("/debug/menu?action=mt%3Ameter.bathroom_cold_water");
        // The keypad of the page with the digits typed: "mk:{meter}:{opened}:" → "ms:{meter}:{opened}:42".
        var keypad = Uri.UnescapeDataString(meter.Split('\n').Single(x => x.StartsWith("[✏️ By the dial]")).Split("action=")[1].Trim());
        var saved = await client.GetStringAsync("/debug/menu?action=" + Uri.EscapeDataString("ms" + keypad[2..] + "42"));

        Assert.Multiple(() =>
        {
            Assert.That(dashboard, Does.Contain("[📊 Meters]  /debug/menu?action=m%3AThisMonth"));
            Assert.That(meter, Does.Contain("The reading is not set"));
            Assert.That(saved, Does.Contain("✅ Saved: 42.000 m³.").And.Contain("Now: 42.000 m³"), "the simulated counter is read");
            Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
        });
    }

    [Test]
    public async Task Simulator_Automations_InTheDebugMenu()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?>
                                                                       {
                                                                           ["Telegram:UseFakeBot"] = "true",
                                                                           ["MegaD:UseSimulator"] = "true",
                                                                           ["Automation:Enabled"] = "true",
                                                                       });
        using var client = factory.CreateClient();

        var dashboard = await client.GetStringAsync("/debug/menu");
        await client.GetAsync("/megad/1?pt=7&mdid=1&v=1");

        // Automations follow the state in the background.
        var automations = "";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!automations.Contains("Hall light", StringComparison.Ordinal) && !timeout.IsCancellationRequested)
            automations = await client.GetStringAsync("/debug/menu?action=a", CancellationToken.None);
        var turnedOff = await client.GetStringAsync("/debug/menu?action=a%3Aauto_off");

        Assert.Multiple(() =>
        {
            Assert.That(dashboard, Does.Contain("[⚙️ Automations]  /debug/menu?action=a"));
            Assert.That(automations, Does.Contain("• 8 h — Hall light: off (Auto-off)").And.Contain("[🟢 Bathroom fan follows the light]"));
            Assert.That(turnedOff, Does.Contain("Nothing is scheduled.").And.Contain("[🔘 Auto-off]"));
            Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
        });
    }

    [Test]
    public async Task DebugMenu_OnlyWithFakeBot()
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/debug/menu");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
