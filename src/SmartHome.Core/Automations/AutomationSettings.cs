using Microsoft.Extensions.Options;

namespace SmartHome.Core.Automations;

/// <summary> Settings of automations (appsettings); the automations themselves are in the house files. </summary>
public sealed class AutomationSettings
{
    public const string Section = "Automation";

    /// <summary> Off by default: the house behaves as <c>master</c>, automations are turned on in the house when ready. </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Automations that run at times of day; off by default, turned on in the house when the schedules of the controllers
    /// are to be moved to the server. Needs <see cref="Enabled"/>.
    /// </summary>
    public bool SchedulesEnabled { get; init; }

    /// <summary>
    /// A scheduled action that is late by more than this - the server was down - is dropped: the house has lived without
    /// it for long. Up to this it is done right after the start. The auto-off is never dropped: the switch is still on.
    /// </summary>
    public TimeSpan MaxLateness { get; init; } = TimeSpan.FromHours(12);
}

internal sealed class AutomationSettingsValidator : IValidateOptions<AutomationSettings>
{
    public ValidateOptionsResult Validate(string? name, AutomationSettings options) =>
        options.MaxLateness > TimeSpan.Zero
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{AutomationSettings.Section}:{nameof(AutomationSettings.MaxLateness)} must be positive");
}
