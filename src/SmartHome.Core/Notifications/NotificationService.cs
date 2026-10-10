using SmartHome.Core.Entities;
using SmartHome.Core.Events;

namespace SmartHome.Core.Notifications;

/// <summary> Delivers a message to the owner (Telegram). Not the log: the log is files for diagnostics. </summary>
public interface INotificationChannel
{
    Task SendAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// The owner learns about every request of the controllers and every command of the rules, as in <c>master</c>.
/// Categories and turning them off come later.
/// </summary>
internal sealed class NotificationService(INotificationChannel channel) : IHomeEventHandler
{
    public Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        var text = Format(homeEvent);
        return text == null ? Task.CompletedTask : channel.SendAsync(text, cancellationToken);
    }

    internal static string? Format(HomeEvent homeEvent) => homeEvent switch
    {
        DeviceEventReceived x => $"{x.Device}: {x.Text}",
        DeviceRestarted x => $"🔄 {x.Device} restarted",
        RuleCommandExecuted x => $"→ {x.Target.Name}: {Command(x.Command)} — {Outcome(x.Outcome)}",
        ValveCommandExecuted x => $"💧 {x.Valve.Name}: {(x.Command == ValveCommand.Open ? "open" : "close")} — {Outcome(x.Outcome)}",
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
        CommandOutcome.Skipped x => $"skipped, already {(x.State == OnOff.On ? "on" : "off")}",
        CommandOutcome.Busy => "skipped, the previous pulse is still running",
        _ => outcome.ToString(),
    };
}
