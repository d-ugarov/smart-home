using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHome.Core.Automations;
using SmartHome.Core.Common;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.State;
using SmartHome.Core.Valves;
using System.Threading.Channels;

namespace SmartHome.Core.Storage;

/// <summary>
/// Before anything else starts: the storage is created or upgraded, the state saved before the restart is back (so "on
/// since" survives it), automations load what they scheduled and the cleanings of bathrooms are back. After everything has stopped: the state is saved.
/// <para>
/// The house does not depend on the storage: when it cannot be opened (full disk, rights, a broken file) the server
/// works as <c>master</c> did - with the state in memory - and the owner is told.
/// </para>
/// </summary>
internal sealed class HomeStartup(
    IHomeStorage storage,
    HomeModel home,
    HomeState state,
    AutomationEngine automations,
    CleaningService cleaning,
    HomeEventBus events,
    ILogger<HomeStartup> logger) : IHostedLifecycleService
{
    private readonly Lock stopSync = new();
    private Task? saveOnStop;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var result = await InvokeOperations.InvokeAsync(logger, "Open the database", () => RestoreAsync(cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.IsSuccess)
        {
            events.Publish(new ServerProblem($"Database unavailable, the state is kept in memory only: {result.Error}"));
            // Schedules and the auto-off still run: from now on, without what was saved.
            await automations.LoadAsync(fromStorage: false, cancellationToken);
        }
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await storage.InitializeAsync(cancellationToken);

        var saved = (await storage.LoadStatesAsync(cancellationToken))
                    .Where(x => home.FindEntity(x.EntityId)?.Type is EntityType.Switch or EntityType.Group)
                    .Select(x => (x.EntityId, new EntityState(new OnOffValue(x.State), x.Since, x.Source)))
                    .ToList();
        state.Restore(saved);
        logger.LogInformation("State of {Count} entities restored", saved.Count);

        await automations.LoadAsync(fromStorage: true, cancellationToken);
        await cleaning.RestoreAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Changes are saved in the background, and the last of them may not have made it: the whole state is saved once
    /// more. Services stop in reverse order, so polling, events and automations registered after this one have stopped.
    /// The host may be stopped twice at once (the test host is): both wait for the same save.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (stopSync)
            return saveOnStop ??= SaveStateAsync(cancellationToken);
    }

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        var states = state.All()
                          .Where(x => x.State.Value is OnOffValue)
                          .Select(x => new StoredState(x.EntityId, ((OnOffValue)x.State.Value).State, x.State.Since, x.State.Source))
                          .ToList();

        await InvokeOperations.InvokeAsync(logger, "Save the state on stop", () => storage.SaveStatesAsync(states, cancellationToken));
    }
}

/// <summary>
/// Every change of a switch or a group goes to the storage in the background, apart from the event bus: what has piled
/// up is written in one transaction (a "turn everything off" press is one write, not twenty), and a slow disk does not
/// hold up notifications. A started write is not cancelled by the stop.
/// </summary>
internal sealed class StatePersistence(IHomeStorage storage, ILogger<StatePersistence> logger) : BackgroundService, IHomeEventHandler
{
    private readonly Channel<StoredState> queue = Channel.CreateUnbounded<StoredState>(new UnboundedChannelOptions {SingleReader = true});

    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (homeEvent is EntityStateChanged {Current: {Value: OnOffValue value} current} change)
            queue.Writer.TryWrite(new StoredState(change.EntityId, value.State, current.Since, current.Source));

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            var latest = new Dictionary<string, StoredState>();
            while (queue.Reader.TryRead(out var state))
                latest[state.EntityId] = state;

            await InvokeOperations.InvokeAsync(logger, $"Save the state of {latest.Count} entities",
                                               () => storage.SaveStatesAsync(latest.Values, CancellationToken.None));
        }
    }
}
