using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SmartHome.Core.Entities;
using SmartHome.Core.State;

namespace SmartHome.Persistence;

/// <summary> The database of the house. Schema changes go through migrations, applied at startup. </summary>
public sealed class HomeDbContext(DbContextOptions<HomeDbContext> options) : DbContext(options)
{
    public DbSet<EntityStateRow> EntityStates => Set<EntityStateRow>();
    public DbSet<DelayedActionRow> DelayedActions => Set<DelayedActionRow>();
    public DbSet<AutomationSwitchRow> AutomationSwitches => Set<AutomationSwitchRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EntityStateRow>(x =>
        {
            x.HasKey(e => e.EntityId);
            x.Property(e => e.State).HasConversion<string>();
            x.Property(e => e.Source).HasConversion<string>();
        });

        modelBuilder.Entity<DelayedActionRow>(x =>
        {
            x.HasKey(e => e.Key);
            x.Property(e => e.State).HasConversion<string>();
        });

        modelBuilder.Entity<AutomationSwitchRow>(x => x.HasKey(e => e.AutomationId));
    }
}

/// <summary> Current state of a switch or a group: one row per entity, updated in place. </summary>
public sealed class EntityStateRow
{
    public string EntityId { get; set; } = "";
    public OnOff State { get; set; }
    public DateTimeOffset Since { get; set; }
    public StateSource Source { get; set; }
}

/// <summary> A scheduled command of an entity, or the check of a held trigger (<see cref="RuleIndex"/>). </summary>
public sealed class DelayedActionRow
{
    public string Key { get; set; } = "";
    public DateTimeOffset Due { get; set; }
    public string AutomationId { get; set; } = "";
    public string? EntityId { get; set; }
    public OnOff? State { get; set; }
    public int? RuleIndex { get; set; }
}

/// <summary> An automation turned on or off from a client. </summary>
public sealed class AutomationSwitchRow
{
    public string AutomationId { get; set; } = "";
    public bool IsOn { get; set; }
}

/// <summary> For <c>dotnet ef migrations add</c>. </summary>
public sealed class HomeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HomeDbContext>
{
    public HomeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(DatabaseLocation.ConnectionStringOf("design-time.db")).Options);
}
