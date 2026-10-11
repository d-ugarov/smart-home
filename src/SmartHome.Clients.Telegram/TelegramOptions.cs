using Microsoft.Extensions.Options;
using SmartHome.Core.Notifications;

namespace SmartHome.Clients.Telegram;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";

    /// <summary> A secret: appsettings of the server. </summary>
    public string BotToken { get; init; } = "";

    /// <summary> Telegram user ids allowed to use the bot; everybody else is ignored. Must not be empty. </summary>
    public HashSet<long> AllowedUsers { get; init; } = [];

    /// <summary> Where notifications go (the owner's chat). Required unless <see cref="UseFakeBot"/>. </summary>
    public long? NotificationChatId { get; init; }

    /// <summary>
    /// When the chat of notifications is a group with topics: the topic (message thread id) of each category, by name -
    /// e.g. <c>"Security": 2</c>. A category without a topic goes to the general one.
    /// </summary>
    public Dictionary<string, int> NotificationTopics { get; init; } = [];

    public TelegramProxyOptions? Proxy { get; init; }

    /// <summary> Local runs: Telegram is never contacted, the menu is available at <c>/debug/menu</c>. </summary>
    public bool UseFakeBot { get; init; }
}

public sealed class TelegramProxyOptions
{
    public string Address { get; init; } = "";
    public string? Username { get; init; }
    public string? Password { get; init; }
}

internal sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        var errors = new List<string>();

        if (options.AllowedUsers.Count == 0)
            errors.Add("Telegram:AllowedUsers is empty - nobody could use the bot (an empty list must not mean 'everybody')");

        if (!options.UseFakeBot && string.IsNullOrWhiteSpace(options.BotToken))
            errors.Add("Telegram:BotToken is required (or Telegram:UseFakeBot for local runs)");

        // master always notified the owner; a forgotten setting must not switch that off silently.
        if (!options.UseFakeBot && options.NotificationChatId == null)
            errors.Add("Telegram:NotificationChatId is required: the owner gets the events of the house");

        foreach (var (category, topic) in options.NotificationTopics)
        {
            if (!Enum.TryParse<NotificationCategory>(category, out var known) || !Enum.IsDefined(known) || known.ToString() != category)
                errors.Add($"Telegram:NotificationTopics: unknown category '{category}', expected one of {string.Join(", ", Enum.GetNames<NotificationCategory>())}");
            if (topic <= 0)
                errors.Add($"Telegram:NotificationTopics:{category}: the topic id must be positive");
        }

        if (options.Proxy != null && !Uri.TryCreate(options.Proxy.Address, UriKind.Absolute, out _))
            errors.Add("Telegram:Proxy:Address must be a URL");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
