using MegaDTelegramRemoteControl.Infrastructure.Configurations;
using MegaDTelegramRemoteControl.Infrastructure.Filters;
using MegaDTelegramRemoteControl.Infrastructure.Helpers;
using MegaDTelegramRemoteControl.Infrastructure.Middlewares;
using MegaDTelegramRemoteControl.Models;
using MegaDTelegramRemoteControl.Services;
using MegaDTelegramRemoteControl.Services.Interfaces;
using MegaDTelegramRemoteControl.Services.MegaDServices;
using MegaDTelegramRemoteControl.Services.PrivateOffices.AntiCaptcha;
using MegaDTelegramRemoteControl.Services.PrivateOffices.PES;
using MegaDTelegramRemoteControl.Services.StubServices;
using MegaDTelegramRemoteControl.Services.TelegramServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Web;
using System;
using System.IO;
using System.Net;
using System.Net.Http;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info($"[Main:{Constants.InstanceId}] Application started");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseNLog();
    builder.Host.UseContentRoot(Directory.GetCurrentDirectory());

    builder.ConfigureKestrel();
    AddSystemServices(builder);
    ConfigureServices(builder);
    ConfigureOptions(builder);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
        app.UseDeveloperExceptionPage();

    app.UseMiddleware<RequestLoggingMiddleware>();

    app.UseResponseCaching();
    app.UseStaticFiles();
    app.UseRouting();
    app.MapControllers();

    app.Run();
}
catch (Exception e)
{
    logger.Fatal($"Application crashed: {e.Message}");
    logger.Error($"[Main:{Constants.InstanceId}] Application crashed: {e.Message} {Environment.NewLine}{e.StackTrace}");
    throw;
}
finally
{
    logger.Info($"[Main:{Constants.InstanceId}] Application stopped");
    LogManager.Shutdown();
}

void AddSystemServices(WebApplicationBuilder builder)
{
    builder.Services
           .AddMemoryCache()
           .AddResponseCaching()
           .AddMvcCore(x =>
           {
               x.Filters.Add(typeof(HttpResponseExceptionFilter));
           })
           .AddApiExplorer()
           .AddDataAnnotationsLocalization()
           .AddAuthorization()
           .AddDataAnnotations()
           .AddJsonOptions(x =>
           {
               x.JsonSerializerOptions.AllowTrailingCommas = true;
               x.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
           });
}

void ConfigureServices(WebApplicationBuilder builder)
{
    var platformConfig = builder.GetConfigByType<PlatformConfig>() ?? new();
    var telegramProxyConfig = builder.GetConfigByType<TelegramProxyConfig>() ?? new();

    builder.Services.AddHttpClient<IBotService, TelegramBotService>()
           .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                                                     {
                                                         Proxy = !string.IsNullOrEmpty(telegramProxyConfig.Host) &&
                                                                 !string.IsNullOrEmpty(telegramProxyConfig.Username) &&
                                                                 !string.IsNullOrEmpty(telegramProxyConfig.Password)
                                                             ? new WebProxy
                                                               {
                                                                   Address = new Uri(telegramProxyConfig.Host),
                                                                   Credentials = new NetworkCredential(telegramProxyConfig.Username, telegramProxyConfig.Password),
                                                               }
                                                             : null,
                                                     });
    builder.Services.AddTransient<IBotHandler, TelegramBotHandler>();
    builder.Services.AddTransient<IMegaDEventsHandler, MegaDEventsHandler>();
    builder.Services.AddTransient<IHomeTriggerProcessor, HomeTriggerProcessor>();
    builder.Services.AddSingleton<IWarmupCacheService, InitService>();
    builder.Services.AddTransient<IDeviceCommandParser, MegaDCommandParser>();

    var homeConfig = CreateHomeConfig(builder);
    logger.Info(homeConfig.ToString());

    builder.Services.AddSingleton<IHomeService, HomeService>(x =>
        new HomeService(
            x.GetRequiredService<ILogger<HomeService>>(),
            homeConfig));

    if (!platformConfig.UseFakeDeviceConnector)
        builder.Services.AddHttpClient<IDeviceConnector, MegaDConnector>();
    else
        builder.Services.AddTransient<IDeviceConnector, StubDeviceConnector>();

    builder.Services.AddHttpClient<IAntiCaptchaService, AntiCaptchaService>();

    builder.Services.AddTransient<IPesService, PesService>();
    builder.Services.AddHttpClient<IPesConnector, PesConnector>();

    builder.Services.AddHostedService<JobScheduler>();
}

void ConfigureOptions(WebApplicationBuilder builder)
{
    builder.ConfigureByType<PlatformConfig>();
    builder.ConfigureByType<TelegramConfig>();
    builder.ConfigureByType<JobSchedulerConfig>();
    builder.ConfigureByType<PesConfig>();
    builder.ConfigureByType<AntiCaptchaConfig>();
}

HomeConfig CreateHomeConfig(WebApplicationBuilder builder)
{
    var automationConfig = builder.GetConfigByType<AutomationConfig>() ?? new();
    var deviceConfig = builder.GetConfigByType<DevicesConfig>() ?? new();
    var homeMapConfig = builder.GetConfigByType<HomeMapConfig>() ?? new();
    var keeneticProxyConfig = builder.GetConfigByType<KeeneticProxyConfig>() ?? new();

    return ConfigHelper.MakeConfig(deviceConfig, homeMapConfig, automationConfig, keeneticProxyConfig);
}