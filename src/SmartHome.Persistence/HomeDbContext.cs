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
    public DbSet<SettingRow> Settings => Set<SettingRow>();
    public DbSet<MeterRow> Meters => Set<MeterRow>();
    public DbSet<MeterDayRow> MeterDays => Set<MeterDayRow>();

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
        modelBuilder.Entity<SettingRow>(x => x.HasKey(e => e.Key));
        modelBuilder.Entity<MeterRow>(x => x.HasKey(e => e.EntityId));
        modelBuilder.Entity<MeterDayRow>(x => x.HasKey(e => new {e.EntityId, e.Date}));
    }
}

/// <summary> The current reading of a meter: one row per meter, updated in place. </summary>
public sealed class MeterRow
{
    public string EntityId { get; set; } = "";

    /// <summary> Null - not entered yet. Stored as text: SQLite has no decimal, a float would drift by the litre. </summary>
    public decimal? Reading { get; set; }

    /// <summary> A pulse meter: the count of the counter of its input the reading includes. </summary>
    public int? Counter { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary> The local day of the last reading entered from a client. </summary>
    public DateOnly? EnteredOn { get; set; }
}

/// <summary> The reading of a meter at the end of a local day of the house; kept forever, a day without a change has no row. </summary>
public sealed class MeterDayRow
{
    public string EntityId { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Reading { get; set; }

    /// <summary> A reading was entered from a client on that day. </summary>
    public bool Entered { get; set; }
}

/// <summary> Current state of a switch or a group: one row per entity, updated in place. </summary>
public sealed class EntityStateRow
{
    public string EntityId { get; set; } = "";
    public OnOff State { get; set; }
    public DateTimeOffset Since { get; set; }
    public StateSource Source { get; set; }
}

/// <summary>
/// A scheduled command of an entity, the check of a held trigger (<see cref="RuleIndex"/>) or the next run of a schedule
/// rule (<see cref="RuleIndex"/> + <see cref="RunTime"/>).
/// </summary>
public sealed class DelayedActionRow
{
    public string Key { get; set; } = "";
    public DateTimeOffset Due { get; set; }
    public string AutomationId { get; set; } = "";
    public string? EntityId { get; set; }
    public OnOff? State { get; set; }
    public int? RuleIndex { get; set; }

    /// <summary> The time of the run by the schedule; <see cref="Due"/> differs when it was moved once. </summary>
    public DateTimeOffset? RunTime { get; set; }

    /// <summary> The run is skipped once. </summary>
    public bool Skip { get; set; }
}

/// <summary> An automation turned on or off from a client. </summary>
public sealed class AutomationSwitchRow
{
    public string AutomationId { get; set; } = "";
    public bool IsOn { get; set; }
}

/// <summary> A value set from the clients, e.g. the vacation of the schedules. </summary>
public sealed class SettingRow
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary> For <c>dotnet ef migrations add</c>. </summary>
public sealed class HomeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HomeDbContext>
{
    public HomeDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HomeDbContext>().UseSqlite(DatabaseLocation.ConnectionStringOf("design-time.db")).Options);
}
