using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SmartHome.Core.Configuration;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Menu;
using SmartHome.Core.Notifications;
using SmartHome.Core.Rules;
using SmartHome.Core.State;
using SmartHome.Core.Valves;

namespace SmartHome.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary> The core of the house. An integration has to provide <see cref="Devices.IDeviceGateway"/>, a client - <see cref="INotificationChannel"/>. </summary>
    public static IServiceCollection AddSmartHomeCore(this IServiceCollection services)
    {
        services.AddOptions<HomeOptions>().BindConfiguration(HomeOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<HomeOptions>, HomeOptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HomeModel>();
        services.AddSingleton<HomeState>();
        services.AddSingleton<HomeEventBus>();
        services.AddSingleton<InputRuleEngine>();
        services.AddSingleton<ValveService>();
        services.AddSingleton<MenuService>();

        services.AddSingleton<NotificationQueue>();
        services.AddSingleton<IHomeEventHandler, NotificationService>();
        services.AddHostedService<NotificationSender>();
        services.AddHostedService<HomeEventDispatcher>();
        services.AddHostedService<StatePollingService>();

        return services;
    }
}
