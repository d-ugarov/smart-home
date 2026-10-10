using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace SmartHome.Clients.Telegram;

/// <summary> One client for the bot and the notifications, created on first use. </summary>
internal sealed class TelegramBotClientProvider(IHttpClientFactory httpClientFactory, IOptions<TelegramOptions> options, IHostApplicationLifetime lifetime)
{
    public const string HttpClientName = "telegram";

    private readonly Lazy<ITelegramBotClient> client = new(() => new TelegramBotClient(options.Value.BotToken,
                                                                                       httpClientFactory.CreateClient(HttpClientName),
                                                                                       lifetime.ApplicationStopping));

    public ITelegramBotClient Client => client.Value;
}
