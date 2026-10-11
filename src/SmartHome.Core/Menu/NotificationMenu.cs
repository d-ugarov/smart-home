using SmartHome.Core.Notifications;

namespace SmartHome.Core.Menu;

/// <summary> The page of notifications: a button per category that can be turned down; a press moves it on → silent → off → on. </summary>
public sealed class NotificationMenu(NotificationPreferences preferences)
{
    public bool IsAvailable => preferences.IsAvailable;

    public MenuPage Render()
    {
        var buttons = NotificationPreferences.Adjustable.Select(Button).ToList();

        return new MenuPage(MenuTexts.Notifications,
                            [
                                "Security (leaks, valves, the cleaning) and System (restarts of the controllers, failed commands, the server) always arrive, " +
                                "with a sound.",
                                "Events — presses and commands of the switches; Automation — automations and schedules; Meters — the meters. " +
                                "A press switches the category: on → silent → off.",
                            ],
                            buttons,
                            [new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);
    }

    public async Task<MenuPage> ChangeAsync(NotificationCategory category, NotificationMode mode, CancellationToken cancellationToken)
    {
        if (NotificationPreferences.Adjustable.Contains(category))
            await preferences.SetAsync(category, mode, cancellationToken);

        return Render();
    }

    private MenuButton Button(NotificationCategory category)
    {
        var mode = preferences.ModeOf(category);
        var text = mode switch
        {
            NotificationMode.On => $"🔔 {category} — on",
            NotificationMode.Silent => $"🔕 {category} — silent",
            _ => $"🚫 {category} — off",
        };

        return new MenuButton(text, new MenuAction.ChangeNotifications(category, Next(mode)));
    }

    private static NotificationMode Next(NotificationMode mode) => mode switch
    {
        NotificationMode.On => NotificationMode.Silent,
        NotificationMode.Silent => NotificationMode.Off,
        _ => NotificationMode.On,
    };
}
