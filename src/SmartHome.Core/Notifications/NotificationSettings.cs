namespace SmartHome.Core.Notifications;

/// <summary> Settings of the notifications (appsettings); the categories turned down are chosen in the menu. </summary>
public sealed class NotificationSettings
{
    public const string Section = "Notifications";

    /// <summary>
    /// Off by default: every category is sent with sound, as <c>master</c>, whatever was chosen before; the menu has no
    /// page of notifications.
    /// </summary>
    public bool CategoriesEnabled { get; init; }
}
