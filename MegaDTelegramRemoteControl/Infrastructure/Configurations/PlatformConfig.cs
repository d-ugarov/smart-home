namespace MegaDTelegramRemoteControl.Infrastructure.Configurations;

public record PlatformConfig
{
    public bool UseFakeDeviceConnector { get; init; }

    /// <summary> Local runs: the bot does not connect to Telegram. </summary>
    public bool UseFakeTelegramBot { get; init; }
}
