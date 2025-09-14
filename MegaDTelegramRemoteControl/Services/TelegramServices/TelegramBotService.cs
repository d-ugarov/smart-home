using MegaDTelegramRemoteControl.Infrastructure.Configurations;
using MegaDTelegramRemoteControl.Infrastructure.Models;
using MegaDTelegramRemoteControl.Models;
using MegaDTelegramRemoteControl.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MegaDTelegramRemoteControl.Services.TelegramServices;

public class TelegramBotService : IBotService
{
    private readonly TelegramConfig telegramConfig;
    private TelegramBotClient bot = null!;

    private readonly HttpClient httpClient;
    private readonly IBotHandler botHandler;
    private readonly ILogger<TelegramBotService> logger;

    private bool isInitialized;

    public TelegramBotService(IOptions<TelegramConfig> telegramConfig,
        IBotHandler botHandler,
        HttpClient httpClient,
        ILogger<TelegramBotService> logger)
    {
        this.telegramConfig = telegramConfig.Value;
        this.botHandler = botHandler;
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public Task InitBotAsync()
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            if (isInitialized)
                return;

            logger.LogTrace("TelegramBot starting..");

            bot = new TelegramBotClient(telegramConfig.BotAccessToken, httpClient);
            bot.OnMessage += async (_, args) => await OnMessageReceivedAsync(args);
            bot.OnCallbackQuery += async (_, args) => await OnCallbackQueryReceivedAsync(args);
            bot.StartReceiving();

            await bot.SetMyCommandsAsync(new List<BotCommand>
                                         {
                                             new()
                                             {
                                                 Command = "dashboard",
                                                 Description = "Dashboard"
                                             }
                                         });

            isInitialized = true;
            logger.LogTrace("TelegramBot started");
        });
    }

    private Task OnMessageReceivedAsync(MessageEventArgs e)
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            if (e.Message == null)
                return;

            var allowed = IsUserAllowed(e.Message.From);
            logger.LogTrace($"[OnMessageReceived] " +
                            $"From: {e.Message.From.Username} ({e.Message.From.Id}), " +
                            $"allowed: {allowed}, " +
                            $"message: {e.Message.Text}");

            if (!allowed)
                return;

            await ProcessMessageAsync(e.Message);
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

    private Task OnCallbackQueryReceivedAsync(CallbackQueryEventArgs e)
    {
        return InvokeOperations.InvokeOperationAsync(async () =>
        {
            if (e.CallbackQuery == null)
                return;

            var allowed = IsUserAllowed(e.CallbackQuery.From);
            logger.LogTrace($"[OnCallbackQueryReceived] " +
                            $"From: {e.CallbackQuery.From.Username} ({e.CallbackQuery.From.Id}), " +
                            $"allowed: {allowed}, " +
                            $"message: {e.CallbackQuery.Data}");

            if (!allowed)
                return;

            await bot.AnswerCallbackQueryAsync(e.CallbackQuery.Id);

            await ProcessCallbackQueryAsync(e.CallbackQuery);
        });
    }

    private async Task ProcessCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        var menu = await botHandler.ProcessActionAsync(callbackQuery.Data);

        var chatId = new ChatId(callbackQuery.Message.Chat.Id);
        var (text, inlineKeyboard) = GetFormattedAnswer(menu);

        await EditMessageAsync(text, inlineKeyboard, chatId, callbackQuery.Message);
    }

    private static (string text, InlineKeyboardMarkup inlineKeyboard) GetFormattedAnswer(BotMenu menu)
    {
        // var buttons = menu.Buttons
        //                   .GroupBy(x => x.Order, (k, v) => new
        //                                                    {
        //                                                        Order = k,
        //                                                        Row = v.Select(x =>
        //                                                            InlineKeyboardButton.WithCallbackData(x.Name,
        //                                                                x.ActionId))
        //                                                    })
        //                   .OrderBy(x => x.Order)
        //                   .Select(x => x.Row)
        //                   .ToList();

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

    private Task SendMessageAsync(string text, IReplyMarkup inlineKeyboard, Chat chat)
    {
        return bot.SendTextMessageAsync(chat.Id, text, replyMarkup: inlineKeyboard);
    }

    private Task EditMessageAsync(string text, InlineKeyboardMarkup inlineKeyboard, ChatId chatId, Message message)
    {
        return bot.EditMessageTextAsync(chatId, message.MessageId, text, ParseMode.Default, replyMarkup: inlineKeyboard);
    }

    private bool IsUserAllowed(User user) => !telegramConfig.AllowedUsers.Any() ||
                                             telegramConfig.AllowedUsers.Contains(user.Id);

}