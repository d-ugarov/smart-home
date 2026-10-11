using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Notifications;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SmartHome.Clients.Telegram;

internal sealed class TelegramNotificationChannel(TelegramBotClientProvider clientProvider, IOptions<TelegramOptions> options, ILogger<TelegramNotificationChannel> logger)
    : INotificationChannel
{
    public string Name => "Telegram";

    /// <summary>
    /// A topic per category when the chat is a group with topics; the rest goes to the general topic. A topic Telegram
    /// rejects (deleted or closed by the owner) does not lose the message: it goes to the general topic with a warning.
    /// </summary>
    public async Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (options.Value.UseFakeBot || options.Value.NotificationChatId is not { } chatId)
        {
            logger.LogInformation("Notification (not sent): {Category}: {Text}", notification.Category, notification.Text);
            return;
        }

        int? topic = options.Value.NotificationTopics.TryGetValue(notification.Category.ToString(), out var id) ? id : null;

        var result = await SendAsync(chatId, topic, notification.Text, notification, cancellationToken);
        if (result.IsSuccess || topic == null || cancellationToken.IsCancellationRequested)
            return;

        await SendAsync(chatId, null, $"{notification.Text}\n\n⚠️ Not delivered to the topic of {notification.Category} ({topic}): {result.Error}", notification,
                        cancellationToken);
    }

    private Task<OperationResult<Message>> SendAsync(long chatId, int? topic, string text, Notification notification, CancellationToken cancellationToken)
    {
        var keyboard = notification.Buttons.Count > 0 ? TelegramMenuRenderer.Keyboard(notification.Buttons) : null;
        return InvokeOperations.InvokeAsync(logger, $"Telegram: notification, topic {topic?.ToString(CultureInfo.InvariantCulture) ?? "general"}",
                                            async () => await clientProvider.Client.SendMessage(chatId, text, replyMarkup: keyboard, messageThreadId: topic,
                                                                                                disableNotification: notification.Silent,
                                                                                                cancellationToken: cancellationToken));
    }
}
