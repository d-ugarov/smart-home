using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartHome.Core.Notifications;
using System.Net;

namespace SmartHome.Clients.Telegram;

public static class TelegramServiceCollectionExtensions
{
    public static IServiceCollection AddTelegramBot(this IServiceCollection services)
    {
        services.AddOptions<TelegramOptions>().BindConfiguration(TelegramOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();

        services.AddHttpClient(TelegramBotClientProvider.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(x => new SocketsHttpHandler {Proxy = CreateProxy(x.GetRequiredService<IOptions<TelegramOptions>>().Value.Proxy)});

        services.AddSingleton<TelegramBotClientProvider>();
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>();
        services.AddHostedService<TelegramBotService>();

        return services;
    }

    private static WebProxy? CreateProxy(TelegramProxyOptions? proxy) => proxy == null
        ? null
        : new WebProxy(proxy.Address)
          {
              Credentials = string.IsNullOrEmpty(proxy.Username) ? null : new NetworkCredential(proxy.Username, proxy.Password),
          };
}
