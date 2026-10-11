using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartHome.Core.Common;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Menu;
using System.Globalization;
using System.Threading.Channels;

namespace SmartHome.Core.Notifications;

/// <summary> Delivers a message to the owner (Telegram). Not the log: the log is files for diagnostics. </summary>
public interface INotificationChannel
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

/// <param name="Buttons"> Menu buttons under the text, one per row: the owner acts on the notification right away. </param>
public sealed record Notification(string Text, IReadOnlyList<MenuButton> Buttons)
{
    public Notification(string text) : this(text, [])
    {
    }
}

/// <summary>
/// The owner learns about every request of the controllers and every command of the rules, as in <c>master</c>, and
/// of the automations. Categories and turning them off come later.
/// </summary>
internal sealed class NotificationService(NotificationQueue queue, HomeModel home, HouseClock clock) : IHomeEventHandler
{
    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (Format(homeEvent) is { } text)
            queue.Enqueue(new Notification(text, Buttons(homeEvent)));

        return Task.CompletedTask;
    }

    internal string? Format(HomeEvent homeEvent) => homeEvent switch
    {
        DeviceEventReceived x => $"{x.Device}: {x.Text}",
        DeviceRestarted x => $"🔄 {x.Device} restarted",
        RuleCommandExecuted x => $"→ {x.Target.Name}: {Command(x.Command)} — {Outcome(x.Outcome)}",
        ValveCommandExecuted x => $"💧 {x.Valve.Name}: {(x.Command == ValveCommand.Open ? "open" : "close")} — {Outcome(x.Outcome)}",
        AutomationCommandExecuted x => $"🤖 {x.Origin} → {x.Target.Name}: {Texts.Of(x.Command)} — {Outcome(x.Outcome)}",
        ServerProblem x => $"⚠️ {x.Text}",
        CleaningStarted x => $"🧽 {x.Valve.Name}: cleaning until {Time(x.Until)}, the leak sensor does not close the valves",
        LeakHeldBack x => $"🚨 {x.Sensor.Name} fired during the cleaning — {x.Valve.Name} not closed",
        CleaningEnded {End: CleaningEnd.SensorDry} x => $"🧽 {x.Valve.Name}: cleaning over, the leak sensor closes the valves again",
        CleaningEnded {End: CleaningEnd.SensorWet} x => $"🚨 {x.Valve.Name}: cleaning over, the leak sensor is still wet — closing the valves",
        CleaningEnded x => $"⚠️ {x.Valve.Name}: cleaning over, the leak sensor could not be read — check it: a sensor still wet does not close the valves",
        DelayedActionDropped x => $"⚠️ {x.Origin}: {x.Action} not done, {Texts.Duration(x.Late)} late (the server was down)",
        _ => null,
    };

    /// <summary> A leak held back: closing the valves is one press away (with the usual confirmation). </summary>
    private IReadOnlyList<MenuButton> Buttons(HomeEvent homeEvent) =>
        homeEvent is LeakHeldBack x && home.FindValveItem(x.Valve, ValveCommand.Close) is var (page, item)
            ? [new MenuButton(item.Text, new MenuAction.AskValve(page.Id, x.Valve.Id, ValveCommand.Close))]
            : [];

    private string Time(DateTimeOffset time) => clock.ToLocal(time).ToString("HH:mm", CultureInfo.InvariantCulture);

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
        CommandOutcome.Withheld {Reason: WithheldReason.Vacation} => "skipped, vacation",
        CommandOutcome.Withheld {Reason: WithheldReason.SkippedOnce} => "skipped once from the menu",
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

    private readonly Channel<Notification> channel;

    public NotificationQueue(ILogger<NotificationQueue> logger)
    {
        channel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(Capacity) {FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true},
                                                      dropped => logger.LogWarning("Notification dropped, the queue is full: {Text}", dropped.Text));
    }

    public void Enqueue(Notification notification) => channel.Writer.TryWrite(notification);

    public ChannelReader<Notification> Reader => channel.Reader;
}

internal sealed class NotificationSender(NotificationQueue queue, INotificationChannel channel, ILogger<NotificationSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await channel.SendAsync(notification, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Notification not sent: {Text}", notification.Text);
            }
        }
    }
}
