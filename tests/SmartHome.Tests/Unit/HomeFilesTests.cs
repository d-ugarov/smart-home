using Microsoft.Extensions.Configuration;
using SmartHome.Host;

namespace SmartHome.Tests.Unit;

/// <summary> House files describe the house; settings, secrets, environment variables and the command line override them. </summary>
public class HomeFilesTests
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "smart-home-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "devices.json"), """{ "MegaD": { "Controllers": { "1": { "Address": "http://10.0.0.1", "Password": "from-house-file" } } } }""");
        File.WriteAllText(Path.Combine(directory, "entities.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "automations.json"), "{}");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, recursive: true);

    [Test]
    public void Settings_OverrideHouseFiles()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                                            {
                                                [HomeFiles.DirectoryKey] = directory,
                                                ["MegaD:Controllers:1:Password"] = "from-settings",
                                            });

        configuration.AddHomeFiles();

        Assert.Multiple(() =>
        {
            Assert.That(configuration["MegaD:Controllers:1:Password"], Is.EqualTo("from-settings"));
            Assert.That(configuration["MegaD:Controllers:1:Address"], Is.EqualTo("http://10.0.0.1"));
        });
    }
}
