using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHome.Core.Common;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using System.Threading.Channels;

namespace SmartHome.Core.Notifications;

/// <summary> Delivers a message to the owner (Telegram). Not the log: the log is files for diagnostics. </summary>
public interface INotificationChannel
{
    Task SendAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// The owner learns about every request of the controllers and every command of the rules, as in <c>master</c>, and
/// of the automations. Categories and turning them off come later.
/// </summary>
internal sealed class NotificationService(NotificationQueue queue) : IHomeEventHandler
{
    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (Format(homeEvent) is { } text)
            queue.Enqueue(text);

        return Task.CompletedTask;
    }

    internal static string? Format(HomeEvent homeEvent) => homeEvent switch
    {
        DeviceEventReceived x => $"{x.Device}: {x.Text}",
        DeviceRestarted x => $"🔄 {x.Device} restarted",
        RuleCommandExecuted x => $"→ {x.Target.Name}: {Command(x.Command)} — {Outcome(x.Outcome)}",
        ValveCommandExecuted x => $"💧 {x.Valve.Name}: {(x.Command == ValveCommand.Open ? "open" : "close")} — {Outcome(x.Outcome)}",
        AutomationCommandExecuted x => $"🤖 {x.Origin} → {x.Target.Name}: {Texts.Of(x.Command)} — {Outcome(x.Outcome)}",
        DelayedActionDropped x => $"⚠️ {x.Origin}: {x.Action} not done, {Texts.Duration(x.Late)} late (the server was down)",
        _ => null,
    };

    private static string Command(SwitchCommand command) => command switch
    {
        SwitchCommand.On => "on",
        SwitchCommand.Off => "off",
        SwitchCommand.Toggle => "toggle",
        _ => command.ToString(),
    };

    private static string Outcome(CommandOutcome outcome) => outcome switch
    {
        CommandOutcome.Sent {Result.IsSuccess: true} => "OK",
        CommandOutcome.Sent x => $"⚠️ failed: {x.Result.Error}",
        CommandOutcome.Skipped x => $"skipped, already {Texts.Of(x.State)}",
        CommandOutcome.Busy => "skipped, the previous pulse is still running",
        _ => outcome.ToString(),
    };
}

/// <summary>
/// Notifications wait for delivery here, apart from the event bus: a slow or unreachable Telegram must not hold up the
/// other handlers. When Telegram is down for long, the oldest messages are dropped instead of piling up.
/// </summary>
internal sealed class NotificationQueue
{
    public const int Capacity = 1000;

    private readonly Channel<string> channel;

    public NotificationQueue(ILogger<NotificationQueue> logger)
    {
        channel = Channel.CreateBounded<string>(new BoundedChannelOptions(Capacity) {FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true},
                                                dropped => logger.LogWarning("Notification dropped, the queue is full: {Text}", dropped));
    }

    public void Enqueue(string text) => channel.Writer.TryWrite(text);

    public ChannelReader<string> Reader => channel.Reader;
}

internal sealed class NotificationSender(NotificationQueue queue, INotificationChannel channel, ILogger<NotificationSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var text in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await channel.SendAsync(text, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Notification not sent: {Text}", text);
            }
        }
    }
}
