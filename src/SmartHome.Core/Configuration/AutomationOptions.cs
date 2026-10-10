using SmartHome.Core.Entities;

namespace SmartHome.Core.Configuration;

/// <summary>
/// An automation of the house: rules that run by themselves, without a press. Turned on and off from the menu as a
/// whole, e.g. "the fan of the shower follows the light" is four rules.
/// </summary>
public sealed class AutomationOptions
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<AutomationRuleOptions> Rules { get; init; } = [];
}

/// <summary> Trigger → conditions → actions. </summary>
public sealed class AutomationRuleOptions
{
    public TriggerOptions? Trigger { get; init; }
    public List<AutomationConditionOptions> Conditions { get; init; } = [];
    public List<AutomationActionOptions> Actions { get; init; } = [];
}

/// <summary> A switch or a group came to the state <see cref="To"/>; with <see cref="For"/> - and has held it that long. </summary>
public sealed class TriggerOptions
{
    public string Entity { get; init; } = "";
    public OnOff? To { get; init; }
    public TimeSpan? For { get; init; }
}

/// <summary>
/// One of: the state of a switch or a group (<see cref="Entity"/> + <see cref="Is"/>); how long the trigger entity was
/// in its previous state - only for a trigger without <see cref="TriggerOptions.For"/>.
/// </summary>
public sealed class AutomationConditionOptions
{
    public string? Entity { get; init; }
    public OnOff? Is { get; init; }
    public TimeSpan? PreviousStateLongerThan { get; init; }
    public TimeSpan? PreviousStateNotLongerThan { get; init; }
}

/// <summary>
/// One of:
/// <list type="bullet">
/// <item>a command now: <see cref="Entity"/> + <see cref="Command"/>;</item>
/// <item>a command later: <see cref="Schedule"/> (the key) + <see cref="Delay"/> + <see cref="Entity"/> + <see cref="Command"/>;
/// scheduling a key again replaces the scheduled command, the command survives restarts of the server;</item>
/// <item>cancel of a scheduled command: <see cref="Cancel"/> (the key).</item>
/// </list>
/// </summary>
public sealed class AutomationActionOptions
{
    public string? Entity { get; init; }
    public OnOff? Command { get; init; }
    public string? Schedule { get; init; }
    public TimeSpan? Delay { get; init; }
    public string? Cancel { get; init; }
}
