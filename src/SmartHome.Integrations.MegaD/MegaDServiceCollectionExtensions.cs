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
        services.AddHttpClient(MegaDClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(x => x.GetRequiredService<IOptions<MegaDOptions>>().Value.UseSimulator
                                                        ? x.GetRequiredService<MegaDSimulator>()
                                                        : new SocketsHttpHandler());

        services.AddSingleton<IDeviceGateway, MegaDGateway>();
        services.AddSingleton<IMegaDRequestHandler, MegaDEventProcessor>();
        services.AddSingleton<IHomeEventHandler, MegaDRestartHandler>();

        return services;
    }
}
