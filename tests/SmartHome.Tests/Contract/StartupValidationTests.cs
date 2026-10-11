using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary> A broken config stops the server at startup with a clear reason, instead of a house that half works. </summary>
[NonParallelizable]
public class StartupValidationTests
{
    [TestCase("Telegram:AllowedUsers", null, "AllowedUsers is empty", TestName = "Empty AllowedUsers")]
    [TestCase("Telegram:NotificationChatId", null, "NotificationChatId is required", TestName = "No chat for notifications")]
    [TestCase("Telegram:NotificationTopics:Leaks", "2", "unknown category 'Leaks'", TestName = "Topic of an unknown category")]
    [TestCase("Telegram:NotificationTopics:1", "2", "unknown category '1'", TestName = "Topic of a category by number")]
    [TestCase("Telegram:NotificationTopics:Security", "0", "the topic id must be positive", TestName = "Topic id zero")]
    [TestCase("MegaD:Controllers:1:Ports:15", "button.hall", "rules are not allowed there", TestName = "Button on a leak protection input")]
    [TestCase("MegaD:Controllers:1:Ports:12", "light.hall", "relay of valve 'valve.bathroom'", TestName = "Valve relay as a switch")]
    [TestCase("Home:Rules:0:Actions:0:Entity", "light.nowhere", "unknown action entity 'light.nowhere'", TestName = "Unknown entity in a rule")]
    [TestCase("Home:Rules:0:Actions:0:Entity", "sensor.bathroom_leak", "only switches are allowed", TestName = "Rule commands a sensor")]
    [TestCase("Home:Rules:6:Condition:Type", "AnyIs", "condition AnyIs needs State", TestName = "Condition without state")]
    [TestCase("Home:Menu:0:Pages:0:Items:0:Entity", "light.nowhere", "unknown entity 'light.nowhere'", TestName = "Unknown entity in the menu")]
    [TestCase("MegaD:Controllers:2:Ports:9", null, "Switch 'light.bedroom' is not bound to a MegaD port", TestName = "Entity without hardware")]
    [TestCase("Storage:DatabasePath", null, "Storage:DatabasePath is required", TestName = "No database")]
    [TestCase("Home:Entities:5:AutoOffAfter", "01:00:00", "AutoOffAfter is only for switches", TestName = "Auto-off of a sensor")]
    [TestCase("Home:Entities:11:Members:1", "sensor.bathroom_leak", "member entity 'sensor.bathroom_leak' is a Sensor", TestName = "Group of a sensor")]
    [TestCase("Home:Menu:0:Pages:0:Items:0:Entity", "group.bathroom_light", "group 'group.bathroom_light' has no state of its own", TestName = "Group in the menu")]
    [TestCase("MegaD:Controllers:2:Ports:13", "group.bathroom_light", "group 'group.bathroom_light' has no hardware", TestName = "Group on a port")]
    [TestCase("Home:Automations:0:Id", "auto_off", "'auto_off' is reserved", TestName = "Reserved automation id")]
    [TestCase("Home:Automations:0:Rules:0:Trigger:Entity", "sensor.bathroom_leak", "only switches and groups are allowed", TestName = "Trigger on a sensor")]
    [TestCase("Home:Automations:0:Rules:0:Conditions:1:PreviousStateLongerThan", "00:20:00", "only for a trigger by a state, without For",
              TestName = "Previous state of a held trigger")]
    [TestCase("Home:Automations:0:Rules:3:Actions:0:Cancel", "fan.nowhere.off", "no automation schedules this key", TestName = "Cancel of an unknown key")]
    [TestCase("Home:Automations:0:Rules:1:Actions:0:Schedule", null, "Delay of 'fan.bathroom' needs a Schedule key", TestName = "Delay without a key")]
    [TestCase("Home:TimeZone", null, "Home:TimeZone is required", TestName = "Schedule without a time zone")]
    [TestCase("Home:TimeZone", "Mars/Olympus", "unknown time zone 'Mars/Olympus'", TestName = "Unknown time zone")]
    [TestCase("Home:Automations:1:Rules:0:Trigger:Entity", "light.hall", "a trigger is either At (+ Days) or Entity + To (+ For)", TestName = "Time and state in one trigger")]
    [TestCase("Home:Automations:1:Rules:0:Trigger:At", "21:00:30", "At must be whole minutes", TestName = "Time with seconds")]
    [TestCase("Home:Automations:1:Rules:2:Trigger:Days:1", "Monday", "Days must be distinct days of the week", TestName = "Day listed twice")]
    [TestCase("Home:Automations:0:Rules:0:Trigger:Days:0", "Monday", "Days are only for a trigger At", TestName = "Days of a state trigger")]
    [TestCase("Home:Automations:1:Rules:0:Conditions:0:PreviousStateLongerThan", "00:20:00", "only for a trigger by a state", TestName = "Previous state in a schedule")]
    [TestCase("Home:Automations:1:Rules:0:Actions:0:Schedule", "floor.off", "a schedule only sends commands", TestName = "Delayed command in a schedule")]
    [TestCase("Home:Automations:0:Rules:3:Trigger:At", "12:00", "rules on states are separate automations", TestName = "Schedule mixed with state rules")]
    [TestCase("Home:Menu:0:Pages:2:Pages:0:Items:3:Valve", "Open", "needs either Valve (Open or Close) or Cleaning", TestName = "Valve button and cleaning in one")]
    [TestCase("Home:Menu:0:Pages:2:Pages:0:Items:0:Cleaning", "true", "Cleaning is set for 'sensor.bathroom_leak', which is not a valve", TestName = "Cleaning of a sensor")]
    [TestCase("MegaD:Controllers:1:Ports:6", null, "Valve 'valve.bathroom' has a cleaning button, but no sensor is bound to its close input",
              TestName = "Cleaning without a leak sensor")]
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

    [Test]
    public async Task RealConfig_StartsWithAutomations()
    {
        House.Real.EnsureAvailable();
        await using var factory = new HomeServerFactory(House.Real, new Dictionary<string, string?>
                                                                    {
                                                                        ["Automation:Enabled"] = "true",
                                                                        ["Automation:SchedulesEnabled"] = "true",
                                                                        ["Cleaning:Enabled"] = "true",
                                                                    });

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
