using SmartHome.ConfigConverter;
using System.Text.Json.Nodes;

namespace SmartHome.Tests.Unit;

/// <summary>
/// The converter keeps the behaviour of <c>master</c>: what master skipped at startup is left out (and listed), what the
/// new server rejects is an error. That the converted houses behave as master is proven by the golden tests.
/// </summary>
public class ConverterTests
{
    private const string Devices = """
        "DevicesConfig": { "Devices": [ { "Id": "1", "Name": "MegaD-1", "Ip": "http://10.0.0.1", "Pwd": "p", "DevicePorts": [
          { "Id": "0", "Type": "IN", "Name": "Switch" },
          { "Id": "1", "Type": "IN", "Name": "Leak", "InOutSWModeIcons": { "On": "🔴", "Off": "🔘" } },
          { "Id": "5", "Type": "IN", "Name": "Meter" },
          { "Id": "7", "Type": "OUT", "OutMode": "SW", "Name": "Light" },
          { "Id": "8", "Type": "OUT", "OutMode": "SW", "Name": "Lamp" },
          { "Id": "22", "Type": "OUT", "OutMode": "SW", "Name": "Valve open" },
          { "Id": "23", "Type": "OUT", "OutMode": "SW", "Name": "Valve close" } ] } ] }
        """;

    private const string Map = """
        {
          "Ports": { "1/0": "button.switch", "1/1": "sensor.leak", "1/7": "light.light", "1/8": "light.lamp" },
          "Pages": { "Home": "home" },
          "HardwareInputs": { "1": [ 1, 4 ] },
          "Valves": [ { "Id": "valve.water", "Name": "Water", "Controller": "1", "OpenInput": 4, "CloseInput": 1, "OpenRelay": 22, "CloseRelay": 23 } ]
        }
        """;

