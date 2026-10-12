using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Menu;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SmartHome.Clients.Telegram;

/// <summary>
/// The bot: any text opens the dashboard, a button press redraws the message with the next page. Only
/// <see cref="TelegramOptions.AllowedUsers"/> get an answer. Updates are handled one by one.
/// </summary>
internal sealed class TelegramBotService(
    TelegramBotClientProvider clientProvider,
    MenuService menu,
    IOptions<TelegramOptions> options,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private static readonly TimeSpan pollingErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.UseFakeBot)
        {
            logger.LogWarning("Telegram bot is fake (Telegram:UseFakeBot): Telegram is not contacted, the menu is at /debug/menu");
            return;
        }

        var bot = clientProvider.Client;
        bot.StartReceiving(OnUpdateAsync, OnErrorAsync, new ReceiverOptions(), stoppingToken);

        await InvokeOperations.InvokeAsync(logger, "Telegram: set commands",
                                           () => bot.SetMyCommands([new BotCommand {Command = "dashboard", Description = MenuTexts.Dashboard}],
                                                                   cancellationToken: stoppingToken));
        logger.LogInformation("Telegram bot started");
    }

    private Task OnUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken) => update.Type switch
    {
        UpdateType.Message when update.Message!.From is { } from => OnMessageAsync(bot, update.Message, from, cancellationToken),
        UpdateType.CallbackQuery => OnCallbackQueryAsync(bot, update.CallbackQuery!, cancellationToken),
        _ => Task.CompletedTask,
    };

    private Task OnMessageAsync(ITelegramBotClient bot, Message message, User from, CancellationToken cancellationToken)
    {
        if (!IsAllowed(from, message.Text))
            return Task.CompletedTask;

        return InvokeOperations.InvokeAsync(logger, "Telegram: message", async () =>
        {
            var page = await menu.HandleAsync(new MenuAction.Dashboard(), cancellationToken);
            var (text, keyboard, parseMode) = TelegramMenuRenderer.Render(page);
            await bot.SendMessage(message.Chat.Id, text, parseMode, replyMarkup: keyboard, cancellationToken: cancellationToken);
        });
    }

    private Task OnCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken cancellationToken)
    {
        if (!IsAllowed(query.From, query.Data) || query.Message is not { } message)
            return Task.CompletedTask;

        return InvokeOperations.InvokeAsync(logger, "Telegram: button", async () =>
        {
            await bot.AnswerCallbackQuery(query.Id, cancellationToken: cancellationToken);

            var page = await menu.HandleAsync(MenuAction.Parse(query.Data), cancellationToken);
            var (text, keyboard, parseMode) = TelegramMenuRenderer.Render(page);
            await bot.EditMessageText(message.Chat.Id, message.MessageId, text, parseMode, replyMarkup: keyboard, cancellationToken: cancellationToken);
        });
    }

    private async Task OnErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogError("Telegram: receive error ({Source}): {Error}", source, exception.Message);

        // Polling restarts right after the handler returns; without a pause a network outage turns into a busy loop.
        if (source == HandleErrorSource.PollingError)
            await Task.Delay(pollingErrorDelay, cancellationToken);
    }

    private bool IsAllowed(User user, string? data)
    {
        var allowed = options.Value.AllowedUsers.Contains(user.Id);
        logger.LogInformation("Telegram: {User} ({UserId}), allowed: {Allowed}, data: {Data}", user.Username, user.Id, allowed, data);
        return allowed;
    }
}
