using Microsoft.Extensions.Options;
using SmartHome.Core.Configuration;
using SmartHome.Core.Entities;
using SmartHome.Core.Rules;
using System.Text;

namespace SmartHome.Core.Home;

/// <summary> The house built from the validated <see cref="HomeOptions"/>: entities, menu tree and input rules. </summary>
public sealed class HomeModel
{
    private readonly Dictionary<string, Entity> entitiesById;
    private readonly Dictionary<string, MenuPageNode> pagesById = [];
    private readonly Dictionary<string, IReadOnlyList<InputRule>> rulesByButton;
    private readonly Dictionary<string, IReadOnlyList<Entity>> readByButton;

    public HomeModel(IOptions<HomeOptions> options) : this(options.Value)
    {
    }

    public HomeModel(HomeOptions options)
    {
        Entities = options.Entities
                          .Select(x => new Entity(x.Id, x.Name, x.Type!.Value, x.Icons is { } icons ? new StateIcons(icons.On, icons.Off) : null))
                          .ToList();
        entitiesById = Entities.ToDictionary(x => x.Id);

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
    }

    /// <summary> In the order of the config. </summary>
    public IReadOnlyList<Entity> Entities { get; }

    public IReadOnlyList<Entity> Switches { get; }

    public IReadOnlyList<MenuPageNode> Dashboard { get; }

    public Entity? FindEntity(string id) => entitiesById.GetValueOrDefault(id);

    public MenuPageNode? FindPage(string id) => pagesById.GetValueOrDefault(id);

    public IReadOnlyList<InputRule> RulesOf(string buttonId) => rulesByButton.GetValueOrDefault(buttonId) ?? [];

    public IReadOnlyList<Entity> EntitiesReadOnPress(string buttonId) => readByButton.GetValueOrDefault(buttonId) ?? [];

    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine($"Home: {Entities.Count} entities, {rulesByButton.Values.Sum(x => x.Count)} rules on {rulesByButton.Count} buttons, {pagesById.Count} menu pages");

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
