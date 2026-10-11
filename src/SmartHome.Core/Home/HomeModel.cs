using Microsoft.Extensions.Options;
using SmartHome.Core.Automations;
using SmartHome.Core.Configuration;
using SmartHome.Core.Entities;
using SmartHome.Core.Rules;
using System.Text;

namespace SmartHome.Core.Home;

/// <summary> The house built from the validated <see cref="HomeOptions"/>: entities, menu tree, input rules and automations. </summary>
public sealed class HomeModel
{
    private readonly Dictionary<string, Entity> entitiesById;
    private readonly Dictionary<string, MenuPageNode> pagesById = [];
    private readonly Dictionary<string, IReadOnlyList<InputRule>> rulesByButton;
    private readonly Dictionary<string, IReadOnlyList<Entity>> readByButton;
    private readonly Dictionary<string, IReadOnlyList<Entity>> groupsByMember;
    private readonly Dictionary<string, Automation> automationsById;
    private readonly Dictionary<string, IReadOnlyList<AutomationRule>> rulesByTrigger;

    public HomeModel(IOptions<HomeOptions> options) : this(options.Value)
    {
    }

    public HomeModel(HomeOptions options)
    {
        var plain = options.Entities
                           .Where(x => x.Type != EntityType.Group)
                           .Select(x => new Entity(x.Id, x.Name, x.Type!.Value, Icons(x)) {AutoOffAfter = x.AutoOffAfter})
                           .ToDictionary(x => x.Id);

        Entities = options.Entities
                          .Select(x => x.Type == EntityType.Group
                                           ? new Entity(x.Id, x.Name, EntityType.Group, Icons(x)) {Members = x.Members.Select(m => plain[m]).ToList()}
                                           : plain[x.Id])
                          .ToList();
        entitiesById = Entities.ToDictionary(x => x.Id);

        groupsByMember = Entities.Where(x => x.Type == EntityType.Group)
                                 .SelectMany(g => g.Members.Select(m => (Member: m.Id, Group: g)))
                                 .GroupBy(x => x.Member)
                                 .ToDictionary(x => x.Key, x => (IReadOnlyList<Entity>)x.Select(y => y.Group).ToList());

        Automations = options.Automations.Select(CreateAutomation).ToList();
        automationsById = Automations.ToDictionary(x => x.Id);
        rulesByTrigger = Automations.SelectMany(x => x.Rules)
                                    .Where(x => x.Trigger is AutomationTrigger.StateChange)
                                    .GroupBy(x => ((AutomationTrigger.StateChange)x.Trigger).Entity.Id)
                                    .ToDictionary(x => x.Key, x => (IReadOnlyList<AutomationRule>)x.ToList());
        TimeZone = string.IsNullOrWhiteSpace(options.TimeZone) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);

        Dashboard = options.Menu.Select(x => CreatePage(x, parent: null)).ToList();

        var rules = options.Rules.Select(CreateRule).ToList();
        rulesByButton = rules.GroupBy(x => x.Button.Id).ToDictionary(x => x.Key, x => (IReadOnlyList<InputRule>)x.ToList());

