using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Notifications;
using Telegram.Bot;

namespace SmartHome.Clients.Telegram;

internal sealed class TelegramNotificationChannel(TelegramBotClientProvider clientProvider, IOptions<TelegramOptions> options, ILogger<TelegramNotificationChannel> logger)
    : INotificationChannel
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (options.Value.UseFakeBot || options.Value.NotificationChatId is not { } chatId)
        {
            logger.LogInformation("Notification (not sent): {Text}", notification.Text);
            return Task.CompletedTask;
        }

        var keyboard = notification.Buttons.Count > 0 ? TelegramMenuRenderer.Keyboard(notification.Buttons) : null;
        return InvokeOperations.InvokeAsync(logger, "Telegram: notification",
                                            () => clientProvider.Client.SendMessage(chatId, notification.Text, replyMarkup: keyboard, cancellationToken: cancellationToken));
    }
}
