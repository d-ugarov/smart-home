namespace SmartHome.Core.Automations;

/// <summary> A change of the next run of a schedule rule from the menu, for this run only. </summary>
public enum RunChange
{
    EarlierHour,
    EarlierQuarter,
    LaterQuarter,
    LaterHour,
    Skip,
    Unskip,

    /// <summary> Back to the time of the schedule, not skipped. </summary>
    Reset,
}
