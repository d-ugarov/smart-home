using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;

namespace SmartHome.Core.State;

public enum StateSource
{
    /// <summary> Reading of the device (periodic polling, a rule, a menu page). </summary>
    Poll,

    /// <summary> The device reported the change itself (e.g. an output switched by its own scenario). </summary>
    Device,

    Rule,
    Menu,
    Automation,
}

/// <summary> The last known value of an entity: since when it holds and who set it. </summary>
public sealed record EntityState(EntityValue Value, DateTimeOffset Since, StateSource Source);

/// <summary>
/// Current state of the house in memory, without history; every change is published as <see cref="EntityStateChanged"/>.
/// Input rules still read the devices before checking conditions - this is for clients and automations.
/// <para>
/// A group follows its switches: on when any of them is on. It changes only when the new state holds for
/// <see cref="GroupSettleTime"/>: a rule that swaps the bright light of a bathroom for the warm one turns the first off
/// a moment before the second goes on, and that is not "the light went out".
/// </para>
/// </summary>
public sealed class HomeState(HomeModel home, HomeEventBus events, TimeProvider timeProvider)
{
    public static readonly TimeSpan GroupSettleTime = TimeSpan.FromSeconds(2);

    private readonly Lock sync = new();
    private readonly Dictionary<string, EntityState> states = [];
    private readonly Dictionary<string, GroupChange> groupChanges = [];

    public EntityState? Get(string entityId)
    {
        lock (sync)
            return states.GetValueOrDefault(entityId);
    }

    public IReadOnlyList<(string EntityId, EntityState State)> All()
    {
        lock (sync)
            return states.Select(x => (x.Key, x.Value)).ToList();
    }

    public void Set(string entityId, EntityValue value, StateSource source)
    {
        lock (sync)
        {
            Apply(entityId, value, source, timeProvider.GetUtcNow());
            UpdateGroups(entityId, source);
        }
    }

    public void SetAll(IReadOnlyDictionary<string, EntityValue> values, StateSource source)
    {
        foreach (var (entityId, value) in values)
            Set(entityId, value, source);
    }

    /// <summary> The state saved before a restart of the server; nothing is published, nothing changed in the house. </summary>
    public void Restore(IEnumerable<(string EntityId, EntityState State)> saved)
    {
        lock (sync)
        {
            foreach (var (entityId, state) in saved)
                states[entityId] = state;
        }
    }

    /// <summary> A device restarted: its outputs are no longer what the server knew. </summary>
    public void Forget(IEnumerable<string> entityIds)
    {
        lock (sync)
        {
            foreach (var entityId in entityIds)
            {
                states.Remove(entityId);
                UpdateGroups(entityId, StateSource.Poll);
            }
        }
    }

    private void Apply(string entityId, EntityValue value, StateSource source, DateTimeOffset since)
    {
        var previous = states.GetValueOrDefault(entityId);
        if (previous?.Value == value)
            return;

        var current = new EntityState(value, since, source);
        states[entityId] = current;
        events.Publish(new EntityStateChanged(entityId, previous, current));
    }

    private void UpdateGroups(string memberId, StateSource source)
    {
        foreach (var group in home.GroupsOf(memberId))
        {
            var value = GroupValue(group);
            var pending = groupChanges.GetValueOrDefault(group.Id);

            if (value == null || value == states.GetValueOrDefault(group.Id)?.Value)
            {
                pending?.Timer.Dispose();
                groupChanges.Remove(group.Id);
                continue;
            }

            if (pending?.Value == value)
                continue;

            pending?.Timer.Dispose();

            var change = new GroupChange(group.Id, value, timeProvider.GetUtcNow(), source);
            groupChanges[group.Id] = change;
            change.Timer = timeProvider.CreateTimer(_ => Commit(change), null, GroupSettleTime, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary> On when any switch is on, off when all are known to be off, unknown otherwise. </summary>
    private OnOffValue? GroupValue(Entity group)
    {
        var members = group.Members.Select(x => states.GetValueOrDefault(x.Id)?.Value as OnOffValue).ToList();

        if (members.Any(x => x?.State == OnOff.On))
            return new OnOffValue(OnOff.On);

        return members.All(x => x?.State == OnOff.Off) ? new OnOffValue(OnOff.Off) : null;
    }

    private void Commit(GroupChange change)
    {
        lock (sync)
        {
            if (groupChanges.GetValueOrDefault(change.GroupId) != change)
                return;

            groupChanges.Remove(change.GroupId);
            change.Timer.Dispose();

            // Since the switch changed, not since the change settled: durations of the group are those of its light.
            Apply(change.GroupId, change.Value, change.Source, change.Since);
        }
    }

    private sealed class GroupChange(string groupId, OnOffValue value, DateTimeOffset since, StateSource source)
    {
        public string GroupId { get; } = groupId;
        public OnOffValue Value { get; } = value;
        public DateTimeOffset Since { get; } = since;
        public StateSource Source { get; } = source;
        public ITimer Timer { get; set; } = null!;
    }
}
