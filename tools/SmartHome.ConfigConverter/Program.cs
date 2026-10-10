using System.Text;

namespace SmartHome.ConfigConverter;

// Converts the config of master (DevicesConfig, HomeMapConfig, AutomationConfig) into the house files of the new
// server. One-off tool for the move to the new core: after release 1 the house files are edited by hand.
//
//   dotnet run --project tools/SmartHome.ConfigConverter -- <master appsettings.json> <map.json> <output directory> [--with-passwords]
internal static class Cli
{
    public static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: <master appsettings.json> <map.json> <output directory> [--with-passwords]");
            return 2;
        }

        var master = MasterConfig.Parse(File.ReadAllText(args[0]));
        var map = ConversionMap.Parse(File.ReadAllText(args[1]));
        var result = Converter.Convert(master, map, withPasswords: args.Contains("--with-passwords"));

        foreach (var note in result.Notes)
            Console.WriteLine($"note: {note}");

        foreach (var error in result.Errors)
            Console.Error.WriteLine($"error: {error}");

        if (result.Errors.Count > 0)
            return 1;

        const string header = "Converted from the config of master by SmartHome.ConfigConverter; edited by hand from now on.";

        Directory.CreateDirectory(args[2]);
        Write("devices.json", CompactJson.Write(result.Devices, $"{header}\nControllers and what is bound to their ports. Passwords are secrets: appsettings of the server."));
        Write("entities.json", CompactJson.Write(result.Entities, $"{header}\nEntities of the house and the bot menu."));
        Write("automations.json", CompactJson.Write(result.Automations, $"{header}\nInput rules: a press of a button → conditions on the switches → commands."));

        Console.WriteLine($"Written to {Path.GetFullPath(args[2])}");
        return 0;

        void Write(string name, string content) => File.WriteAllText(Path.Combine(args[2], name), content, new UTF8Encoding(false));
    }
}
