using SmartHome.Core.Common;
using SmartHome.Core.Entities;
using SmartHome.Core.State;

namespace SmartHome.Core.Events;

/// <summary> Something that happened in the house. Published after the fact and handled in the background. </summary>
public abstract record HomeEvent;

/// <summary> A request of a device as the integration understood it; <paramref name="Text"/> is for people. </summary>
/// <param name="Entity"> Bound to the port of the request; null - a port without a binding, a controller itself. </param>
public sealed record DeviceEventReceived(string Device, string Text, DeviceEventKind Kind, Entity? Entity = null) : HomeEvent;

/// <summary> What the request is about; only the integration knows the wiring. </summary>
public enum DeviceEventKind
{
    /// <summary> Buttons, switches, ports without a binding. </summary>
    Regular,

    /// <summary> The leak protection: leak sensors, inputs and relays of the valves. </summary>
    LeakProtection,

    /// <summary> A device the server does not know. </summary>
    Unknown,

    /// <summary> A pulse of a meter. </summary>
    Meter,
}

/// <param name="DeviceId"> Unique among integrations, e.g. <c>megad:1</c>. </param>
public sealed record DeviceRestarted(string DeviceId, string Device) : HomeEvent;

public sealed record RuleCommandExecuted(Entity Button, Entity Target, SwitchCommand Command, CommandOutcome Outcome) : HomeEvent;

public sealed record ValveCommandExecuted(Entity Valve, ValveCommand Command, CommandOutcome Outcome) : HomeEvent;

/// <param name="Previous"> Null - the first known state since the start of the server or a restart of the device. </param>
public sealed record EntityStateChanged(string EntityId, EntityState? Previous, EntityState Current) : HomeEvent;

/// <param name="Origin"> Name of the automation, or the auto-off. </param>
public sealed record AutomationCommandExecuted(string Origin, Entity Target, OnOff Command, CommandOutcome Outcome) : HomeEvent;

public sealed record CleaningStarted(Entity Valve, DateTimeOffset Until) : HomeEvent;

/// <summary> The leak sensor fired while the bathroom is being cleaned: the valves were not closed. </summary>
public sealed record LeakHeldBack(Entity Valve, Entity Sensor) : HomeEvent;

public sealed record CleaningEnded(Entity Valve, CleaningEnd End) : HomeEvent;

public enum CleaningEnd
{
    SensorDry,

    /// <summary> The sensor reports only when it gets wet: the valves are closed at the end. </summary>
    SensorWet,

    SensorNotRead,
}

/// <param name="Previous"> Null - the first reading of the meter. </param>
public sealed record MeterReadingEntered(Entity Meter, decimal Reading, decimal? Previous) : HomeEvent;

/// <summary>
/// The counter of a meter went back without a restart of its device the server knows of: counted as a restart, the pulses
/// between the last count seen and the restart are lost.
/// </summary>
public sealed record MeterCounterReset(Entity Meter, int From, int To) : HomeEvent;

/// <summary> Something the owner has to look at: the server works, but not fully. </summary>
public sealed record ServerProblem(string Text) : HomeEvent;

/// <summary> A scheduled action was not done: the server was down past <see cref="Automations.AutomationSettings.MaxLateness"/>. </summary>
public sealed record DelayedActionDropped(string Origin, string Action, TimeSpan Late) : HomeEvent;

public abstract record CommandOutcome
{
    public sealed record Sent(OperationResult Result) : CommandOutcome;

    /// <summary> On/Off for a switch that is already in that state. </summary>
    public sealed record Skipped(OnOff State) : CommandOutcome;

    /// <summary> A valve pulse is already running. </summary>
    public sealed record Busy : CommandOutcome;

    /// <summary> A schedule did not send the command, as the owner asked from the menu. </summary>
    public sealed record Withheld(WithheldReason Reason) : CommandOutcome;
}

public enum WithheldReason
{
    /// <summary> Schedules turn nothing on during a vacation; turning off goes on. </summary>
    Vacation,

    SkippedOnce,
}
