using SmartHome.Core.Common;
using SmartHome.Core.Entities;
using SmartHome.Core.State;

namespace SmartHome.Core.Events;

/// <summary> Something that happened in the house. Published after the fact and handled in the background. </summary>
public abstract record HomeEvent;

/// <summary> A request of a device as the integration understood it; <paramref name="Text"/> is for people. </summary>
public sealed record DeviceEventReceived(string Device, string Text) : HomeEvent;

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
