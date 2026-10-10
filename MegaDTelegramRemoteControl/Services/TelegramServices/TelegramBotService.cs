using MegaDTelegramRemoteControl.Infrastructure.Configurations;
using MegaDTelegramRemoteControl.Infrastructure.Models;
using MegaDTelegramRemoteControl.Models;
using MegaDTelegramRemoteControl.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MegaDTelegramRemoteControl.Services.TelegramServices;

public class TelegramBotService : IBotService
{
    private static readonly TimeSpan pollingErrorDelay = TimeSpan.FromSeconds(5);

    private readonly TelegramConfig telegramConfig;
    private TelegramBotClient bot = null!;

    private readonly HttpClient httpClient;
    private readonly IBotHandler botHandler;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly ILogger<TelegramBotService> logger;

    private bool isInitialized;

    public TelegramBotService(IOptions<TelegramConfig> telegramConfig,
        IBotHandler botHandler,
        HttpClient httpClient,
        IHostApplicationLifetime applicationLifetime,
        ILogger<TelegramBotService> logger)
    {
        this.telegramConfig = telegramConfig.Value;
        this.botHandler = botHandler;
        this.httpClient = httpClient;
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;
    }

    public Task InitBotAsync()
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            if (isInitialized)
                return;

            logger.LogTrace("TelegramBot starting..");

            var stopping = applicationLifetime.ApplicationStopping;

            bot = new TelegramBotClient(telegramConfig.BotAccessToken, httpClient, stopping);
            bot.StartReceiving(OnUpdateReceivedAsync, OnReceiveErrorAsync, new ReceiverOptions(), stopping);

            await bot.SetMyCommands(new List<BotCommand>
                                    {
                                        new()
                                        {
                                            Command = "dashboard",
                                            Description = "Dashboard"
                                        }
                                    },
                                    cancellationToken: stopping);

            isInitialized = true;
            logger.LogTrace("TelegramBot started");
        });
    }

    private Task OnUpdateReceivedAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        return update.Type switch
        {
            UpdateType.Message => OnMessageReceivedAsync(update.Message!),
            UpdateType.CallbackQuery => OnCallbackQueryReceivedAsync(update.CallbackQuery!),
            _ => Task.CompletedTask,
        };
    }

    private async Task OnReceiveErrorAsync(ITelegramBotClient _, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        logger.LogError($"[TelegramBot] Receive error ({source}): {exception.Message}");

        // Polling restarts right after the handler returns; without a pause a network outage turns into a busy loop.
        if (source == HandleErrorSource.PollingError)
            await Task.Delay(pollingErrorDelay, cancellationToken);
    }

    private Task OnMessageReceivedAsync(Message message)
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            if (message.From == null)
                return;

            var allowed = IsUserAllowed(message.From);
            logger.LogTrace($"[OnMessageReceived] " +
                            $"From: {message.From.Username} ({message.From.Id}), " +
                            $"allowed: {allowed}, " +
                            $"message: {message.Text}");

            if (!allowed)
                return;

            await ProcessMessageAsync(message);
        });
    }

    private async Task ProcessMessageAsync(Message message)
    {
        switch (message.Text?.ToLowerInvariant())
        {
            case "dashboard":
            default:
            {
                var mainMenu = await botHandler.ProcessActionAsync();

                var (text, inlineKeyboard) = GetFormattedAnswer(mainMenu);
                await SendMessageAsync(text, inlineKeyboard, message.Chat);
                break;
            }
        }
    }

    private Task OnCallbackQueryReceivedAsync(CallbackQuery callbackQuery)
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            var allowed = IsUserAllowed(callbackQuery.From);
            logger.LogTrace($"[OnCallbackQueryReceived] " +
                            $"From: {callbackQuery.From.Username} ({callbackQuery.From.Id}), " +
                            $"allowed: {allowed}, " +
                            $"message: {callbackQuery.Data}");

            if (!allowed)
                return;

            await bot.AnswerCallbackQuery(callbackQuery.Id);

            await ProcessCallbackQueryAsync(callbackQuery);
        });
    }

    private async Task ProcessCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Message == null)
            return;

        var menu = await botHandler.ProcessActionAsync(callbackQuery.Data);

        var (text, inlineKeyboard) = GetFormattedAnswer(menu);

        await EditMessageAsync(text, inlineKeyboard, callbackQuery.Message);
    }

    private static (string text, InlineKeyboardMarkup inlineKeyboard) GetFormattedAnswer(BotMenu menu)
    {
        var buttons = menu.Buttons
                          .OrderBy(x => x.Order)
                          .Select(x => new List<InlineKeyboardButton>
                                       {
                                           InlineKeyboardButton.WithCallbackData(x.Name, x.ActionId)
                                       })
                          .ToList();

        if (menu.FooterButtons.Any())
        {
            buttons.Add(menu.FooterButtons
                            .OrderBy(x => x.Order)
                            .Select(x => InlineKeyboardButton.WithCallbackData(x.Name, x.ActionId))
                            .ToList());
        }

        var inlineKeyboard = new InlineKeyboardMarkup(buttons);

        return (menu.Text, inlineKeyboard);
    }

    private Task SendMessageAsync(string text, InlineKeyboardMarkup inlineKeyboard, Chat chat)
    {
        return bot.SendMessage(chat.Id, text, replyMarkup: inlineKeyboard);
    }

    private Task EditMessageAsync(string text, InlineKeyboardMarkup inlineKeyboard, Message message)
    {
        return bot.EditMessageText(message.Chat.Id, message.MessageId, text, replyMarkup: inlineKeyboard);
    }

    private bool IsUserAllowed(User user) => !telegramConfig.AllowedUsers.Any() ||
                                             telegramConfig.AllowedUsers.Contains(user.Id);
}
