using SmartHome.Core.Common;
using SmartHome.Core.Entities;

namespace SmartHome.Core.Events;

/// <summary> Something that happened in the house. Published after the fact and handled in the background. </summary>
public abstract record HomeEvent;

/// <summary> A request of a device as the integration understood it; <paramref name="Text"/> is for people. </summary>
public sealed record DeviceEventReceived(string Device, string Text) : HomeEvent;

/// <param name="DeviceId"> Unique among integrations, e.g. <c>megad:1</c>. </param>
public sealed record DeviceRestarted(string DeviceId, string Device) : HomeEvent;

public sealed record RuleCommandExecuted(Entity Button, Entity Target, SwitchCommand Command, CommandOutcome Outcome) : HomeEvent;

public sealed record ValveCommandExecuted(Entity Valve, ValveCommand Command, CommandOutcome Outcome) : HomeEvent;

public abstract record CommandOutcome
{
    public sealed record Sent(OperationResult Result) : CommandOutcome;

    /// <summary> On/Off for a switch that is already in that state. </summary>
    public sealed record Skipped(OnOff State) : CommandOutcome;

    /// <summary> A valve pulse is already running. </summary>
    public sealed record Busy : CommandOutcome;
}
