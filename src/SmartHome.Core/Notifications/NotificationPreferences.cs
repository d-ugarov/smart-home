using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartHome.Core.Common;
using SmartHome.Core.Storage;

namespace SmartHome.Core.Notifications;

public enum NotificationCategory
{
    /// <summary> Leaks, valves, the cleaning of bathrooms. </summary>
    Security,

    /// <summary> Requests of the controllers and commands of the input rules. </summary>
    Events,

    /// <summary> Commands of automations and schedules. </summary>
    Automation,

    Meters,

    /// <summary> Restarts of the controllers, the server not fully working, commands that failed. </summary>
    System,
}

public enum NotificationMode
{
    On,

    /// <summary> Delivered without a sound: kept in the chat, the phone stays quiet. </summary>
    Silent,

    Off,
}

/// <summary>
/// How the owner wants each category: chosen in the menu, saved in the storage and restored by
/// <see cref="HomeStartup"/>. <see cref="NotificationCategory.Security"/> and <see cref="NotificationCategory.System"/>
/// are always on: a leak or a server that does not work must not go unnoticed because of a choice made for the noise.
/// </summary>
public sealed class NotificationPreferences(IHomeStorage storage, IOptions<NotificationSettings> settings, ILogger<NotificationPreferences> logger)
{
    public static readonly IReadOnlyList<NotificationCategory> Adjustable = [NotificationCategory.Events, NotificationCategory.Automation, NotificationCategory.Meters];

    private const string KeyPrefix = "notifications:";

    private readonly Lock sync = new();
    private readonly Dictionary<NotificationCategory, NotificationMode> modes = [];

    // One change at a time with its write: two quick presses are saved in the order they were made.
    private readonly SemaphoreSlim changes = new(1, 1);

    public bool IsAvailable => settings.Value.CategoriesEnabled;

    public NotificationMode ModeOf(NotificationCategory category)
    {
        if (!IsAvailable || !Adjustable.Contains(category))
            return NotificationMode.On;

        lock (sync)
            return modes.GetValueOrDefault(category, NotificationMode.On);
    }

    /// <summary> Kept in memory when the storage fails: works until the restart. </summary>
    public async Task SetAsync(NotificationCategory category, NotificationMode mode, CancellationToken cancellationToken)
    {
        if (!IsAvailable || !Adjustable.Contains(category))
            throw new InvalidOperationException($"Notifications of {category} cannot be changed");

        await changes.WaitAsync(cancellationToken);
        try
        {
            lock (sync)
                modes[category] = mode;

            logger.LogInformation("Notifications of {Category}: {Mode}", category, mode);
            await InvokeOperations.InvokeAsync(logger, $"Save the notifications of {category}",
                                               () => storage.SaveSettingAsync(Key(category), mode == NotificationMode.On ? null : mode.ToString(), CancellationToken.None));
        }
        finally
        {
            changes.Release();
        }
    }

    /// <summary> Also when <see cref="IsAvailable"/> is off: turned on again, the categories are as the owner left them. </summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        foreach (var category in Adjustable)
        {
            if (await storage.LoadSettingAsync(Key(category), cancellationToken) is not { } text ||
                !Enum.TryParse<NotificationMode>(text, out var mode) || !Enum.IsDefined(mode))
                continue;

            lock (sync)
                modes[category] = mode;

            logger.LogInformation("Notifications of {Category} restored: {Mode}", category, mode);
        }
    }

    private static string Key(NotificationCategory category) => KeyPrefix + category;
}
