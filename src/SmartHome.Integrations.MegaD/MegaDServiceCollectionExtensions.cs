using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartHome.Core.Devices;
using SmartHome.Core.Events;

namespace SmartHome.Integrations.MegaD;

public static class MegaDServiceCollectionExtensions
{
    public static IServiceCollection AddMegaD(this IServiceCollection services)
    {
        services.AddOptions<MegaDOptions>().BindConfiguration(MegaDOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MegaDOptions>, MegaDOptionsValidator>();

        services.AddSingleton<MegaDBindings>();
        services.AddSingleton<MegaDClient>();
        services.AddSingleton<MegaDSimulator>();
        // The password is a part of the URL: the default request logging would write it out on every poll.
        services.AddHttpClient(MegaDClient.HttpClientName)
                .RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(x => x.GetRequiredService<IOptions<MegaDOptions>>().Value.UseSimulator
                                                        ? x.GetRequiredService<MegaDSimulator>()
                                                        : new SocketsHttpHandler());

        services.AddSingleton<IDeviceGateway, MegaDGateway>();
        services.AddSingleton<IMegaDRequestHandler, MegaDEventProcessor>();
        services.AddSingleton<IHomeEventHandler, MegaDRestartHandler>();

        return services;
    }
}
