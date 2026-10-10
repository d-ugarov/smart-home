using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Notifications;
using Telegram.Bot;

namespace SmartHome.Clients.Telegram;

internal sealed class TelegramNotificationChannel(TelegramBotClientProvider clientProvider, IOptions<TelegramOptions> options, ILogger<TelegramNotificationChannel> logger)
    : INotificationChannel
{
    public Task SendAsync(string text, CancellationToken cancellationToken)
    {
        if (options.Value.UseFakeBot || options.Value.NotificationChatId is not { } chatId)
        {
            logger.LogInformation("Notification (not sent): {Text}", text);
            return Task.CompletedTask;
        }

        return InvokeOperations.InvokeAsync(logger, "Telegram: notification",
                                            () => clientProvider.Client.SendMessage(chatId, text, cancellationToken: cancellationToken));
    }
}