    [Test]
    public void SkippedByMaster_LeftOut_AndListed()
    {
        var result = Convert("""
            { "SourcePortState": { "DeviceId": "1", "PortId": "0", "Status": { "InPortCommands": [ "Click" ] } },
              "AdditionalConditions": { "Ports": [ { "DeviceId": "1", "PortId": "99" } ], "Type": "StatesAreEqual", "Status": "On" },
              "DestinationPortStates": [ { "DeviceId": "1", "PortId": "99", "Action": { "SWCommand": "Off" } },
                                         { "DeviceId": "1", "PortId": "7", "Action": { "SWCommand": "Switch" } } ],
              "Result": "DoNothing" },
            { "SourcePortState": { "DeviceId": "2", "PortId": "0", "Status": { "InPortCommands": [ "Click" ] } },
              "DestinationPortStates": [ { "DeviceId": "1", "PortId": "7", "Action": { "SWCommand": "Off" } } ], "Result": "DoNothing" }
            """);

        var rules = result.Automations["Home"]!["Rules"]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(rules, Has.Count.EqualTo(1));
            Assert.That(rules[0]!["Condition"], Is.Null, "no condition ports left - the rule fires unconditionally, as in master");
            Assert.That(rules[0]!["Actions"]!.ToJsonString(), Is.EqualTo("""[{"Entity":"light.light","Command":"Toggle"}]"""));
            Assert.That(result.Notes, Has.Some.Contains("Rule #2").And.Some.Contains("Port 1/5 'Meter'"));
        });
    }

    [TestCase("StatesAreEqual", null, "AllSame")]
    [TestCase("StatesAreEqual", "\"On\"", "AllAre")]
    [TestCase("StatesAnyEqual", null, "AllSame")]
    [TestCase("StatesAnyEqual", "\"Off\"", "AnyIs")]
    [TestCase("StatesNotEqual", null, "Mixed")]
    [TestCase("StatesNotEqual", "\"On\"", "NoneIs")]
    public void Conditions(string type, string? status, string expected)
    {
        var result = Convert($$"""
            { "SourcePortState": { "DeviceId": "1", "PortId": "0", "Status": { "InPortCommands": [ "KeyPressed", "LongClick" ] } },
              "AdditionalConditions": { "Ports": [ { "DeviceId": "1", "PortId": "7" }, { "DeviceId": "1", "PortId": "8" } ], "Type": "{{type}}" {{(status != null ? $", \"Status\": {status}" : "")}} },
              "DestinationPortStates": [ { "DeviceId": "1", "PortId": "7", "Action": { "SWCommand": "On" }, "DelayBeforeActionInMilliseconds": 100 } ],
              "Result": "Default", "IsFinal": true }
            """);

        var rule = result.Automations["Home"]!["Rules"]![0]!;

        Assert.Multiple(() =>
        {
            Assert.That(rule["Condition"]!["Type"]!.GetValue<string>(), Is.EqualTo(expected));
            Assert.That(rule["Presses"]!.ToJsonString(), Is.EqualTo("""["Press","LongPress"]"""));
            Assert.That(rule["Actions"]![0]!["DelayBefore"]!.GetValue<string>(), Is.EqualTo("00:00:00.100"));
            Assert.That(rule["Response"]!.GetValue<string>(), Is.EqualTo("ControllerActs"));
            Assert.That(rule["IsFinal"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public void ValveRelayInRule_Error()
    {
        var result = Convert("""
            { "SourcePortState": { "DeviceId": "1", "PortId": "0", "Status": { "InPortCommands": [ "Click" ] } },
              "DestinationPortStates": [ { "DeviceId": "1", "PortId": "23", "Action": { "SWCommand": "On" } } ], "Result": "DoNothing" }
            """);

        Assert.That(result.Errors, Has.Some.Contains("relay 1/23 of valve valve.water"));
    }

    [Test]
    public void RuleOnHardwareInput_Error()
    {
        var result = Convert("""
            { "SourcePortState": { "DeviceId": "1", "PortId": "1", "Status": { "InPortCommands": [ "Click" ] } },
              "DestinationPortStates": [ { "DeviceId": "1", "PortId": "7", "Action": { "SWCommand": "On" } } ], "Result": "DoNothing" }
            """);

        Assert.That(result.Errors, Has.Some.Contains("hardware input"));
    }

    [Test]
    public void ValveRelayButtons_BecomeValveButtons_SensorsAndSwitchesAsEntities()
    {
        var result = Convert(rules: "", menu: """
            { "Name": "Home", "Items": [ { "DeviceId": "1", "PortId": "1" }, { "DeviceId": "1", "PortId": "22", "CustomName": "Open" },
                                         { "DeviceId": "1", "PortId": "23" }, { "DeviceId": "1", "PortId": "7", "CustomName": "Light" } ],
              "ItemsConditions": [ { "Type": "PortOutCurrentStatus", "Status": "On" } ] }
            """);

        var page = result.Entities["Home"]!["Menu"]![0]!;
        var entities = result.Entities["Home"]!["Entities"]!.AsArray().Select(x => $"{x!["Id"]}:{x["Type"]}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Errors, Is.Empty);
            Assert.That(page["Items"]!.ToJsonString(), Is.EqualTo("""[{"Entity":"sensor.leak"},{"Entity":"valve.water","Label":"Open","Valve":"Open"},""" +
                                                                  """{"Entity":"valve.water","Label":"Valve close","Valve":"Close"},{"Entity":"light.light"}]"""));
            Assert.That(page["ListSwitches"]!.GetValue<string>(), Is.EqualTo("On"));
            Assert.That(entities, Is.EqualTo(new[] {"sensor.leak:Sensor", "light.light:Switch", "light.lamp:Switch", "valve.water:Valve"}));
            Assert.That(result.Devices["MegaD"]!["Controllers"]!["1"]!["Password"], Is.Null, "passwords stay out of the house files");
        });
    }

    [Test]
    public void MissingIds_Errors()
    {
        var result = Converter.Convert(MasterConfig.Parse($$"""{ {{Devices}}, "HomeMapConfig": { "Locations": [ { "Name": "Other" } ] } }"""),
                                       ConversionMap.Parse(Map), withPasswords: false);

        Assert.That(result.Errors, Has.Some.Contains("Menu page 'Other': no id"));
    }

    private static ConversionResult Convert(string rules, string menu = """{ "Name": "Home" }""") =>
        Converter.Convert(MasterConfig.Parse($$"""
                                               { {{Devices}},
                                                 "HomeMapConfig": { "Locations": [ {{menu}} ] },
                                                 "AutomationConfig": { "Triggers": [ {{rules}} ] } }
                                               """),
                          ConversionMap.Parse(Map),
                          withPasswords: false);
}
