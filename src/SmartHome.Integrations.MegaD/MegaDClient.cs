using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace SmartHome.Integrations.MegaD;

/// <summary>
/// HTTP API of MegaD-2561: <c>http://{address}/{password}/?{query}</c>. Requests to one controller go one at a time -
/// it does not like parallel requests. No retries: a repeated toggle would switch the light back.
/// </summary>
internal sealed class MegaDClient(IHttpClientFactory httpClientFactory, IOptions<MegaDOptions> options)
{
    public const string HttpClientName = "megad";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();

    /// <summary> <c>cmd=all</c>: states of all ports, the index is the port number (<c>ON</c>, <c>OFF</c>, <c>OFF/12</c>, ...). </summary>
    public async Task<string[]> ReadAllAsync(MegaDController controller, CancellationToken cancellationToken) =>
        (await SendAsync(controller, "cmd=all", cancellationToken)).Trim().Split(';');

    /// <summary> <c>cmd=P:V</c>: 0 - off, 1 - on, 2 - toggle. </summary>
    public Task SetAsync(MegaDController controller, int port, int value, CancellationToken cancellationToken) =>
        SendAsync(controller, $"cmd={port}:{value}", cancellationToken);

    /// <summary> <c>pt=N&amp;cmd=d</c>: run the scenario of input N as if it fired. </summary>
    public Task RunScenarioAsync(MegaDController controller, int input, CancellationToken cancellationToken) =>
        SendAsync(controller, $"pt={input}&cmd=d", cancellationToken);

    private async Task<string> SendAsync(MegaDController controller, string query, CancellationToken cancellationToken)
    {
        var sync = locks.GetOrAdd(controller.Id, _ => new SemaphoreSlim(1, 1));
        await sync.WaitAsync(cancellationToken);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Value.RequestTimeout);

            var url = $"{controller.Address.ToString().TrimEnd('/')}/{controller.Password}/?{query}";
            using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(url, timeout.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync(timeout.Token);
        }
        finally
        {
            sync.Release();
        }
    }
}
