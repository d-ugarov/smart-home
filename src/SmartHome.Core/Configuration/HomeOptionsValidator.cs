using Microsoft.Extensions.Options;
using SmartHome.Core.Entities;
using SmartHome.Core.Menu;
using System.Text.RegularExpressions;

namespace SmartHome.Core.Configuration;

/// <summary> The server does not start with a broken house config: every error is listed at startup. </summary>
public sealed partial class HomeOptionsValidator : IValidateOptions<HomeOptions>
{
    /// <summary> Pauses of input rules delay the answer to the controller; longer waits are automations. </summary>
    public static readonly TimeSpan MaxRuleDelay = TimeSpan.FromSeconds(5);

    public ValidateOptionsResult Validate(string? name, HomeOptions options)
    {
        var errors = new List<string>();
        var entities = ValidateEntities(options.Entities, errors);

        ValidateMenu(options.Menu, entities, errors);
        ValidateRules(options.Rules, entities, errors);
        AutomationOptionsValidator.Validate(options.Automations, entities, errors);
        ValidateTimeZone(options, errors);

        if (options.StatePollInterval < TimeSpan.Zero)
            errors.Add($"{HomeOptions.Section}:{nameof(HomeOptions.StatePollInterval)} must not be negative");

        if (options.ValvePulseTimeout <= TimeSpan.Zero)
            errors.Add($"{HomeOptions.Section}:{nameof(HomeOptions.ValvePulseTimeout)} must be positive");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static Dictionary<string, EntityOptions> ValidateEntities(List<EntityOptions> options, List<string> errors)
    {
        var result = new Dictionary<string, EntityOptions>();

        foreach (var entity in options)
        {
            if (!EntityIdRegex().IsMatch(entity.Id))
                errors.Add($"Entity id '{entity.Id}' must look like 'class.name' (lowercase latin letters, digits, '_')");
            else if (!result.TryAdd(entity.Id, entity))
                errors.Add($"Entity '{entity.Id}' is declared twice");

            if (string.IsNullOrWhiteSpace(entity.Name))
                errors.Add($"Entity '{entity.Id}': Name is required");

            if (entity.Type == null)
                errors.Add($"Entity '{entity.Id}': Type is required");

            if (entity.Icons != null && (string.IsNullOrEmpty(entity.Icons.On) || string.IsNullOrEmpty(entity.Icons.Off)))
                errors.Add($"Entity '{entity.Id}': Icons need both On and Off");

            if (entity.AutoOffAfter != null && entity.Type != EntityType.Switch)
                errors.Add($"Entity '{entity.Id}': AutoOffAfter is only for switches");
            else if (entity.AutoOffAfter <= TimeSpan.Zero)
                errors.Add($"Entity '{entity.Id}': AutoOffAfter must be positive");

            if (entity.Type != EntityType.Group && entity.Members.Count > 0)
                errors.Add($"Entity '{entity.Id}': Members are only for groups");
        }

        foreach (var group in options.Where(x => x.Type == EntityType.Group))
        {
            if (group.Members.Count == 0)
                errors.Add($"Group '{group.Id}': Members are required");

            if (group.Members.Distinct().Count() != group.Members.Count)
                errors.Add($"Group '{group.Id}': a member is listed twice");

            foreach (var member in group.Members)
                ValidateSwitch(member, "member", $"Group '{group.Id}'", result, errors);
        }

        return result;
    }

    private static void ValidateMenu(List<MenuPageOptions> pages, Dictionary<string, EntityOptions> entities, List<string> errors)
    {
        var pageIds = new HashSet<string>();

        void ValidatePage(MenuPageOptions page)
        {
            if (!PageIdRegex().IsMatch(page.Id))
                errors.Add($"Menu page id '{page.Id}' must consist of lowercase latin letters, digits and '_'");
            else if (!pageIds.Add(page.Id))
                errors.Add($"Menu page '{page.Id}' is declared twice");

            if (string.IsNullOrWhiteSpace(page.Title))
                errors.Add($"Menu page '{page.Id}': Title is required");

            foreach (var item in page.Items)
            {
                if (!entities.TryGetValue(item.Entity, out var entity))
                {
                    errors.Add($"Menu page '{page.Id}': unknown entity '{item.Entity}'");
                    continue;
                }

                if (entity.Type == EntityType.Group)
                    errors.Add($"Menu page '{page.Id}': group '{item.Entity}' has no state of its own to show, list its switches");

                if (entity.Type == EntityType.Valve && item.Valve == null)
                    errors.Add($"Menu page '{page.Id}': valve '{item.Entity}' needs Valve (Open or Close)");

                if (entity.Type != EntityType.Valve && item.Valve != null)
                    errors.Add($"Menu page '{page.Id}': Valve is set for '{item.Entity}', which is not a valve");

                MenuAction action = item.Valve is { } command
                    ? new MenuAction.RunValve(page.Id, item.Entity, command)
                    : new MenuAction.Toggle(page.Id, item.Entity);

                if (MenuAction.ByteLength(action) > MenuAction.MaxLength)
                    errors.Add($"Menu page '{page.Id}': ids of the page and '{item.Entity}' are too long for a Telegram button (64 bytes)");
            }

            foreach (var subPage in page.Pages)
                ValidatePage(subPage);
        }

        foreach (var page in pages)
            ValidatePage(page);

        // "now on" lists can toggle any switch
        var longestSwitch = entities.Values.Where(x => x.Type == EntityType.Switch).MaxBy(x => x.Id.Length);
        foreach (var page in Flatten(pages).Where(x => x.ListSwitches != null))
        {
            if (longestSwitch != null && MenuAction.ByteLength(new MenuAction.Toggle(page.Id, longestSwitch.Id)) > MenuAction.MaxLength)
                errors.Add($"Menu page '{page.Id}': ids of the page and '{longestSwitch.Id}' are too long for a Telegram button (64 bytes)");
        }
    }

    private static void ValidateRules(List<InputRuleOptions> rules, Dictionary<string, EntityOptions> entities, List<string> errors)
    {
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var prefix = $"Rule #{i + 1} ({rule.Button})";

            if (!entities.TryGetValue(rule.Button, out var button))
                errors.Add($"{prefix}: unknown button");
            else if (button.Type != EntityType.Button)
                errors.Add($"{prefix}: '{rule.Button}' is a {button.Type}, rules are triggered by buttons");

            if (rule.Presses.Count == 0)
                errors.Add($"{prefix}: Presses are required");

            if (rule.Response == null)
                errors.Add($"{prefix}: Response is required (Handled or ControllerActs)");

            if (rule.Condition is { } condition)
            {
                if (condition.Type == null)
                    errors.Add($"{prefix}: condition Type is required");

                var needsState = condition.Type is ConditionType.AllAre or ConditionType.AnyIs or ConditionType.NoneIs;
                if (needsState && condition.State == null)
                    errors.Add($"{prefix}: condition {condition.Type} needs State");
                if (!needsState && condition.State != null)
                    errors.Add($"{prefix}: condition {condition.Type} compares the switches with each other, State is not used");

                if (condition.Entities.Count == 0)
                    errors.Add($"{prefix}: condition needs Entities");

                foreach (var id in condition.Entities)
                    ValidateSwitch(id, "condition", prefix, entities, errors);
            }

            if (rule.Actions.Count == 0)
                errors.Add($"{prefix}: Actions are required");

            foreach (var action in rule.Actions)
            {
                ValidateSwitch(action.Entity, "action", prefix, entities, errors);

                if (action.Command == null)
                    errors.Add($"{prefix}: action '{action.Entity}' needs Command");

                if (action.DelayBefore < TimeSpan.Zero || action.DelayBefore > MaxRuleDelay ||
                    action.DelayAfter < TimeSpan.Zero || action.DelayAfter > MaxRuleDelay)
                    errors.Add($"{prefix}: delays of '{action.Entity}' must be within 0..{MaxRuleDelay.TotalSeconds:0} s");
            }
        }
    }