        // One reading per press: the switches of the conditions and of the actions of all rules of the button.
        readByButton = rulesByButton.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<Entity>)x.Value.SelectMany(r => r.Condition?.Switches ?? [])
                                       .Concat(x.Value.SelectMany(r => r.Actions.Select(a => a.Target)))
                                       .Distinct()
                                       .ToList());

        Switches = Entities.Where(x => x.Type == EntityType.Switch).ToList();
        AutoOffSwitches = Switches.Where(x => x.AutoOffAfter != null).ToList();
    }

    /// <summary> In the order of the config. </summary>
    public IReadOnlyList<Entity> Entities { get; }

    public IReadOnlyList<Entity> Switches { get; }

    /// <summary> Switches with <see cref="Entity.AutoOffAfter"/>. </summary>
    public IReadOnlyList<Entity> AutoOffSwitches { get; }

    public IReadOnlyList<MenuPageNode> Dashboard { get; }

    /// <summary> In the order of the config, schedules among them. </summary>
    public IReadOnlyList<Automation> Automations { get; }

    /// <summary> Schedules run by it; UTC when the house has none. </summary>
    public TimeZoneInfo TimeZone { get; }

    public Entity? FindEntity(string id) => entitiesById.GetValueOrDefault(id);

    public IReadOnlyList<Entity> GroupsOf(string memberId) => groupsByMember.GetValueOrDefault(memberId) ?? [];

    public Automation? FindAutomation(string id) => automationsById.GetValueOrDefault(id);

    /// <summary> Automation rules triggered by a change of this switch or group. </summary>
    public IReadOnlyList<AutomationRule> RulesTriggeredBy(string entityId) => rulesByTrigger.GetValueOrDefault(entityId) ?? [];

    public MenuPageNode? FindPage(string id) => pagesById.GetValueOrDefault(id);

    public IReadOnlyList<InputRule> RulesOf(string buttonId) => rulesByButton.GetValueOrDefault(buttonId) ?? [];

    public IReadOnlyList<Entity> EntitiesReadOnPress(string buttonId) => readByButton.GetValueOrDefault(buttonId) ?? [];

    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"Home: {Entities.Count} entities, {rulesByButton.Values.Sum(x => x.Count)} rules on {rulesByButton.Count} buttons, {pagesById.Count} menu pages, " +
                        $"{Automations.Count} automations ({Automations.Count(x => x.IsSchedule)} schedules), auto-off of {AutoOffSwitches.Count} switches");

        foreach (var group in Entities.GroupBy(x => x.Type))
            text.AppendLine($"  {group.Key}: {string.Join(", ", group.Select(x => x.Id))}");

        return text.ToString();
    }

    private MenuPageNode CreatePage(MenuPageOptions options, MenuPageNode? parent)
    {
        var page = new MenuPageNode(options.Id, options.Title, parent, options.ListSwitches);

        page.Items = options.Items.Select(x => new MenuItem(entitiesById[x.Entity], string.IsNullOrEmpty(x.Label) ? null : x.Label, x.Valve)).ToList();
        page.Pages = options.Pages.Select(x => CreatePage(x, page)).ToList();

        pagesById.Add(page.Id, page);
        return page;
    }

    private InputRule CreateRule(InputRuleOptions options)
    {
        var condition = options.Condition is { } c
            ? new RuleCondition(c.Type!.Value, c.State, c.Entities.Select(x => entitiesById[x]).ToList())
            : null;

        return new InputRule(entitiesById[options.Button],
                             options.Presses.ToHashSet(),
                             condition,
                             options.Actions.Select(x => new RuleAction(entitiesById[x.Entity], x.Command!.Value, x.DelayBefore, x.DelayAfter)).ToList(),
                             options.Response!.Value,
                             options.IsFinal);
    }

    private Automation CreateAutomation(AutomationOptions options)
    {
        var automation = new Automation(options.Id, options.Name);

        automation.Rules = options.Rules.Select((rule, index) => new AutomationRule(
                                                    automation,
                                                    index,
                                                    CreateTrigger(rule.Trigger!),
                                                    rule.Conditions.Select(CreateCondition).ToList(),
                                                    rule.Actions.Select(CreateAction).ToList()))
                                      .ToList();

        return automation;
    }

    private AutomationTrigger CreateTrigger(TriggerOptions options) =>
        options.At is { } at
            ? new AutomationTrigger.TimeOfDay(at, options.Days.ToHashSet())
            : new AutomationTrigger.StateChange(entitiesById[options.Entity!], options.To!.Value, options.For);

    private AutomationCondition CreateCondition(AutomationConditionOptions options) => options switch
    {
        {Entity: { } entity, Is: { } state} => new AutomationCondition.StateIs(entitiesById[entity], state),
        {PreviousStateLongerThan: { } duration} => new AutomationCondition.PreviousStateLongerThan(duration),
        {PreviousStateNotLongerThan: { } duration} => new AutomationCondition.PreviousStateNotLongerThan(duration),
        _ => throw new InvalidOperationException("Invalid automation condition: the options are validated at startup"),
    };

    private AutomationAction CreateAction(AutomationActionOptions options) => options switch
    {
        {Schedule: { } key} => new AutomationAction.Schedule(key, options.Delay!.Value, entitiesById[options.Entity!], options.Command!.Value),
        {Cancel: { } key} => new AutomationAction.Cancel(key),
        _ => new AutomationAction.Command(entitiesById[options.Entity!], options.Command!.Value),
    };

    private static StateIcons? Icons(EntityOptions options) => options.Icons is { } icons ? new StateIcons(icons.On, icons.Off) : null;
}

public sealed class MenuPageNode(string id, string title, MenuPageNode? parent, OnOff? listSwitches)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public MenuPageNode? Parent { get; } = parent;
    public OnOff? ListSwitches { get; } = listSwitches;
    public IReadOnlyList<MenuPageNode> Pages { get; internal set; } = [];
    public IReadOnlyList<MenuItem> Items { get; internal set; } = [];

    public override string ToString() => Id;
}

public sealed record MenuItem(Entity Entity, string? Label, ValveCommand? Valve)
{
    public string Text => Label ?? Entity.Name;
}
