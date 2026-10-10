using Microsoft.Extensions.Configuration.Json;

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

    /// <summary>
    /// The files go below every other source: appsettings, environment variables and the command line override the
    /// house description, never the other way round.
    /// </summary>
    public static IConfigurationBuilder AddHomeFiles(this ConfigurationManager configuration)
    {
        var directory = configuration[DirectoryKey] ?? DefaultDirectory;
        var fileProvider = configuration.GetFileProvider();

        for (var i = 0; i < Names.Length; i++)
        {
            var source = new JsonConfigurationSource
                         {
                             Path = Path.Combine(directory, Names[i]),
                             Optional = false,
                             ReloadOnChange = false,
                             FileProvider = Path.IsPathRooted(directory) ? null : fileProvider,
                         };
            source.ResolveFileProvider();

            configuration.Sources.Insert(i, source);
        }

        return configuration;
    }
}
