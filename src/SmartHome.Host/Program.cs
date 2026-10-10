using NLog;
using NLog.Web;
using SmartHome.Clients.Telegram;
using SmartHome.Core;
using SmartHome.Core.Home;
using SmartHome.Host;
using SmartHome.Integrations.MegaD;
using SmartHome.Persistence;

var logger = LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();

try
{
    logger.Info("Application started");

    var builder = WebApplication.CreateBuilder(args);

    builder.Configuration.AddHomeFiles();
    builder.UseDefaultListenAddress();
    builder.Host.UseNLog();
    builder.Host.UseSystemd();

    builder.Services
           .AddSmartHomeCore()
           .AddPersistence()
           .AddMegaD()
           .AddTelegramBot();

    var app = builder.Build();

    // Reading the model validates the whole config: a broken one stops the server here, with every error listed.
    logger.Info(app.Services.GetRequiredService<HomeModel>().Describe());

    app.UseMiddleware<RequestLoggingMiddleware>();
    app.MapMegaD();
    app.MapDebugMenu();

    app.Run();
}
catch (Exception e)
{
    logger.Fatal(e, "Application crashed");
    throw;
}
finally
{
    logger.Info("Application stopped");
    LogManager.Shutdown();
}

public partial class Program;
