using NLog;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System.Xml;

namespace SmartHome.Tests.Contract;

/// <summary>
/// nlog.config from the repo: loads on the NLog version the server uses, writes plain text files only,
/// and routes request logs and framework noise as intended. A log factory of its own: a test server shutting down resets
/// the global one.
/// </summary>
[NonParallelizable]
public class NLogConfigTests
{
    private const string RequestLogger = "SmartHome.Host.RequestLoggingMiddleware";

    private string logDirectory = null!;
    private LogFactory logs = null!;

    [SetUp]
    public void SetUp()
    {
        logDirectory = Path.Combine(Path.GetTempPath(), "smart-home-tests", Guid.NewGuid().ToString("N"));
        LoadRepoConfig();
    }

    [TearDown]
    public void TearDown()
    {
        logs.Dispose();
        if (Directory.Exists(logDirectory))
            Directory.Delete(logDirectory, recursive: true);
    }

    [Test]
    public void OnlyFileTargets()
    {
        var targets = logs.Configuration!.AllTargets.Where(x => x is not AsyncTargetWrapper).ToList();

        Assert.That(targets, Has.Count.EqualTo(2).And.All.InstanceOf<FileTarget>());
    }

    [Test]
    public void Routing()
    {
        logs.GetLogger("SmartHome.Core.Rules.InputRuleEngine").Trace("trigger trace");
        logs.GetLogger("SmartHome.Core.Rules.InputRuleEngine").Error(new InvalidOperationException("boom"), "trigger failed");
        logs.GetLogger(RequestLogger).Debug("GET /megad/2?pt=21 | Response: 200");
        logs.GetLogger("Microsoft.AspNetCore.Routing.EndpointMiddleware").Info("framework info");
        logs.GetLogger("Microsoft.AspNetCore.Server.Kestrel").Warn("framework warning");
        logs.GetLogger("Microsoft.Hosting.Lifetime").Info("Application started");
        logs.Flush(TimeSpan.FromSeconds(5));

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

        logs = new LogFactory {ThrowConfigExceptions = true};
        logs.Setup().LoadConfigurationFromXml(xml.OuterXml);
    }

    private string Read(string pattern)
    {
        using var stream = new FileStream(Directory.GetFiles(logDirectory, pattern).Single(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
