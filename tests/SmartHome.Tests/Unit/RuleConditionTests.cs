using SmartHome.Core.Configuration;
using SmartHome.Core.Entities;
using SmartHome.Core.Rules;

namespace SmartHome.Tests.Unit;

/// <summary>
/// Conditions as in <c>master</c>, including a reading where some controllers did not answer: their switches are left
/// out of the check.
/// </summary>
public class RuleConditionTests
{
    private static readonly Entity a = new("light.a", "A", EntityType.Switch, null);
    private static readonly Entity b = new("light.b", "B", EntityType.Switch, null);

    [TestCase(ConditionType.AllAre, OnOff.On, "on,on", true)]
    [TestCase(ConditionType.AllAre, OnOff.On, "on,off", false)]
    [TestCase(ConditionType.AllAre, OnOff.On, "on,-", true)]
    [TestCase(ConditionType.AllAre, OnOff.On, "-,-", true)]
    [TestCase(ConditionType.AnyIs, OnOff.On, "off,on", true)]
    [TestCase(ConditionType.AnyIs, OnOff.On, "off,off", false)]
    [TestCase(ConditionType.AnyIs, OnOff.On, "-,-", false)]
    [TestCase(ConditionType.NoneIs, OnOff.On, "off,off", true)]
    [TestCase(ConditionType.NoneIs, OnOff.On, "off,on", false)]
    [TestCase(ConditionType.NoneIs, OnOff.On, "-,-", true)]
    [TestCase(ConditionType.AllSame, null, "on,on", true)]
    [TestCase(ConditionType.AllSame, null, "off,off", true)]
    [TestCase(ConditionType.AllSame, null, "on,off", false)]
    [TestCase(ConditionType.AllSame, null, "on,-", true)]
    [TestCase(ConditionType.AllSame, null, "-,-", false)]
    [TestCase(ConditionType.Mixed, null, "on,off", true)]
    [TestCase(ConditionType.Mixed, null, "on,on", false)]
    [TestCase(ConditionType.Mixed, null, "on,-", false)]
    public void IsMet(ConditionType type, OnOff? state, string snapshot, bool expected)
    {
        var condition = new RuleCondition(type, state, [a, b]);

        Assert.That(condition.IsMet(Snapshot(snapshot)), Is.EqualTo(expected));
    }

    private static Dictionary<string, EntityValue> Snapshot(string states)
    {
        var values = states.Split(',');
        var result = new Dictionary<string, EntityValue>();

        foreach (var (entity, value) in new[] {a, b}.Zip(values))
        {
            if (value != "-")
                result[entity.Id] = new OnOffValue(value == "on" ? OnOff.On : OnOff.Off);
        }

        return result;
    }
}
