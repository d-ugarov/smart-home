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

/// <summary>
/// Where the notifications go: Telegram now, other targets (an app, mail) are more implementations. Every notification
/// that is not turned off goes to every channel, each with its own queue; the channel decides how to show it (where,
/// with or without a sound, what to do with the buttons). Not the log: the log is files for diagnostics.
/// </summary>
public interface INotificationChannel
{
    /// <summary> For the logs. </summary>
    string Name { get; }

    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}

/// <param name="Buttons"> Menu buttons under the text, one per row: the owner acts on the notification right away. </param>
/// <param name="Silent"> Delivered without a sound. </param>
public sealed record Notification(string Text, NotificationCategory Category, IReadOnlyList<MenuButton> Buttons, bool Silent);

/// <summary>
/// The owner learns about every request of the controllers and every command of the rules, as in <c>master</c>, and
/// of the automations - each in its category, sent as the owner chose for it (<see cref="NotificationPreferences"/>).
/// Repeats of a leak sensor are counted, not sent one by one (<see cref="NotificationBursts"/>).
/// </summary>
internal sealed class NotificationService(NotificationQueue queue, NotificationBursts bursts, HomeModel home, HouseClock clock, NotificationPreferences preferences)
    : IHomeEventHandler
{
    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (Format(homeEvent) is not { } text)
            return Task.CompletedTask;

        if (Burst(homeEvent) is var (key, repeats) && !bursts.Start(key, count => Create(homeEvent, repeats(count))))
            return Task.CompletedTask;

        if (Create(homeEvent, text) is { } notification)
            queue.Enqueue(notification);

        return Task.CompletedTask;
    }

    /// <summary> Null - the category is turned off. </summary>
    private Notification? Create(HomeEvent homeEvent, string text)
    {
        var category = Category(homeEvent);
        var mode = preferences.ModeOf(category);
        return mode == NotificationMode.Off ? null : new Notification(text, category, Buttons(homeEvent), Silent: mode == NotificationMode.Silent);
    }

    /// <summary> Events of a leak sensor: the key of its burst and the line for the repeats. </summary>
    private static (string Key, Func<int, string> Repeats)? Burst(HomeEvent homeEvent) => homeEvent switch
    {
        DeviceEventReceived {Kind: DeviceEventKind.LeakProtection, Entity: {Type: EntityType.Sensor} sensor} x =>
            ($"sensor:{sensor.Id}", count => $"{x.Device}: {sensor.Name}: {count} more {Times(count)} in a minute"),
        LeakHeldBack x =>
            ($"held:{x.Sensor.Id}", count => $"🚨 {x.Sensor.Name} fired {count} more {Times(count)} during the cleaning — {x.Valve.Name} not closed"),
        _ => null,
    };

    private static string Times(int count) => count == 1 ? "time" : "times";

    /// <summary> A command that failed is <see cref="NotificationCategory.System"/>: turning its category down does not hide it. </summary>
    internal static NotificationCategory Category(HomeEvent homeEvent) => homeEvent switch
    {
        DeviceEventReceived {Kind: DeviceEventKind.LeakProtection} => NotificationCategory.Security,
        DeviceEventReceived {Kind: DeviceEventKind.Unknown} => NotificationCategory.System,
        DeviceEventReceived {Kind: DeviceEventKind.Meter} => NotificationCategory.Meters,
        DeviceEventReceived => NotificationCategory.Events,
        ValveCommandExecuted or CleaningStarted or LeakHeldBack or CleaningEnded => NotificationCategory.Security,
        RuleCommandExecuted {Outcome: CommandOutcome.Sent {Result.IsSuccess: false}} => NotificationCategory.System,
        RuleCommandExecuted => NotificationCategory.Events,
        AutomationCommandExecuted {Outcome: CommandOutcome.Sent {Result.IsSuccess: false}} => NotificationCategory.System,
        AutomationCommandExecuted => NotificationCategory.Automation,
        MeterReadingEntered => NotificationCategory.Meters,
        _ => NotificationCategory.System,
    };

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
        MeterReadingEntered x => $"✏️ {x.Meter.Name}: {Reading(x.Meter, x.Reading)} entered{(x.Previous is { } previous ? $", was {Reading(x.Meter, previous)}" : "")}",
        MeterCounterReset x => $"⚠️ {x.Meter.Name}: the counter of the input went back from {x.From} to {x.To} without a restart of the controller the server saw — counted as one, "
                               + "the pulses before it may be lost; check the reading by the dial",
        _ => null,
    };

    /// <summary> A leak held back: closing the valves is one press away (with the usual confirmation). </summary>
    private IReadOnlyList<MenuButton> Buttons(HomeEvent homeEvent) =>
        homeEvent is LeakHeldBack x && home.FindValveItem(x.Valve, ValveCommand.Close) is var (page, item)
            ? [new MenuButton(item.Text, new MenuAction.AskValve(page.Id, x.Valve.Id, ValveCommand.Close))]
            : [];

    private string Time(DateTimeOffset time) => clock.ToLocal(time).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Reading(Entity meter, decimal value) => $"{MeterMenu.Format(meter.Meter!, value)} {meter.Meter!.Unit}";

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
/// Notifications wait for delivery here, a queue per channel, apart from the event bus: a slow or unreachable channel
/// holds up neither the other handlers nor the other channels. When a channel is down for long, its oldest messages are
/// dropped instead of piling up.
/// </summary>
internal sealed class NotificationQueue
{
    public const int Capacity = 1000;

    public NotificationQueue(IEnumerable<INotificationChannel> channels, ILogger<NotificationQueue> logger)
    {
        Outboxes = channels.Select(x => new Outbox(x, Channel.CreateBounded<Notification>(
                                                          new BoundedChannelOptions(Capacity) {FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true},
                                                          dropped => logger.LogWarning("Notification dropped, the queue of {Channel} is full: {Text}", x.Name, dropped.Text))))
                           .ToList();
    }

    public IReadOnlyList<Outbox> Outboxes { get; }

    public void Enqueue(Notification notification)
    {
        foreach (var outbox in Outboxes)
            outbox.Queue.Writer.TryWrite(notification);
    }

    public sealed record Outbox(INotificationChannel Channel, Channel<Notification> Queue);
}

internal sealed class NotificationSender(NotificationQueue queue, ILogger<NotificationSender> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(queue.Outboxes.Select(x => SendAsync(x, stoppingToken)));

    private async Task SendAsync(NotificationQueue.Outbox outbox, CancellationToken stoppingToken)
    {
        await foreach (var notification in outbox.Queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await outbox.Channel.SendAsync(notification, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(e, "Notification not sent to {Channel}: {Text}", outbox.Channel.Name, notification.Text);
            }
        }
    }
}
