using SmartHome.Core.Entities;
using System.Collections.Concurrent;

namespace SmartHome.Core.State;

public enum StateSource
{
    /// <summary> Reading of the device (periodic polling, a rule, a menu page). </summary>
    Poll,

    /// <summary> The device reported the change itself (e.g. an output switched by its own scenario). </summary>
    Device,

    Rule,
    Menu,
}

/// <summary> The last known value of an entity: since when it holds and who set it. </summary>
public sealed record EntityState(EntityValue Value, DateTimeOffset Since, StateSource Source);

/// <summary>
/// Current state of the house in memory, without history. Input rules still read the devices before checking
/// conditions - this is for clients and automations.
/// </summary>
public sealed class HomeState(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, EntityState> states = new();

    public EntityState? Get(string entityId) => states.GetValueOrDefault(entityId);

    public void Set(string entityId, EntityValue value, StateSource source)
    {
        var now = timeProvider.GetUtcNow();
        states.AddOrUpdate(entityId,
                           _ => new EntityState(value, now, source),
                           (_, current) => current.Value == value ? current : new EntityState(value, now, source));
    }

    public void SetAll(IReadOnlyDictionary<string, EntityValue> values, StateSource source)
    {
        foreach (var (entityId, value) in values)
            Set(entityId, value, source);
    }

    /// <summary> A device restarted: its outputs are no longer what the server knew. </summary>
    public void Forget(IEnumerable<string> entityIds)
    {
        foreach (var entityId in entityIds)
            states.TryRemove(entityId, out _);
    }
}
