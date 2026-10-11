using SmartHome.Core.Automations;
using SmartHome.Core.Entities;
using SmartHome.Core.Menu;
using System.Text.RegularExpressions;

namespace SmartHome.Core.Configuration;

/// <summary> Startup check of <see cref="HomeOptions.Automations"/>, a part of <see cref="HomeOptionsValidator"/>. </summary>
internal static partial class AutomationOptionsValidator
{
    public static void Validate(List<AutomationOptions> automations, Dictionary<string, EntityOptions> entities, List<string> errors)
    {
        var ids = new HashSet<string>();
        var scheduledKeys = automations.SelectMany(x => x.Rules).SelectMany(x => x.Actions).Select(x => x.Schedule).OfType<string>().ToHashSet();

        foreach (var automation in automations)
        {
            var prefix = $"Automation '{automation.Id}'";

            if (!IdRegex().IsMatch(automation.Id))
                errors.Add($"Automation id '{automation.Id}' must consist of lowercase latin letters, digits and '_'");
            else if (automation.Id == Automation.AutoOffId)
                errors.Add($"Automation id '{Automation.AutoOffId}' is reserved for AutoOffAfter of the switches");
            else if (!ids.Add(automation.Id))
                errors.Add($"{prefix} is declared twice");
            else if (MenuAction.ByteLength(new MenuAction.ToggleAutomation(automation.Id)) > MenuAction.MaxLength)
                errors.Add($"{prefix}: the id is too long for a Telegram button (64 bytes)");

            if (string.IsNullOrWhiteSpace(automation.Name))
                errors.Add($"{prefix}: Name is required");

            if (automation.Rules.Count == 0)
                errors.Add($"{prefix}: Rules are required");

            var timed = automation.Rules.Count(x => x.Trigger?.At != null);
            if (timed > 0 && timed < automation.Rules.Count)
                errors.Add($"{prefix}: rules at times of day (a schedule) and rules on states are separate automations");
            else if (timed > 0)
                ValidateScheduleButtons(automation, prefix, errors);

            for (var i = 0; i < automation.Rules.Count; i++)
                ValidateRule(automation.Rules[i], $"{prefix}, rule #{i + 1}", entities, scheduledKeys, errors);
        }
    }

    private static void ValidateRule(AutomationRuleOptions rule, string prefix, Dictionary<string, EntityOptions> entities, HashSet<string> scheduledKeys,
                                     List<string> errors)
    {
        var isTimed = rule.Trigger?.At != null;

        if (rule.Trigger is not { } trigger)
        {
            errors.Add($"{prefix}: Trigger is required");
        }
        else if (trigger.At is { } at)
        {
            if (trigger.Entity != null || trigger.To != null || trigger.For != null)
                errors.Add($"{prefix}: a trigger is either At (+ Days) or Entity + To (+ For)");

            if (at != new TimeOnly(at.Hour, at.Minute))
                errors.Add($"{prefix}: trigger At must be whole minutes");

            if (trigger.Days.Any(x => !Enum.IsDefined(x)) || trigger.Days.Distinct().Count() != trigger.Days.Count)
                errors.Add($"{prefix}: trigger Days must be distinct days of the week (Monday ... Sunday)");
        }
        else
        {
            ValidateState(trigger.Entity ?? "", "trigger", prefix, entities, errors);

            if (trigger.Days.Count > 0)
                errors.Add($"{prefix}: Days are only for a trigger At");

            if (trigger.To == null)
                errors.Add($"{prefix}: trigger needs To (On or Off)");

            if (trigger.For <= TimeSpan.Zero)
                errors.Add($"{prefix}: trigger For must be positive");
        }

        foreach (var condition in rule.Conditions)
        {
            var kinds = (condition.Entity != null || condition.Is != null ? 1 : 0) +
                        (condition.PreviousStateLongerThan != null ? 1 : 0) +
                        (condition.PreviousStateNotLongerThan != null ? 1 : 0);

            if (kinds != 1)
            {
                errors.Add($"{prefix}: a condition is one of: Entity + Is, PreviousStateLongerThan, PreviousStateNotLongerThan");
                continue;
            }

            if (condition.Entity != null || condition.Is != null)
            {
                ValidateState(condition.Entity ?? "", "condition", prefix, entities, errors);
                if (condition.Is == null)
                    errors.Add($"{prefix}: condition '{condition.Entity}' needs Is (On or Off)");
                continue;
            }

            if (condition.PreviousStateLongerThan <= TimeSpan.Zero || condition.PreviousStateNotLongerThan <= TimeSpan.Zero)
                errors.Add($"{prefix}: the duration of the previous state must be positive");

            if (rule.Trigger?.For != null || isTimed)
                errors.Add($"{prefix}: the previous state is known only for a trigger by a state, without For");
        }

        if (rule.Actions.Count == 0)
            errors.Add($"{prefix}: Actions are required");

        foreach (var action in rule.Actions)
        {
            if (isTimed && (action.Schedule != null || action.Cancel != null))
            {
                errors.Add($"{prefix}: a schedule only sends commands, without Schedule and Cancel");
                continue;
            }

            if (action.Cancel is { } cancel)
            {
                if (action.Entity != null || action.Command != null || action.Schedule != null || action.Delay != null)
                    errors.Add($"{prefix}: Cancel '{cancel}' takes nothing else");
                else if (!scheduledKeys.Contains(cancel))
                    errors.Add($"{prefix}: Cancel '{cancel}': no automation schedules this key");
                continue;
            }

            HomeOptionsValidator.ValidateSwitch(action.Entity ?? "", "action", prefix, entities, errors);

            if (action.Command == null)
                errors.Add($"{prefix}: action '{action.Entity}' needs Command (On or Off)");

            if (action.Schedule is { } key)
            {
                if (!KeyRegex().IsMatch(key))
                    errors.Add($"{prefix}: Schedule key '{key}' must consist of lowercase latin letters, digits, '_' and '.'");
                if (action.Delay is not { } delay || delay <= TimeSpan.Zero)
                    errors.Add($"{prefix}: Schedule '{key}' needs a positive Delay");
            }
            else if (action.Delay != null)
            {
                errors.Add($"{prefix}: Delay of '{action.Entity}' needs a Schedule key, which a later action can cancel");
            }
        }
    }

    private static void ValidateScheduleButtons(AutomationOptions automation, string prefix, List<string> errors)
    {
        var longest = Enum.GetValues<RunChange>()
                          .Max(x => MenuAction.ByteLength(new MenuAction.ChangeRun(automation.Id, automation.Rules.Count - 1, DateTimeOffset.MaxValue, x)));
        if (longest > MenuAction.MaxLength)
            errors.Add($"{prefix}: the id is too long for a Telegram button (64 bytes)");
    }

    /// <summary> Triggers and conditions watch switches and groups. </summary>
    private static void ValidateState(string id, string role, string prefix, Dictionary<string, EntityOptions> entities, List<string> errors)
    {
        if (!entities.TryGetValue(id, out var entity))
            errors.Add($"{prefix}: unknown {role} entity '{id}'");
        else if (entity.Type is not (EntityType.Switch or EntityType.Group))
            errors.Add($"{prefix}: {role} entity '{id}' is a {entity.Type}, only switches and groups are allowed");
    }

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[a-z0-9_.]+$")]
    private static partial Regex KeyRegex();
}
