using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartHome.Core.Automations;
using SmartHome.Core.Configuration;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.Menu;
using SmartHome.Core.Notifications;
using SmartHome.Core.Rules;
using SmartHome.Core.State;
using SmartHome.Core.Storage;
using SmartHome.Core.Valves;

namespace SmartHome.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// The core of the house. An integration has to provide <see cref="Devices.IDeviceGateway"/>, a client -
    /// <see cref="INotificationChannel"/>, the database - <see cref="IHomeStorage"/>.
    /// </summary>
    public static IServiceCollection AddSmartHomeCore(this IServiceCollection services)
    {
        services.AddOptions<HomeOptions>().BindConfiguration(HomeOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<HomeOptions>, HomeOptionsValidator>();
        services.AddOptions<AutomationSettings>().BindConfiguration(AutomationSettings.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AutomationSettings>, AutomationSettingsValidator>();
        services.AddOptions<CleaningSettings>().BindConfiguration(CleaningSettings.Section);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HomeModel>();
        services.AddSingleton<HouseClock>();
        services.AddSingleton<HomeState>();
        services.AddSingleton<HomeEventBus>();
        services.AddSingleton<InputRuleEngine>();
        services.AddSingleton<ValveService>();
        services.AddSingleton<CleaningService>();
        services.AddSingleton<AutomationEngine>();
        services.AddSingleton<AutomationMenu>();
        services.AddSingleton<ScheduleMenu>();
        services.AddSingleton<CleaningMenu>();
        services.AddSingleton<MenuService>();

        // First: the saved state is back before anything reads or changes it.
        services.AddHostedService<HomeStartup>();

        services.AddSingleton<NotificationQueue>();
        services.AddSingleton<IHomeEventHandler, NotificationService>();
        services.AddSingleton<StatePersistence>();
        services.AddSingleton<IHomeEventHandler>(x => x.GetRequiredService<StatePersistence>());
        services.AddSingleton<IHomeEventHandler>(x => x.GetRequiredService<AutomationEngine>());
        services.AddSingleton<IHostedService>(x => x.GetRequiredService<StatePersistence>());
        services.AddHostedService<NotificationSender>();
        services.AddHostedService<HomeEventDispatcher>();
        services.AddHostedService<StatePollingService>();
        services.AddSingleton<IHostedService>(x => x.GetRequiredService<AutomationEngine>());
        services.AddSingleton<IHostedService>(x => x.GetRequiredService<CleaningService>());

        return services;
    }
}
