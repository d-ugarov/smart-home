using MegaDTelegramRemoteControl.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace MegaDTelegramRemoteControl.Services.StubServices;

public class StubBotService : IBotService
{
    private readonly ILogger<StubBotService> logger;

    public StubBotService(ILogger<StubBotService> logger)
    {
        this.logger = logger;
    }

    public Task InitBotAsync()
    {
        logger.LogInformation("TelegramBot is fake (PlatformConfig.UseFakeTelegramBot), Telegram is not contacted");
        return Task.CompletedTask;
    }
}
