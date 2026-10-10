using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace SmartHome.Persistence;

/// <summary>
/// A copy of the database once a day (the online backup of SQLite: consistent while the server writes), the last
/// <see cref="StorageOptions.BackupCount"/> are kept. Checked at startup and every hour, so a server that restarts often
/// still makes one copy a day.
/// </summary>
internal sealed class DatabaseBackupService(DatabaseLocation location, DatabaseGate gate, IOptions<StorageOptions> options, TimeProvider timeProvider,
                                            ILogger<DatabaseBackupService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private const string Prefix = "home-";
    private const string Extension = ".db";
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);

        do
        {
            try
            {
                BackupIfDue();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Backup of the database failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal void BackupIfDue()
    {
        var now = timeProvider.GetUtcNow();
        var backups = Backups();

        if (backups.Count > 0 && now - backups[^1].Time < Interval)
            return;

        Directory.CreateDirectory(location.BackupDirectory);
        var file = Path.Combine(location.BackupDirectory, Prefix + now.ToString(TimeFormat, CultureInfo.InvariantCulture) + Extension);
        var temporary = file + ".tmp";

        using (gate.Enter())
        using (var source = new SqliteConnection(location.ConnectionString))
        using (var target = new SqliteConnection(DatabaseLocation.ConnectionStringOf(temporary)))
        {
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }

        File.Move(temporary, file, overwrite: true);
        logger.LogInformation("Database backed up to {File}", file);

        foreach (var old in Backups().SkipLast(options.Value.BackupCount))
        {
            File.Delete(old.File);
            logger.LogInformation("Old backup {File} deleted", old.File);
        }
    }

    /// <summary> Oldest first. </summary>
    private List<(string File, DateTimeOffset Time)> Backups()
    {
        if (!Directory.Exists(location.BackupDirectory))
            return [];

        return Directory.GetFiles(location.BackupDirectory, Prefix + "*" + Extension)
                        .Select(x => (File: x, Name: Path.GetFileNameWithoutExtension(x)[Prefix.Length..]))
                        .Select(x => DateTimeOffset.TryParseExact(x.Name, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
                                         ? (x.File, Time: time)
                                         : (x.File, Time: DateTimeOffset.MinValue))
                        .Where(x => x.Time != DateTimeOffset.MinValue)
                        .OrderBy(x => x.Time)
                        .ToList();
    }
}
