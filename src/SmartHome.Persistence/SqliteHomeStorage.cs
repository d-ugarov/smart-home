using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartHome.Core.Storage;

namespace SmartHome.Persistence;

internal sealed class SqliteHomeStorage(IDbContextFactory<HomeDbContext> contexts, DatabaseGate gate, DatabaseLocation location, ILogger<SqliteHomeStorage> logger)
    : IHomeStorage
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(location.DatabaseFile)!);

        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var migrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database {File}: {Migrations}", location.DatabaseFile,
                              migrations.Count == 0 ? "up to date" : $"migrated ({string.Join(", ", migrations)})");
    }

    public async Task<IReadOnlyList<StoredState>> LoadStatesAsync(CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.EntityStates.AsNoTracking()
                            .Select(x => new StoredState(x.EntityId, x.State, x.Since, x.Source))
                            .ToListAsync(cancellationToken);
    }

    public async Task SaveStatesAsync(IReadOnlyCollection<StoredState> states, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        foreach (var state in states)
        {
            var row = await context.EntityStates.FindAsync([state.EntityId], cancellationToken);
            if (row == null)
                context.EntityStates.Add(row = new EntityStateRow {EntityId = state.EntityId});

            row.State = state.State;
            row.Since = state.Since;
            row.Source = state.Source;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StoredDelayedAction>> LoadDelayedActionsAsync(CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.DelayedActions.AsNoTracking()
                            .Select(x => new StoredDelayedAction(x.Key, x.Due, x.AutomationId, x.EntityId, x.State, x.RuleIndex, x.RunTime, x.Skip))
                            .ToListAsync(cancellationToken);
    }

    public async Task SaveDelayedActionAsync(StoredDelayedAction action, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        var row = await context.DelayedActions.FindAsync([action.Key], cancellationToken);
        if (row == null)
            context.DelayedActions.Add(row = new DelayedActionRow {Key = action.Key});

        row.Due = action.Due;
        row.AutomationId = action.AutomationId;
        row.EntityId = action.EntityId;
        row.State = action.State;
        row.RuleIndex = action.RuleIndex;
        row.RunTime = action.RunTime;
        row.Skip = action.Skip;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteDelayedActionAsync(string key, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await context.DelayedActions.Where(x => x.Key == key).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, bool>> LoadAutomationSwitchesAsync(CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.AutomationSwitches.AsNoTracking().ToDictionaryAsync(x => x.AutomationId, x => x.IsOn, cancellationToken);
    }

    public async Task SaveAutomationSwitchAsync(string automationId, bool isOn, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        var row = await context.AutomationSwitches.FindAsync([automationId], cancellationToken);
        if (row == null)
            context.AutomationSwitches.Add(row = new AutomationSwitchRow {AutomationId = automationId});

        row.IsOn = isOn;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> LoadSettingAsync(string key, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        return await context.Settings.AsNoTracking().Where(x => x.Key == key).Select(x => x.Value).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveSettingAsync(string key, string? value, CancellationToken cancellationToken)
    {
        using var _ = await gate.EnterAsync(cancellationToken);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        var row = await context.Settings.FindAsync([key], cancellationToken);
        if (value == null)
        {
            if (row != null)
                context.Settings.Remove(row);
        }
        else
        {
            if (row == null)
                context.Settings.Add(row = new SettingRow {Key = key});
            row.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
