using System.Net;
using System.Web;

namespace SmartHome.Tests.Fakes;

public sealed record MegaDCommand(string DeviceId, int Port, string Value)
{
    public override string ToString() => $"{DeviceId}/{Port}:{Value}";
}

/// <summary>
/// MegaD-2561 as seen over HTTP: <c>?cmd=all</c>, <c>?cmd=P:V</c>, <c>?pt=P&amp;cmd=get</c>, <c>?pt=P&amp;cmd=d</c>.
/// Port values are kept in the controller's own format: <c>ON</c>, <c>OFF</c>, <c>OFF/12</c>.
/// </summary>
public sealed class FakeMegaD
{
    private readonly Lock sync = new();
    private readonly string[] snapshot;
    private readonly Action<MegaDCommand> onCommand;
    private string[] ports;

    public FakeMegaD(string deviceId, string host, string password, string cmdAllSnapshot, Action<MegaDCommand> onCommand)
    {
        DeviceId = deviceId;
        Host = host;
        Password = password;
        snapshot = cmdAllSnapshot.Trim().Split(';');
        ports = snapshot.ToArray();
        this.onCommand = onCommand;
    }

    public string DeviceId { get; }
    public string Host { get; }
    public string Password { get; }

    /// <summary> Controller does not answer: requests fail as if the network is down. </summary>
    public bool IsOffline { get; set; }

    public int StatusRequests { get; private set; }

    public void Reset()
    {
        lock (sync)
        {
            ports = snapshot.ToArray();
            IsOffline = false;
            StatusRequests = 0;
        }
    }

    public bool IsOn(int port)
    {
        lock (sync)
            return ports[port].StartsWith("ON", StringComparison.Ordinal);
    }

    public void Set(int port, bool isOn)
    {
        lock (sync)
        {
            var counter = ports[port].Split('/') is [_, var c] ? $"/{c}" : "";
            ports[port] = (isOn ? "ON" : "OFF") + counter;
        }
    }

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        if (IsOffline)
            throw new HttpRequestException($"MegaD {DeviceId} is offline");

        var uri = request.RequestUri!;
        if (uri.AbsolutePath.Trim('/') != Password)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var query = HttpUtility.ParseQueryString(uri.Query);
        var cmd = query["cmd"];
        var pt = query["pt"];

        lock (sync)
        {
            switch (cmd)
            {
                case "all":
                    StatusRequests++;
                    return Text(string.Join(";", ports));
                case "get" when int.TryParse(pt, out var port):
                    return Text(ports[port]);
                case "d" when int.TryParse(pt, out var input):
                    // Runs the scenario of the input; its effect on the relays is up to the test.
                    onCommand(new MegaDCommand(DeviceId, input, "d"));
                    return Text("Done");
                case not null when pt is null && cmd.Split(':') is [var p, var v] && int.TryParse(p, out var port):
                    ApplyCommand(port, v);
                    return Text("Done");
                default:
                    throw new NotSupportedException($"FakeMegaD {DeviceId}: unsupported request {uri.Query}");
            }
        }
    }

    private void ApplyCommand(int port, string value)
    {
        onCommand(new MegaDCommand(DeviceId, port, value));

        var isOn = ports[port].StartsWith("ON", StringComparison.Ordinal);
        switch (value)
        {
            case "0": Set(port, false); break;
            case "1": Set(port, true); break;
            case "2": Set(port, !isOn); break;
            default: throw new NotSupportedException($"FakeMegaD {DeviceId}: unsupported command value {port}:{value}");
        }
    }

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK) {Content = new StringContent(text)};
}
