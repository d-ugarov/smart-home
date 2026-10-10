using System.Collections.Generic;

namespace MegaDTelegramRemoteControl.Infrastructure.Configurations;

public record TelegramConfig
{
    public string BotAccessToken { get; init; } = null!;

    public HashSet<long> AllowedUsers { get; init; } = new();
}

public record TelegramProxyConfig
{
    public string? Host { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}