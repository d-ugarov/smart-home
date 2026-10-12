namespace SmartHome.Core.Meters;

/// <summary> Settings of the meters (appsettings); the meters themselves and their pulse weights are in the house files. </summary>
public sealed class MeterSettings
{
    public const string Section = "Meters";

    /// <summary>
    /// Off by default: the requests of the meter inputs are reported as in <c>master</c>, nothing is counted or saved, the
    /// menu has no page of the meters.
    /// </summary>
    public bool Enabled { get; init; }
}
