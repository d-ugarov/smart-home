using Microsoft.Extensions.Options;
using System.Net;
using System.Web;

namespace SmartHome.Integrations.MegaD;

/// <summary>
/// Controllers in memory for local runs (<see cref="MegaDOptions.UseSimulator"/>): every port starts off with its counter at
/// zero, as after a start of a controller; relays follow the commands, scenarios of inputs do nothing. Nothing leaves the
/// machine.
/// </summary>
internal sealed class MegaDSimulator(IOptions<MegaDOptions> options) : HttpMessageHandler
{
    private const int PortCount = MegaDOptionsValidator.MaxPort + 1;

    private readonly Lock sync = new();
    private readonly Dictionary<string, string[]> controllers = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var query = HttpUtility.ParseQueryString(uri.Query);

        lock (sync)
        {
            var ports = PortsOf(uri);
            var answer = (query["cmd"], query["pt"]) switch
            {
                ("all", null) => string.Join(";", ports),
                ("d", not null) => "Done",
                ({ } cmd, null) when cmd.Split(':') is [var p, var v] && int.TryParse(p, out var port) && port < PortCount => Set(ports, port, v),
                _ => null,
            };

            return Task.FromResult(answer == null
                                       ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                                       : new HttpResponseMessage(HttpStatusCode.OK) {Content = new StringContent(answer)});
        }
    }

    // HttpClientFactory disposes handler chains when it rotates them; the simulated state must outlive them.
    protected override void Dispose(bool disposing)
    {
    }

    private string[] PortsOf(Uri uri)
    {
        var address = options.Value.Controllers.Values.FirstOrDefault(x => Uri.TryCreate(x.Address, UriKind.Absolute, out var a) && a.Authority == uri.Authority)?.Address
                      ?? uri.Authority;

        if (!controllers.TryGetValue(address, out var ports))
            controllers[address] = ports = Enumerable.Repeat("OFF/0", PortCount).ToArray();

        return ports;
    }

    private static string? Set(string[] ports, int port, string value)
    {
        ports[port] = value switch
        {
            "0" => "OFF",
            "1" => "ON",
            "2" => ports[port] == "ON" ? "OFF" : "ON",
            _ => ports[port],
        };

        return "Done";
    }
}
