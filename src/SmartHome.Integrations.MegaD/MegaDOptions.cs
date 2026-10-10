namespace SmartHome.Integrations.MegaD;

/// <summary> MegaD-2561 controllers and the entities bound to their ports. </summary>
public sealed class MegaDOptions
{
    public const string Section = "MegaD";

    /// <summary> By controller id - the number in <c>/megad/{id}</c> the controller calls the server with. </summary>
    public Dictionary<string, MegaDControllerOptions> Controllers { get; init; } = [];

    /// <summary> Local runs without hardware: the controllers are simulated in memory. </summary>
    public bool UseSimulator { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed class MegaDControllerOptions
{
    public string Name { get; init; } = "";

    /// <summary> <c>http://192.168.0.11</c>. </summary>
    public string Address { get; init; } = "";

    /// <summary> A secret: kept in appsettings of the server, not in the house files. </summary>
    public string Password { get; init; } = "";

    /// <summary>
    /// Inputs whose scenario belongs to the controller alone (leak protection). No buttons and no rules on them: the
    /// server always answers 204 and the controller runs its scenario.
    /// </summary>
    public List<int> HardwareInputs { get; init; } = [];

    /// <summary> Port number → entity (a switch on an output, a button or a sensor on an input). </summary>
    public Dictionary<int, string> Ports { get; init; } = [];

    public List<MegaDValveOptions> Valves { get; init; } = [];
}

/// <summary>
/// A pair of valves driven by the scenarios of two hardware inputs (<c>?pt=N&amp;cmd=d</c>). The server never commands
/// their relays directly: switching both lines at once can break the valve drive.
/// </summary>
public sealed class MegaDValveOptions
{
    public string Entity { get; init; } = "";
    public int OpenInput { get; init; }
    public int CloseInput { get; init; }
    public int OpenRelay { get; init; }
    public int CloseRelay { get; init; }
}
