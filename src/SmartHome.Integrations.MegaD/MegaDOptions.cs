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

    /// <summary>
    /// <c>/megad/{id}</c> answers only the address of controller <c>id</c> (MegaD sends no password, its address is all there
    /// is): a stranger in the network can neither press buttons nor hold back a leak sensor. Anything else gets 403 and is not
    /// handled. With <see cref="UseSimulator"/> the requests of this machine are taken as well. Off - anyone, as in
    /// <c>master</c>.
    /// </summary>
    public bool AcceptOnlyControllers { get; init; } = true;

    /// <summary>
    /// Steps of the counter of a meter input (<c>cnt</c>) per pulse of the meter. Meter inputs are in the P&amp;R mode: the
    /// counter steps on the closing and on the opening of the contact (the request log, 2026-10-11: closing - odd
    /// <c>cnt</c>, opening - the next, even one).
    /// </summary>
    public int MeterCountsPerPulse { get; init; } = 2;
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

    /// <summary> Port number → entity (a switch on an output, a button, a sensor or a pulse meter on an input). </summary>
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
