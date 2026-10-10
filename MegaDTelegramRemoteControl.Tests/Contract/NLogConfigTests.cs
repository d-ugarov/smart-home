using NLog;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System.Xml;

namespace MegaDTelegramRemoteControl.Tests.Contract;

/// <summary>
/// nlog.config from the repo: loads on the NLog version the server uses, writes plain text files only,
/// and routes request logs and framework noise as intended.
/// </summary>
[NonParallelizable]
public class NLogConfigTests
{
    private const string RequestLogger = "MegaDTelegramRemoteControl.Infrastructure.Middlewares.RequestLoggingMiddleware";

    private string logDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        logDirectory = Path.Combine(Path.GetTempPath(), "smart-home-tests", Guid.NewGuid().ToString("N"));
        LoadRepoConfig();
    }

    [TearDown]
    public void TearDown()
    {
        LogManager.Configuration = null;
        LogManager.ThrowConfigExceptions = false;
        if (Directory.Exists(logDirectory))
            Directory.Delete(logDirectory, recursive: true);
    }

    [Test]
    public void OnlyFileTargets()
    {
        var targets = LogManager.Configuration!.AllTargets.Where(x => x is not AsyncTargetWrapper).ToList();

        Assert.That(targets, Has.Count.EqualTo(2).And.All.InstanceOf<FileTarget>());
    }

    [Test]
    public void Routing()
    {
        LogManager.GetLogger("MegaDTelegramRemoteControl.Services.HomeTriggerProcessor").Trace("trigger trace");
        LogManager.GetLogger("MegaDTelegramRemoteControl.Services.HomeTriggerProcessor").Error(new InvalidOperationException("boom"), "trigger failed");
        LogManager.GetLogger(RequestLogger).Debug("GET /megad/2?pt=21 | Response: 200");
        LogManager.GetLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware").Info("framework info");
        LogManager.GetLogger("Microsoft.AspNetCore.Server.Kestrel").Warn("framework warning");
        LogManager.GetLogger("Microsoft.Hosting.Lifetime").Info("Application started");
        LogManager.Flush(TimeSpan.FromSeconds(5));

        var app = Read("app-*.log");
        var requests = Read("requests-*.log");

        Assert.Multiple(() =>
        {
            Assert.That(app, Does.Contain("trigger trace"));
            Assert.That(app, Does.Contain("trigger failed").And.Contain("boom"));
            Assert.That(app, Does.Contain("framework warning"));
            Assert.That(app, Does.Contain("Application started"));
            Assert.That(app, Does.Not.Contain("framework info"));
            Assert.That(app, Does.Not.Contain("GET /megad/2"));
            Assert.That(requests, Does.Contain("GET /megad/2?pt=21 | Response: 200"));
            Assert.That(requests, Does.Not.Contain("trigger"));
        });
    }

    private void LoadRepoConfig()
    {
        var xml = new XmlDocument();
        xml.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "repo-nlog.config"));

        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("n", "http://www.nlog-project.org/schemas/NLog.xsd");
        ((XmlElement)xml.SelectSingleNode("//n:variable[@name='logDirectory']", ns)!).SetAttribute("value", logDirectory);
        xml.DocumentElement!.SetAttribute("throwConfigExceptions", "true");

        LogManager.ThrowConfigExceptions = true;
        LogManager.Setup().LoadConfigurationFromXml(xml.OuterXml);
    }

    private string Read(string pattern)
    {
        using var stream = new FileStream(Directory.GetFiles(logDirectory, pattern).Single(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
