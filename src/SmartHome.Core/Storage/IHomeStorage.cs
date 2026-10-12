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

    Task<IReadOnlyList<StoredMeter>> LoadMetersAsync(CancellationToken cancellationToken);

    /// <summary> Adds or replaces the meters and the readings at the ends of the days, in one transaction. </summary>
    Task SaveMetersAsync(IReadOnlyCollection<StoredMeter> meters, IReadOnlyCollection<StoredMeterDay> days, CancellationToken cancellationToken);

    /// <summary> The readings at the ends of the days from <paramref name="from"/> on and the last one before it, by date. </summary>
    Task<IReadOnlyList<StoredMeterDay>> LoadMeterDaysAsync(string entityId, DateOnly from, CancellationToken cancellationToken);
}

/// <summary> State of a switch or a group: one record per entity, without history. </summary>
public sealed record StoredState(string EntityId, OnOff State, DateTimeOffset Since, StateSource Source);

/// <summary>
/// A command for <see cref="EntityId"/>; with <see cref="RuleIndex"/> - the check of a held trigger, or with
/// <see cref="RunTime"/> the next run of a schedule.
/// </summary>
/// <summary> The current reading of a meter; one record per meter, updated in place. </summary>
/// <param name="Reading"> Null - not entered yet: a pulse meter counts from the first reading entered by the dial. </param>
/// <param name="Counter"> A pulse meter: the count of the counter of its input the reading includes. </param>
/// <param name="EnteredOn"> The local day of the last reading entered from the menu. </param>
public sealed record StoredMeter(string EntityId, decimal? Reading, int? Counter, DateTimeOffset UpdatedAt, DateOnly? EnteredOn);

/// <summary> The reading of a meter at the end of a local day of the house; days without a change have no record. </summary>
/// <param name="Entered"> A reading was entered from the menu on that day: a correction by the dial, or a meter without pulses. </param>
public sealed record StoredMeterDay(string EntityId, DateOnly Date, decimal Reading, bool Entered);

public sealed record StoredDelayedAction(string Key, DateTimeOffset Due, string AutomationId, string? EntityId, OnOff? State, int? RuleIndex,
                                         DateTimeOffset? RunTime = null, bool Skip = false);
