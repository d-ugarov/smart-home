using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary> The database of the house: created at the first start, copied once a day, old copies removed. </summary>
[NonParallelizable]
public class StorageTests
{
    [Test]
    public async Task Backup_AtStartAndOnceADay_LastOnesKept()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["Storage:BackupCount"] = "2"},
                                                        services => services.AddSingleton<TimeProvider>(time));
        using var client = factory.CreateClient();
        var backups = Path.Combine(factory.DataDirectory, "backup");

        await WaitForFilesAsync(backups, 1);
        var first = Files(backups);
        time.Advance(TimeSpan.FromHours(23));
        await Task.Delay(300);
        var sameDay = Files(backups);
        time.Advance(TimeSpan.FromHours(2));
        await WaitForFilesAsync(backups, 2);
        time.Advance(TimeSpan.FromHours(25));
        await WaitUntilAsync(() => !Files(backups).Contains(first.Single()));
        var kept = Files(backups);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new[] {"home-20261010-120000.db"}));
            Assert.That(sameDay, Is.EqualTo(first));
            // The hourly check falls anywhere within the hour the clock was moved by: only the days are fixed.
            Assert.That(kept.Select(x => x[..13]), Is.EqualTo(new[] {"home-20261011", "home-20261012"}));
            Assert.That(Tables(Path.Combine(backups, kept[^1])), Does.Contain("EntityStates").And.Contain("DelayedActions"), "a working copy of the database");
        });
    }

    [Test]
    public async Task DatabaseUnavailable_HouseStillWorks_OwnerNotified()
    {
        var notDatabase = TempDirectory.Create();
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["Storage:DatabasePath"] = notDatabase});
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/megad/1?pt=0&click=1");
        var notification = await factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage" && x.Body["text"]!.GetValue<string>().StartsWith("⚠️"),
                                                                           description: "a warning");

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200), "the rules work as in master");
            Assert.That(factory.Network.Commands, Is.Not.Empty);
            Assert.That(notification.Body["text"]!.GetValue<string>(), Does.StartWith("⚠️ Database unavailable, the state is kept in memory only"));
        });
        TempDirectory.Delete(notDatabase);
    }

    private static string[] Files(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, "*.db").Select(Path.GetFileName).OfType<string>().Order().ToArray() : [];

    private static List<string> Tables(string file)
    {
        using var connection = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static Task WaitForFilesAsync(string directory, int count) => WaitUntilAsync(() => Files(directory).Length >= count);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(20, timeout.Token);
    }
}