    private static void ValidateTimeZone(HomeOptions options, List<string> errors)
    {
        var key = $"{HomeOptions.Section}:{nameof(HomeOptions.TimeZone)}";

        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            if (options.Automations.SelectMany(x => x.Rules).Any(x => x.Trigger?.At != null))
                errors.Add($"{key} is required: schedules run by the local time of the house");
        }
        else if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _))
        {
            errors.Add($"{key}: unknown time zone '{options.TimeZone}', an IANA id is expected, e.g. Europe/Moscow");
        }
    }

    internal static void ValidateSwitch(string id, string role, string prefix, Dictionary<string, EntityOptions> entities, List<string> errors)
    {
        if (!entities.TryGetValue(id, out var entity))
            errors.Add($"{prefix}: unknown {role} entity '{id}'");
        else if (entity.Type != EntityType.Switch)
            errors.Add($"{prefix}: {role} entity '{id}' is a {entity.Type}, only switches are allowed");
    }

    internal static IEnumerable<MenuPageOptions> Flatten(IEnumerable<MenuPageOptions> pages) =>
        pages.SelectMany(x => Flatten(x.Pages).Prepend(x));

    [GeneratedRegex("^[a-z][a-z0-9_]*\\.[a-z0-9_]+$")]
    private static partial Regex EntityIdRegex();

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex PageIdRegex();
}
