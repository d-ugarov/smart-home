using SmartHome.Core.Entities;

namespace SmartHome.Core.Automations;

/// <summary> Rules that run by themselves; turned on and off as a whole. </summary>
public sealed class Automation(string id, string name)
{
    /// <summary> Turning off of forgotten switches (<see cref="Entity.AutoOffAfter"/>): switched on and off like an automation. </summary>
    public const string AutoOffId = "auto_off";

    public string Id { get; } = id;
    public string Name { get; } = name;
    public IReadOnlyList<AutomationRule> Rules { get; internal set; } = [];

    public override string ToString() => Id;
}

public sealed class AutomationRule(Automation automation, int index, AutomationTrigger trigger, IReadOnlyList<AutomationCondition> conditions,
                                   IReadOnlyList<AutomationAction> actions)
{
    public Automation Automation { get; } = automation;
    public int Index { get; } = index;
    public AutomationTrigger Trigger { get; } = trigger;
    public IReadOnlyList<AutomationCondition> Conditions { get; } = conditions;
    public IReadOnlyList<AutomationAction> Actions { get; } = actions;

    /// <summary> Switches the rule commands, now or later. </summary>
    public IReadOnlySet<Entity> Targets { get; } = actions.Select(x => x switch
                                                          {
                                                              AutomationAction.Command c => c.Target,
                                                              AutomationAction.Schedule s => s.Target,
                                                              _ => null,
                                                          })
                                                          .OfType<Entity>()
                                                          .ToHashSet();

    /// <summary> Key of the wait of a trigger with <see cref="AutomationTrigger.For"/>. </summary>
    public string HeldKey => $"held:{Automation.Id}:{Index}";

    public override string ToString() => $"{Automation.Id} #{Index + 1}";
}

/// <summary> The entity came to <see cref="To"/>; with <see cref="For"/> - and has held it that long. </summary>
public sealed record AutomationTrigger(Entity Entity, OnOff To, TimeSpan? For);

public abstract record AutomationCondition
{
    /// <summary> The state in memory: an unknown state does not hold. </summary>
    public sealed record StateIs(Entity Entity, OnOff State) : AutomationCondition;

    /// <summary> The trigger entity was in its previous state longer than this, e.g. the light was on for more than 20 minutes. </summary>
    public sealed record PreviousStateLongerThan(TimeSpan Duration) : AutomationCondition;

    public sealed record PreviousStateNotLongerThan(TimeSpan Duration) : AutomationCondition;
}

public abstract record AutomationAction
{
    public sealed record Command(Entity Target, OnOff State) : AutomationAction;

    public sealed record Schedule(string Key, TimeSpan Delay, Entity Target, OnOff State) : AutomationAction;

    public sealed record Cancel(string Key) : AutomationAction;
}
