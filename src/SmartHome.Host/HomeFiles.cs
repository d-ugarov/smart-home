namespace SmartHome.Host;

/// <summary>
/// The house is described by three files in <c>HomeConfigDirectory</c> (relative to the working directory):
/// controllers and port bindings, entities and the menu, rules. Settings and secrets stay in appsettings.json.
/// </summary>
public static class HomeFiles
{
    public const string DirectoryKey = "HomeConfigDirectory";
    public const string DefaultDirectory = "home";

    public static readonly string[] Names = ["devices.json", "entities.json", "automations.json"];

    public static IConfigurationBuilder AddHomeFiles(this ConfigurationManager configuration)
    {
        var directory = configuration[DirectoryKey] ?? DefaultDirectory;

        foreach (var name in Names)
            configuration.AddJsonFile(Path.Combine(directory, name), optional: false, reloadOnChange: false);

        return configuration;
    }
}
