namespace MegaDTelegramRemoteControl.Tests.Fakes;

/// <summary>
/// The only way out of the server under test: every outgoing HTTP request lands here and is routed by host
/// to a fake MegaD or to the fake Telegram API. Anything else fails the request.
/// </summary>
public sealed class FakeNetwork : HttpMessageHandler
{
    private readonly Lock sync = new();
    private readonly List<MegaDCommand> commands = [];
    private readonly Dictionary<string, FakeMegaD> devicesByHost;

    public FakeNetwork(IEnumerable<(string DeviceId, string Url, string Password, string CmdAllSnapshot)> devices)
    {
        Devices = devices.Select(x => new FakeMegaD(x.DeviceId, new Uri(x.Url).Host, x.Password, x.CmdAllSnapshot, OnCommand))
                         .ToDictionary(x => x.DeviceId);
        devicesByHost = Devices.Values.ToDictionary(x => x.Host);
    }

    public IReadOnlyDictionary<string, FakeMegaD> Devices { get; }

    public FakeTelegramApi Telegram { get; } = new();

    public List<string> UnexpectedRequests { get; } = [];

    /// <summary> Relay commands sent to all controllers, in the order they were received. </summary>
    public IReadOnlyList<MegaDCommand> Commands
    {
        get
        {
            lock (sync)
                return commands.ToList();
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            commands.Clear();
            UnexpectedRequests.Clear();
        }

        foreach (var device in Devices.Values)
            device.Reset();

        Telegram.ClearCalls();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri!.Host;

        if (host == "api.telegram.org")
            return await Telegram.HandleAsync(request, cancellationToken);

        if (devicesByHost.TryGetValue(host, out var device))
            return device.Handle(request);

        lock (sync)
            UnexpectedRequests.Add(request.RequestUri.ToString());

        throw new HttpRequestException($"Unexpected outgoing request: {request.RequestUri}");
    }

    // HttpClientFactory rotates and disposes handler chains; the fake must outlive them.
    protected override void Dispose(bool disposing)
    {
    }

    private void OnCommand(MegaDCommand command)
    {
        lock (sync)
            commands.Add(command);
    }
}
