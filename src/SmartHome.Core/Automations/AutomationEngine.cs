using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.State;
using SmartHome.Core.Storage;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SmartHome.Core.Automations;

/// <summary>
/// Runs the automations and the auto-off of forgotten switches. Everything it does goes one by one in its own loop:
/// changes of the state, scheduled actions as they fall due, switching automations on and off - so a cancel never
/// overtakes the schedule it cancels.
/// <list type="bullet">
/// <item>Triggers fire on changes of the state in memory (<see cref="HomeState"/>); the first state seen after a start
/// is not a change. A trigger with <c>For</c> schedules a check of its rule and is cancelled when the state goes back.</item>
/// <item>Conditions are checked on the state in memory; before a command the switch is read from the device, and a
/// command to the state it is already in is skipped.</item>
/// <item>A person wins: a switch changed by anyone but an automation loses everything automations scheduled for it -
/// its scheduled commands and the checks of rules that command it.</item>
/// <item>Scheduled actions are saved and survive restarts; one late by more than
/// <see cref="AutomationSettings.MaxLateness"/> is dropped.</item>
/// </list>
/// </summary>
public sealed class AutomationEngine(
    HomeModel home,
    HomeState state,
    IDeviceGateway gateway,
    IHomeStorage storage,
    HomeEventBus events,
    TimeProvider timeProvider,
    IOptions<AutomationSettings> settings,
    ILogger<AutomationEngine> logger) : BackgroundService, IHomeEventHandler
{
    public const string AutoOffName = "Auto-off";

    private static readonly TimeSpan maxWait = TimeSpan.FromDays(1);

    private readonly Channel<Work> work = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions {SingleReader = true});
    private readonly Lock sync = new();
    private readonly Dictionary<string, DelayedAction> pending = [];
    private readonly ConcurrentDictionary<string, bool> switches = new();

    /// <summary> <see cref="AutomationSettings.Enabled"/>: when off, nothing here runs and the menu has no automations. </summary>
    public bool IsEnabled => settings.Value.Enabled;

    /// <summary> Scheduled actions, the nearest first. </summary>
    public IReadOnlyList<DelayedAction> Pending
    {
        get
        {
            lock (sync)
                return pending.Values.OrderBy(x => x.Due).ToList();
        }
    }

    /// <summary> An automation (or <see cref="Automation.AutoOffId"/>) turned on; on unless turned off from a client. </summary>
    public bool IsOn(string automationId) => switches.GetValueOrDefault(automationId, true);

    public string OriginName(string automationId) =>
        automationId == Automation.AutoOffId ? AutoOffName : home.FindAutomation(automationId)?.Name ?? automationId;

    /// <summary> What the action will do, for people. </summary>
    public static string Describe(DelayedAction action) => action.Work switch
    {
        DelayedWork.Command x => $"{x.Target.Name}: {Texts.Of(x.State)}",
        DelayedWork.Check x => x.Rule.Actions.OfType<AutomationAction.Command>().ToList() is {Count: > 0} commands
            ? $"{string.Join(", ", commands.Select(c => $"{c.Target.Name}: {Texts.Of(c.State)}"))} (if still so)"
            : $"rule #{x.Rule.Index + 1} (if still so)",
        _ => action.Key,
    };

    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (IsEnabled && homeEvent is EntityStateChanged change)
            work.Writer.TryWrite(new Work.StateChanged(change));

        return Task.CompletedTask;
    }

    /// <summary> Turns an automation (or the auto-off) on or off; returns when done. </summary>
    public async Task SwitchAsync(string automationId, bool isOn, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Writer.TryWrite(new Work.Switch(automationId, isOn, done));
        await done.Task.WaitAsync(cancellationToken);
    }

    /// <summary> At startup, after the saved state is restored and before the loop starts. </summary>
    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            logger.LogInformation("Automations are off ({Section}:{Key})", AutomationSettings.Section, nameof(AutomationSettings.Enabled));
            return;
        }

        foreach (var (automationId, isOn) in await storage.LoadAutomationSwitchesAsync(cancellationToken))
            switches[automationId] = isOn;

        foreach (var stored in await storage.LoadDelayedActionsAsync(cancellationToken))
        {
            if (Restore(stored) is { } action)
            {
                lock (sync)
                    pending[action.Key] = action;
                continue;
            }

            logger.LogWarning("Scheduled action {Key} dropped: its automation or switch is no longer in the house config", stored.Key);
            await storage.DeleteDelayedActionAsync(stored.Key, cancellationToken);
        }

        // Switches left on before the restart (or before automations were turned on) get their auto-off.
        if (IsOn(Automation.AutoOffId))
            await ArmAutoOffAsync();

        logger.LogInformation("Automations are on, {Count} actions scheduled", Pending.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
            return;

        while (true)
        {
            await RunDueAsync(stoppingToken);

            if (work.Reader.TryRead(out var next))
                await ProcessAsync(next, stoppingToken);
            else
                await WaitAsync(stoppingToken);
        }
    }

    private async Task WaitAsync(CancellationToken cancellationToken)
    {
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var waits = new List<Task> {work.Reader.WaitToReadAsync(wake.Token).AsTask()};

        if (NextDue() is { } due)
            waits.Add(Task.Delay(Clamp(due - timeProvider.GetUtcNow()), timeProvider, wake.Token));

        await Task.WhenAny(waits);
        await wake.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task ProcessAsync(Work item, CancellationToken cancellationToken)
    {
        try
        {
            switch (item)
            {
                case Work.StateChanged x:
                    await OnStateChangedAsync(x.Change, cancellationToken);
                    break;
                case Work.Switch x:
                    await SwitchCoreAsync(x.AutomationId, x.IsOn);
                    break;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(e, "Automations failed on {Work}", item);
        }
        finally
        {
            (item as Work.Switch)?.Done.TrySetResult();
        }
    }

    private async Task OnStateChangedAsync(EntityStateChanged change, CancellationToken cancellationToken)
    {
        if (home.FindEntity(change.EntityId) is not { } entity || change.Current.Value is not OnOffValue current)
            return;

        if (entity.Type == EntityType.Switch && change.Previous != null && change.Current.Source != StateSource.Automation)
            await CancelAsync(x => x.Targets(entity));

        if (entity.AutoOffAfter is { } limit && IsOn(Automation.AutoOffId))
        {
            if (current.State == OnOff.On)
                await ScheduleAsync(AutoOff(entity, change.Current.Since + limit));
            else
                await CancelAsync(DelayedAction.AutoOffKey(entity));
        }

        foreach (var rule in home.RulesTriggeredBy(entity.Id).Where(x => IsOn(x.Automation.Id)))
        {
            if (current.State != rule.Trigger.To)
            {
                if (rule.Trigger.For != null)
                    await CancelAsync(rule.HeldKey);
                continue;
            }

            // Nothing is known about how the first state seen after a start came about.
            if (change.Previous == null)
                continue;

            if (rule.Trigger.For is { } hold)
                await ScheduleAsync(new DelayedAction(rule.HeldKey, change.Current.Since + hold, rule.Automation.Id, new DelayedWork.Check(rule)));
            else if (ConditionsHold(rule, change))
                await RunActionsAsync(rule, change.Current.Since, cancellationToken);
        }
    }

    private async Task RunDueAsync(CancellationToken cancellationToken)
    {
        while (TakeDue() is { } action)
        {
            await DeleteSavedAsync(action.Key);

            var late = timeProvider.GetUtcNow() - action.Due;
            if (late > settings.Value.MaxLateness)
            {
                logger.LogWarning("Scheduled action {Key} dropped: late by {Late}", action.Key, late);
                events.Publish(new DelayedActionDropped(OriginName(action.AutomationId), Describe(action), late));
                continue;
            }

            try
            {
                await RunAsync(action, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(e, "Scheduled action {Key} failed", action.Key);
            }
        }
    }

    private async Task RunAsync(DelayedAction action, CancellationToken cancellationToken)
    {
        switch (action.Work)
        {
            case DelayedWork.Command x:
                await SendCommandAsync(OriginName(action.AutomationId), x.Target, x.State, cancellationToken);
                break;

            case DelayedWork.Check x when IsOn(x.Rule.Automation.Id) && Holds(x.Rule.Trigger):
                if (ConditionsHold(x.Rule, change: null))
                    await RunActionsAsync(x.Rule, action.Due, cancellationToken);
                else
                    logger.LogInformation("Automation {Rule}: conditions do not hold", x.Rule);
                break;
        }
    }

    /// <param name="at"> When the trigger fired: scheduled delays count from it. </param>
    private async Task RunActionsAsync(AutomationRule rule, DateTimeOffset at, CancellationToken cancellationToken)
    {
        logger.LogInformation("Automation {Rule} fired", rule);

        foreach (var action in rule.Actions)
        {
            switch (action)
            {
                case AutomationAction.Command x:
                    await SendCommandAsync(rule.Automation.Name, x.Target, x.State, cancellationToken);
                    break;
                case AutomationAction.Schedule x:
                    await ScheduleAsync(new DelayedAction(x.Key, at + x.Delay, rule.Automation.Id, new DelayedWork.Command(x.Target, x.State)));
                    break;
                case AutomationAction.Cancel x:
                    await CancelAsync(x.Key);
                    break;
            }
        }
    }

    private async Task SendCommandAsync(string origin, Entity target, OnOff desired, CancellationToken cancellationToken)
    {
        var read = await gateway.ReadAsync([target], cancellationToken);
        state.SetAll(read, StateSource.Poll);

        CommandOutcome outcome;
        if (read.GetValueOrDefault(target.Id) is OnOffValue known && known.State == desired)
        {
            outcome = new CommandOutcome.Skipped(known.State);
        }
        else
        {
            var result = await gateway.SwitchAsync(target, desired == OnOff.On ? SwitchCommand.On : SwitchCommand.Off, cancellationToken);
            if (result.IsSuccess)
                state.Set(target.Id, new OnOffValue(desired), StateSource.Automation);
            outcome = new CommandOutcome.Sent(result);
        }

        logger.LogInformation("{Origin}: {Command} {Target}: {Outcome}", origin, desired, target.Id, outcome);
        events.Publish(new AutomationCommandExecuted(origin, target, desired, outcome));
    }

    private async Task SwitchCoreAsync(string automationId, bool isOn)
    {
        switches[automationId] = isOn;
        logger.LogInformation("Automation {Automation} turned {State}", automationId, isOn ? "on" : "off");
        await InvokeOperations.InvokeAsync(logger, $"Save switch of {automationId}", () => storage.SaveAutomationSwitchAsync(automationId, isOn, CancellationToken.None));

        if (!isOn)
        {
            await CancelAsync(x => x.AutomationId == automationId);
            return;
        }

        if (automationId == Automation.AutoOffId)
        {
            await ArmAutoOffAsync();
            return;
        }

        // A trigger that already holds starts counting from when it came.
        foreach (var rule in home.FindAutomation(automationId)?.Rules ?? [])
        {
            if (rule.Trigger.For is { } hold && Holds(rule.Trigger) && state.Get(rule.Trigger.Entity.Id) is { } since)
                await ScheduleAsync(new DelayedAction(rule.HeldKey, since.Since + hold, automationId, new DelayedWork.Check(rule)));
        }
    }

    private async Task ArmAutoOffAsync()
    {
        foreach (var entity in home.AutoOffSwitches)
        {
            if (state.Get(entity.Id) is {Value: OnOffValue {State: OnOff.On}} on)
                await ScheduleAsync(AutoOff(entity, on.Since + entity.AutoOffAfter!.Value));
        }
    }

    private bool Holds(AutomationTrigger trigger) => state.Get(trigger.Entity.Id)?.Value is OnOffValue value && value.State == trigger.To;

    private bool ConditionsHold(AutomationRule rule, EntityStateChanged? change)
    {
        var previous = change?.Previous is { } p ? change.Current.Since - p.Since : (TimeSpan?)null;

        return rule.Conditions.All(x => x switch
        {
            AutomationCondition.StateIs c => state.Get(c.Entity.Id)?.Value is OnOffValue value && value.State == c.State,
            AutomationCondition.PreviousStateLongerThan c => previous > c.Duration,
            AutomationCondition.PreviousStateNotLongerThan c => previous <= c.Duration,
            _ => false,
        });
    }

    private async Task ScheduleAsync(DelayedAction action)
    {
        lock (sync)
            pending[action.Key] = action;

        logger.LogInformation("Scheduled {Key} at {Due:u}: {Action}", action.Key, action.Due, Describe(action));
        await InvokeOperations.InvokeAsync(logger, $"Save scheduled {action.Key}", () => storage.SaveDelayedActionAsync(ToStored(action), CancellationToken.None));
    }

    private async Task CancelAsync(string key)
    {
        bool removed;
        lock (sync)
            removed = pending.Remove(key);

        if (!removed)
            return;

        logger.LogInformation("Scheduled {Key} cancelled", key);
        await DeleteSavedAsync(key);
    }

    private async Task CancelAsync(Func<DelayedAction, bool> predicate)
    {
        List<string> keys;
        lock (sync)
            keys = pending.Values.Where(predicate).Select(x => x.Key).ToList();

        foreach (var key in keys)
            await CancelAsync(key);
    }

    // Writes are not cancelled by the stop of the server: the stop waits for them, so a schedule made just before a
    // restart is not lost.
    private Task DeleteSavedAsync(string key) =>
        InvokeOperations.InvokeAsync(logger, $"Delete scheduled {key}", () => storage.DeleteDelayedActionAsync(key, CancellationToken.None));

    private DelayedAction? TakeDue()
    {
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            var due = pending.Values.Where(x => x.Due <= now).MinBy(x => x.Due);
            if (due != null)
                pending.Remove(due.Key);
            return due;
        }
    }

    private DateTimeOffset? NextDue()
    {
        lock (sync)
            return pending.Count > 0 ? pending.Values.Min(x => x.Due) : null;
    }

    private static TimeSpan Clamp(TimeSpan delay) => delay < TimeSpan.Zero ? TimeSpan.Zero : delay > maxWait ? maxWait : delay;

    private static DelayedAction AutoOff(Entity entity, DateTimeOffset due) =>
        new(DelayedAction.AutoOffKey(entity), due, Automation.AutoOffId, new DelayedWork.Command(entity, OnOff.Off));

    private static StoredDelayedAction ToStored(DelayedAction action) => action.Work switch
    {
        DelayedWork.Command x => new StoredDelayedAction(action.Key, action.Due, action.AutomationId, x.Target.Id, x.State, null),
        DelayedWork.Check x => new StoredDelayedAction(action.Key, action.Due, action.AutomationId, null, null, x.Rule.Index),
        _ => throw new NotSupportedException(action.Work.GetType().Name),
    };

    private DelayedAction? Restore(StoredDelayedAction stored)
    {
        if (stored.RuleIndex is { } index)
        {
            return home.FindAutomation(stored.AutomationId)?.Rules.ElementAtOrDefault(index) is {Trigger.For: not null} rule && rule.HeldKey == stored.Key
                ? new DelayedAction(stored.Key, stored.Due, stored.AutomationId, new DelayedWork.Check(rule))
                : null;
        }

        var known = stored.AutomationId == Automation.AutoOffId || home.FindAutomation(stored.AutomationId) != null;

        return known && home.FindEntity(stored.EntityId ?? "") is {Type: EntityType.Switch} target && stored.State is { } command
            ? new DelayedAction(stored.Key, stored.Due, stored.AutomationId, new DelayedWork.Command(target, command))
            : null;
    }

    private abstract record Work
    {
        public sealed record StateChanged(EntityStateChanged Change) : Work;

        public sealed record Switch(string AutomationId, bool IsOn, TaskCompletionSource Done) : Work;
    }
}
