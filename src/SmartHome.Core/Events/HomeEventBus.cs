using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace SmartHome.Core.Events;

public interface IHomeEventHandler
{
    Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Events go to the handlers in the background and in order: publishing never delays the answer to a device.
/// </summary>
public sealed class HomeEventBus
{
    private readonly Channel<HomeEvent> channel = Channel.CreateUnbounded<HomeEvent>(new UnboundedChannelOptions {SingleReader = true});

    public void Publish(HomeEvent homeEvent) => channel.Writer.TryWrite(homeEvent);

    internal ChannelReader<HomeEvent> Reader => channel.Reader;
}

internal sealed class HomeEventDispatcher(HomeEventBus bus, IEnumerable<IHomeEventHandler> handlers, ILogger<HomeEventDispatcher> logger) : BackgroundService
{
    private readonly IReadOnlyList<IHomeEventHandler> handlers = handlers.ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var homeEvent in bus.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(homeEvent, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(e, "{Handler} failed on {Event}", handler.GetType().Name, homeEvent);
                }
            }
        }
    }
}
