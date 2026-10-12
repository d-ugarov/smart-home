using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// <c>/megad/{id}</c> answers only the address of controller <c>id</c> (MegaD-1 of the example flat - 192.168.0.11,
/// MegaD-2 - 192.168.0.12): a stranger in the network can neither press a button, nor hold back a leak sensor, nor fill
/// the chat with notifications.
/// </summary>
[NonParallelizable]
public class MegaDSenderTests
{
    private const string HallClick = "/megad/1?pt=0&click=1";

    [TestCase("192.168.0.50", TestName = "A stranger in the network")]
    [TestCase("192.168.0.12", TestName = "Another controller")]
    [TestCase("127.0.0.1", TestName = "This machine, without the simulator")]
    public async Task NotTheController_403_NothingHandled(string sender)
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HallClick, sender);
        await SendAsync(client, "/megad/1?st=1", sender: null);
        await factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage" && x.Body["text"]!.GetValue<string>() == "🔄 MegaD-1 restarted",
                                                        description: "the marker");

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(403), "not 204: that would be \"run your scenario\" for a controller");
            Assert.That(factory.Network.Commands, Is.Empty);
            Assert.That(Notifications(factory), Is.EqualTo(new[] {"🔄 MegaD-1 restarted"}), "nothing reported but the marker");
        });
    }

    [TestCase("192.168.0.11", TestName = "IPv4")]
    [TestCase("::ffff:192.168.0.11", TestName = "IPv4 mapped to IPv6")]
    public async Task TheController_Handled(string sender)
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HallClick, sender);

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200));
            Assert.That(factory.Network.Commands, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ControllerOfTheHouse_WithAWrongId_ReportedAsUnknown()
    {
        await using var factory = new HomeServerFactory(House.Example);
        using var client = factory.CreateClient();

        var ours = await SendAsync(client, "/megad/9?pt=0", "192.168.0.12");
        var stranger = await SendAsync(client, "/megad/9?pt=1", "192.168.0.50");
        await factory.Network.Telegram.WaitForCallAsync(x => x.Method == "sendMessage", description: "the report");

        Assert.Multiple(() =>
        {
            Assert.That((int)ours.StatusCode, Is.EqualTo(204));
            Assert.That((int)stranger.StatusCode, Is.EqualTo(403));
            Assert.That(Notifications(factory), Is.EqualTo(new[] {"megad/9: unknown controller, pt=0"}));
        });
    }

    [Test]
    public async Task Simulator_ThisMachine_Accepted()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?>
                                                                       {
                                                                           ["MegaD:UseSimulator"] = "true",
                                                                           ["Telegram:UseFakeBot"] = "true",
                                                                       });
        using var client = factory.CreateClient();

        var local = await SendAsync(client, HallClick, "127.0.0.1");
        var stranger = await SendAsync(client, HallClick, "192.168.0.50");

        Assert.Multiple(() =>
        {
            Assert.That((int)local.StatusCode, Is.EqualTo(200), "curl of a local run");
            Assert.That((int)stranger.StatusCode, Is.EqualTo(403));
        });
    }

    [Test]
    public async Task Off_AnyoneAccepted_AsMaster()
    {
        await using var factory = new HomeServerFactory(House.Example, new Dictionary<string, string?> {["MegaD:AcceptOnlyControllers"] = "false"});
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HallClick, "192.168.0.50");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string url, string? sender)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (sender != null)
            request.Headers.Add(HomeServerFactory.SenderHeader, sender);
        return client.SendAsync(request);
    }

    private static List<string> Notifications(HomeServerFactory factory) =>
        factory.Network.Telegram.Calls.Where(x => x.Method == "sendMessage" && x.Body["chat_id"]!.GetValue<long>() == HomeServerFactory.NotificationChatId)
               .Select(x => x.Body["text"]!.GetValue<string>())
               .ToList();
}
