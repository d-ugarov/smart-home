using Microsoft.Extensions.Configuration;
using SmartHome.Host;

namespace SmartHome.Tests.Unit;

/// <summary> Without an address in the settings the server listens where the controllers call it, not on localhost:5000. </summary>
public class ListenAddressTests
{
    [Test]
    public void Default_IsThePortOfMaster() => Assert.That(ListenAddress.Default, Is.EqualTo("http://0.0.0.0:5075"));

    // The server's appsettings of master has only KestrelConfig:Port, which the new host does not read.
    [TestCase("KestrelConfig:Port", "5075", false)]
    [TestCase("Kestrel:Endpoints:Http:Url", "http://0.0.0.0:5075", true)]
    [TestCase("urls", "http://+:8080", true)]
    [TestCase("http_ports", "8080", true)]
    public void IsConfigured(string key, string value, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {[key] = value}).Build();

        Assert.That(ListenAddress.IsConfigured(configuration), Is.EqualTo(expected));
    }
}
