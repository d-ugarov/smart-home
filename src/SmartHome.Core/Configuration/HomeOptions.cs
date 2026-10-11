using SmartHome.Core.Entities;

namespace SmartHome.Core.Configuration;

/// <summary>
/// The house: entities, the bot menu and input rules. Lists keep the order of the file - it is the order of
/// menu buttons and of rule evaluation.
/// </summary>
public sealed class HomeOptions
{
    public const string Section = "Home";

    public List<EntityOptions> Entities { get; init; } = [];

    /// <summary> Pages of the dashboard. </summary>
    public List<MenuPageOptions> Menu { get; init; } = [];

    public List<InputRuleOptions> Rules { get; init; } = [];

    /// <summary> Run by themselves, without a press; work only when <see cref="Automations.AutomationSettings.Enabled"/>. </summary>
    public List<AutomationOptions> Automations { get; init; } = [];

    /// <summary> IANA id, e.g. <c>Europe/Moscow</c>; schedules run by it. Required when the house has schedules. </summary>
    public string TimeZone { get; init; } = "";

    /// <summary> How often the state of all entities is read from the devices; zero disables polling. </summary>
    public TimeSpan StatePollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a valve command blocks the next one when the end of the pulse is not reported by the controller.
    /// The real pulse is shorter (25 s + 1 s pause).
    /// </summary>
    public TimeSpan ValvePulseTimeout { get; init; } = TimeSpan.FromSeconds(40);
}

public sealed class EntityOptions
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public EntityType? Type { get; init; }
    public StateIconsOptions? Icons { get; init; }

    /// <summary> Switches only: turned off by the server when left on longer than this ("forgotten" light). </summary>
    public TimeSpan? AutoOffAfter { get; init; }

    /// <summary> Groups only: the switches of the group. </summary>
    public List<string> Members { get; init; } = [];
}

public sealed class StateIconsOptions
{
    public string On { get; init; } = "";
    public string Off { get; init; } = "";
}

/// <summary> A menu page: buttons of sub-pages, then entity items, then the dynamic list. </summary>
public sealed class MenuPageOptions
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public List<MenuPageOptions> Pages { get; init; } = [];
    public List<MenuItemOptions> Items { get; init; } = [];

    /// <summary> Every switch of the house that is in this state right now, e.g. "now on". </summary>
    public OnOff? ListSwitches { get; init; }
}

public sealed class MenuItemOptions
{
    public string Entity { get; init; } = "";

    /// <summary> Button text; the entity name when empty. </summary>
    public string? Label { get; init; }

    /// <summary> For a valve, this or <see cref="Cleaning"/>: the button opens or closes it (after a confirmation). </summary>
    public ValveCommand? Valve { get; init; }

    /// <summary>
    /// For a valve with a leak sensor: the button opens the page of the cleaning of the bathroom. Shown when
    /// <see cref="Valves.CleaningSettings.Enabled"/>.
    /// </summary>
    public bool Cleaning { get; init; }
}

/// <summary>
/// "A press of a button → conditions on the switches → commands". Rules of a button are evaluated in order on one
/// fresh reading of the devices; see <see cref="Rules.InputRuleEngine"/>.
/// </summary>
public sealed class InputRuleOptions
{
    public string Button { get; init; } = "";
    public List<PressKind> Presses { get; init; } = [];
    public ConditionOptions? Condition { get; init; }
    public List<RuleActionOptions> Actions { get; init; } = [];
    public RuleResponse? Response { get; init; }

    /// <summary> The rule stops the evaluation of the following rules when it fires. </summary>
    public bool IsFinal { get; init; }
}

public sealed class ConditionOptions
{
    public ConditionType? Type { get; init; }

    /// <summary> Required for <see cref="ConditionType.AllAre"/>, <see cref="ConditionType.AnyIs"/>, <see cref="ConditionType.NoneIs"/>. </summary>
    public OnOff? State { get; init; }

    public List<string> Entities { get; init; } = [];
}

public enum ConditionType
{
    /// <summary> Every switch is in <see cref="ConditionOptions.State"/>. </summary>
    AllAre,

    /// <summary> At least one switch is in <see cref="ConditionOptions.State"/>. </summary>
    AnyIs,

    /// <summary> No switch is in <see cref="ConditionOptions.State"/>. </summary>
    NoneIs,

    /// <summary> All switches are in the same state, whichever it is. </summary>
    AllSame,

    /// <summary> Some switches are on and some are off. </summary>
    Mixed,
}

public sealed class RuleActionOptions
{
    public string Entity { get; init; } = "";
    public SwitchCommand? Command { get; init; }

    /// <summary> Short pauses are kept inside the processing of the press, before the answer to the controller. </summary>
    public TimeSpan DelayBefore { get; init; }

    public TimeSpan DelayAfter { get; init; }
}

public enum RuleResponse
{
    /// <summary> 200: the controller does not run its own scenario. </summary>
    Handled,

    /// <summary> 204 after the commands: the controller runs its own scenario as well. </summary>
    ControllerActs,
}
