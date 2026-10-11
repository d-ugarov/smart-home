namespace SmartHome.Core.Valves;

/// <summary> Settings of the cleaning of bathrooms (appsettings); the buttons are in the house files. </summary>
public sealed class CleaningSettings
{
    public const string Section = "Cleaning";

    /// <summary>
    /// Off by default: the server never holds back the leak protection of the controller, as <c>master</c>. The cleaning
    /// buttons of the menu are hidden then.
    /// </summary>
    public bool Enabled { get; init; }
}
