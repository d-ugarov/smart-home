using SmartHome.Core.Entities;

namespace SmartHome.Core.Automations;

/// <summary> Something to do later; the key is unique, scheduling the same key again replaces the action. </summary>
/// <param name="AutomationId"> The automation it belongs to (<see cref="Automation.AutoOffId"/> for the auto-off). </param>
public sealed record DelayedAction(string Key, DateTimeOffset Due, string AutomationId, DelayedWork Work)
{
    public static string AutoOffKey(Entity entity) => $"auto_off:{entity.Id}";

    public bool Targets(Entity entity) => Work switch
    {
        DelayedWork.Command x => x.Target == entity,
        DelayedWork.Check x => x.Rule.Targets.Contains(entity),
        _ => false,
    };
}

public abstract record DelayedWork
{
    public sealed record Command(Entity Target, OnOff State) : DelayedWork;

    /// <summary> The trigger of the rule has held its state for <see cref="AutomationTrigger.For"/>: check the conditions and act. </summary>
    public sealed record Check(AutomationRule Rule) : DelayedWork;
}
