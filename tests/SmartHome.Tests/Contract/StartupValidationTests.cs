using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary> A broken config stops the server at startup with a clear reason, instead of a house that half works. </summary>
[NonParallelizable]
public class StartupValidationTests
{
    [TestCase("Telegram:AllowedUsers", null, "AllowedUsers is empty", TestName = "Empty AllowedUsers")]
    [TestCase("Telegram:NotificationChatId", null, "NotificationChatId is required", TestName = "No chat for notifications")]
    [TestCase("MegaD:Controllers:1:Ports:15", "button.hall", "rules are not allowed there", TestName = "Button on a leak protection input")]
    [TestCase("MegaD:Controllers:1:Ports:12", "light.hall", "relay of valve 'valve.bathroom'", TestName = "Valve relay as a switch")]
    [TestCase("Home:Rules:0:Actions:0:Entity", "light.nowhere", "unknown action entity 'light.nowhere'", TestName = "Unknown entity in a rule")]
    [TestCase("Home:Rules:0:Actions:0:Entity", "sensor.bathroom_leak", "only switches are allowed", TestName = "Rule commands a sensor")]
    [TestCase("Home:Rules:6:Condition:Type", "AnyIs", "condition AnyIs needs State", TestName = "Condition without state")]
    [TestCase("Home:Menu:0:Pages:0:Items:0:Entity", "light.nowhere", "unknown entity 'light.nowhere'", TestName = "Unknown entity in the menu")]
    [TestCase("MegaD:Controllers:2:Ports:9", null, "Switch 'light.bedroom' is not bound to a MegaD port", TestName = "Entity without hardware")]
    public async Task BrokenConfig_ServerDoesNotStart(string key, string? value, string reason)
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {[key] = value});

        var error = Assert.Catch(() => factory.CreateClient());

        Assert.That(Messages(error), Does.Contain(reason));
    }

    [Test]
    public async Task ExampleConfig_Starts()
    {
        await using var factory = new HomeServerFactory(House.Example);

        Assert.DoesNotThrow(() => factory.CreateClient());
    }

    private static string Messages(Exception? error)
    {
        var messages = new List<string>();
        for (var e = error; e != null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }
}
