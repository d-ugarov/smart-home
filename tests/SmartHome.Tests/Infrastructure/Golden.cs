using System.Text;

namespace SmartHome.Tests.Infrastructure;

/// <summary>
/// Golden files: behaviour recorded from <c>master</c> and kept under version control. A test compares the
/// current behaviour with the file; to re-record (only for a deliberate, agreed change) run the tests with
/// <c>UPDATE_GOLDEN=1</c>.
/// </summary>
public static class Golden
{
    public static void AssertMatches(House house, string name, IReadOnlyList<string> actual)
    {
        var path = Path.Combine(house.GoldenDirectory, name);

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(house.GoldenDirectory);
            File.WriteAllLines(path, actual, new UTF8Encoding(false));
            Assert.Inconclusive($"Golden file recorded: {path}. Review it and run the tests again.");
        }

        var expected = File.ReadAllLines(path);
        Assert.That(actual, Is.EqualTo(expected), $"Behaviour differs from {path}");
    }
}
