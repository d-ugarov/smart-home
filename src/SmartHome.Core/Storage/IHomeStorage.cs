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

    /// <summary> A value set from the clients, e.g. the vacation; null when not set. </summary>
    Task<string?> LoadSettingAsync(string key, CancellationToken cancellationToken);

    /// <summary> Null removes the value. </summary>
    Task SaveSettingAsync(string key, string? value, CancellationToken cancellationToken);
}

/// <summary> State of a switch or a group: one record per entity, without history. </summary>
public sealed record StoredState(string EntityId, OnOff State, DateTimeOffset Since, StateSource Source);

/// <summary>
/// A command for <see cref="EntityId"/>; with <see cref="RuleIndex"/> - the check of a held trigger, or with
/// <see cref="RunTime"/> the next run of a schedule.
/// </summary>
public sealed record StoredDelayedAction(string Key, DateTimeOffset Due, string AutomationId, string? EntityId, OnOff? State, int? RuleIndex,
                                         DateTimeOffset? RunTime = null, bool Skip = false);
