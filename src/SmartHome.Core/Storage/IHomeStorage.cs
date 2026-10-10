using SmartHome.Core.Entities;
using SmartHome.Core.State;

namespace SmartHome.Core.Storage;

/// <summary> What outlives a restart of the server; implemented by the database module. Methods throw on failures. </summary>
public interface IHomeStorage
{
    /// <summary> Creates or upgrades the storage; runs once at startup, before anything else. </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredState>> LoadStatesAsync(CancellationToken cancellationToken);

    /// <summary> Adds or replaces the states of these entities, in one transaction. </summary>
    Task SaveStatesAsync(IReadOnlyCollection<StoredState> states, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredDelayedAction>> LoadDelayedActionsAsync(CancellationToken cancellationToken);

    /// <summary> Adds or replaces the action with the same key. </summary>
    Task SaveDelayedActionAsync(StoredDelayedAction action, CancellationToken cancellationToken);

    Task DeleteDelayedActionAsync(string key, CancellationToken cancellationToken);

    /// <summary> Automations turned on or off from the clients; an automation without a record is on. </summary>
    Task<IReadOnlyDictionary<string, bool>> LoadAutomationSwitchesAsync(CancellationToken cancellationToken);

    Task SaveAutomationSwitchAsync(string automationId, bool isOn, CancellationToken cancellationToken);
}

/// <summary> State of a switch or a group: one record per entity, without history. </summary>
public sealed record StoredState(string EntityId, OnOff State, DateTimeOffset Since, StateSource Source);

/// <summary> A command for <see cref="EntityId"/> or, with <see cref="RuleIndex"/>, the check of a held trigger. </summary>
public sealed record StoredDelayedAction(string Key, DateTimeOffset Due, string AutomationId, string? EntityId, OnOff? State, int? RuleIndex);
