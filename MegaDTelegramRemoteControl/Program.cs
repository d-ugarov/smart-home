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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Web;
using System;
using System.Net;
using System.Net.Http;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info($"[Main:{Constants.InstanceId}] Application started");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseNLog();
    builder.Host.UseSystemd();

    builder.ConfigureKestrel();
    AddSystemServices(builder);
    ConfigureServices(builder);
    ConfigureOptions(builder);

    var app = builder.Build();

    logger.Info(app.Services.GetRequiredService<HomeConfig>().ToString());

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
    logger.Fatal(e, $"[Main:{Constants.InstanceId}] Application crashed");
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
    // Configuration is read when services are resolved, not here: the host may still add configuration sources until Build().
    builder.Services.AddHttpClient<TelegramBotService>()
           .ConfigurePrimaryHttpMessageHandler(x =>
           {
               var telegramProxyConfig = x.GetRequiredService<IOptions<TelegramProxyConfig>>().Value;

               return new HttpClientHandler
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
                      };
           });
    builder.Services.AddTransient<StubBotService>();
    builder.Services.AddTransient<IBotService>(x => x.GetRequiredService<IOptions<PlatformConfig>>().Value.UseFakeTelegramBot
                                                   ? x.GetRequiredService<StubBotService>()
                                                   : x.GetRequiredService<TelegramBotService>());
    builder.Services.AddTransient<IBotHandler, TelegramBotHandler>();
    builder.Services.AddTransient<IMegaDEventsHandler, MegaDEventsHandler>();
    builder.Services.AddTransient<IHomeTriggerProcessor, HomeTriggerProcessor>();
    builder.Services.AddSingleton<IWarmupCacheService, InitService>();
    builder.Services.AddTransient<IDeviceCommandParser, MegaDCommandParser>();

    builder.Services.AddSingleton(x => CreateHomeConfig(x.GetRequiredService<IConfiguration>()));
    builder.Services.AddSingleton<IHomeService, HomeService>();

    builder.Services.AddHttpClient<MegaDConnector>();
    builder.Services.AddTransient<StubDeviceConnector>();
    builder.Services.AddTransient<IDeviceConnector>(x => x.GetRequiredService<IOptions<PlatformConfig>>().Value.UseFakeDeviceConnector
                                                        ? x.GetRequiredService<StubDeviceConnector>()
                                                        : x.GetRequiredService<MegaDConnector>());

    builder.Services.AddHttpClient<IAntiCaptchaService, AntiCaptchaService>();

    builder.Services.AddTransient<IPesService, PesService>();
    builder.Services.AddHttpClient<IPesConnector, PesConnector>();

    builder.Services.AddHostedService<JobScheduler>();
}

void ConfigureOptions(WebApplicationBuilder builder)
{
    builder.ConfigureByType<PlatformConfig>();
    builder.ConfigureByType<TelegramConfig>();
    builder.ConfigureByType<TelegramProxyConfig>();
    builder.ConfigureByType<JobSchedulerConfig>();
    builder.ConfigureByType<PesConfig>();
    builder.ConfigureByType<AntiCaptchaConfig>();
}

HomeConfig CreateHomeConfig(IConfiguration configuration)
{
    var automationConfig = configuration.GetConfigByType<AutomationConfig>() ?? new();
    var deviceConfig = configuration.GetConfigByType<DevicesConfig>() ?? new();
    var homeMapConfig = configuration.GetConfigByType<HomeMapConfig>() ?? new();
    var keeneticProxyConfig = configuration.GetConfigByType<KeeneticProxyConfig>() ?? new();

    return ConfigHelper.MakeConfig(deviceConfig, homeMapConfig, automationConfig, keeneticProxyConfig);
}
