namespace SmartHome.Core.Entities;

/// <summary>
/// What the house controls and observes. The id is stable and readable (<c>light.hall</c>): the part before the dot is
/// the class of the entity, rules and clients refer to entities only by id. Binding to hardware belongs to the integration.
/// </summary>
public sealed record Entity(string Id, string Name, EntityType Type, StateIcons? Icons)
{
    public string Class => Id[..Id.IndexOf('.')];

    /// <summary> A switch left on longer than this is turned off by the server; null - never. </summary>
    public TimeSpan? AutoOffAfter { get; init; }

    /// <summary> Switches of a <see cref="EntityType.Group"/>. </summary>
    public IReadOnlyList<Entity> Members { get; init; } = [];

    public override string ToString() => $"{Name} ({Id})";
}

public enum EntityType
{
    /// <summary> A relay: on/off, commands <see cref="SwitchCommand"/>. </summary>
    Switch,

    /// <summary> An input that is only observed, e.g. a leak sensor. </summary>
    Sensor,

    /// <summary> An input whose presses trigger input rules. </summary>
    Button,

    /// <summary> A pair of water valves driven by a scenario of the controller, see <see cref="ValveCommand"/>. </summary>
    Valve,

    /// <summary>
    /// Several switches seen as one, e.g. the bright and the warm light of a bathroom: on when any of them is on. Has no
    /// hardware; used by automations.
    /// </summary>
    Group,
}

public enum OnOff
{
    Off,
    On,
}

public enum SwitchCommand
{
    Off,
    On,

    /// <summary> Sent to the controller as is: the relay flips whatever the server thinks its state is. </summary>
    Toggle,
}

public enum ValveCommand
{
    Open,
    Close,
}

/// <summary> Whether a valve pulse is running. Valves have no position feedback, only the pulse is visible. </summary>
public enum ValveActivity
{
    Idle,
    Opening,
    Closing,
}

public enum PressKind
{
    /// <summary> Input closed (MegaD sends no <c>m</c>/<c>click</c>). </summary>
    Press,

    /// <summary> Input released (<c>m=1</c>). </summary>
    Release,

    /// <summary> <c>click=1</c>. </summary>
    Click,

    /// <summary> <c>click=2</c>. </summary>
    DoubleClick,

    /// <summary> Held (<c>m=2</c>). </summary>
    LongPress,
}

/// <summary> State icons in the menu; without them the state is shown as text: <c>[On]</c>. </summary>
public sealed record StateIcons(string On, string Off)
{
    public string For(OnOff state) => state == OnOff.On ? On : Off;
}

/// <summary> Current value of an entity as read from the device. </summary>
public abstract record EntityValue;

public sealed record OnOffValue(OnOff State) : EntityValue;

public sealed record ValveValue(ValveActivity Activity) : EntityValue;
