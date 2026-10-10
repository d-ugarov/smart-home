using Microsoft.Extensions.Logging;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Home;
using SmartHome.Core.Rules;
using SmartHome.Core.State;
using SmartHome.Core.Valves;

namespace SmartHome.Core.Menu;

/// <summary>
/// The menu of the house as in <c>master</c>: a page shows sub-pages, then its items with the state read from the
/// devices right now, then the dynamic list; a press on a switch toggles it and draws the page again.
/// New: a valve button opens or closes the valve after a confirmation; the page of automations, when they are on.
/// </summary>
public sealed class MenuService(
    HomeModel home,
    IDeviceGateway gateway,
    HomeState state,
    ValveService valves,
    AutomationMenu automations,
    ILogger<MenuService> logger)
{
    public async Task<MenuPage> HandleAsync(MenuAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case MenuAction.OpenPage x when home.FindPage(x.PageId) is { } page:
                return await RenderAsync(page, cancellationToken);

            case MenuAction.Toggle x when home.FindPage(x.PageId) is { } page && FindSwitch(page, x.EntityId) is { } entity:
                await ToggleAsync(entity, cancellationToken);
                return await RenderAsync(page, cancellationToken);

            case MenuAction.AskValve x when home.FindPage(x.PageId) is { } page && FindValveItem(page, x.EntityId, x.Command) is { } item:
                return await AskValveAsync(page, item, cancellationToken);

            case MenuAction.RunValve x when home.FindPage(x.PageId) is { } page && FindValveItem(page, x.EntityId, x.Command) is { } item:
                await valves.RunAsync(item.Entity, x.Command, cancellationToken);
                return await RenderAsync(page, cancellationToken);

            case MenuAction.Automations when automations.IsAvailable:
                return automations.Render();

            case MenuAction.ToggleAutomation x when automations.IsAvailable:
                return await automations.ToggleAsync(x.AutomationId, cancellationToken);

            default:
                return Dashboard();
        }
    }

    private MenuPage Dashboard()
    {
        var buttons = home.Dashboard.Select(x => new MenuButton(x.Title, new MenuAction.OpenPage(x.Id))).ToList();
        if (automations.IsAvailable)
            buttons.Add(new MenuButton(MenuTexts.AutomationsButton, new MenuAction.Automations()));

        return new MenuPage(MenuTexts.Dashboard, [], buttons, []);
    }

    private async Task<MenuPage> RenderAsync(MenuPageNode page, CancellationToken cancellationToken)
    {
        var buttons = page.Pages.Select(x => new MenuButton(x.Title + MenuTexts.SubPageMark, new MenuAction.OpenPage(x.Id))).ToList();

        var toRead = page.Items.Select(x => x.Entity)
                         .Concat(page.ListSwitches != null ? home.Switches : [])
                         .Distinct()
                         .ToList();

        var snapshot = toRead.Count > 0 ? await gateway.ReadAsync(toRead, cancellationToken) : new Dictionary<string, EntityValue>();
        state.SetAll(snapshot, StateSource.Poll);

        buttons.AddRange(page.Items.Select(x => ItemButton(page, x, snapshot.GetValueOrDefault(x.Entity.Id))));

        if (page.ListSwitches is { } listed)
        {
            buttons.AddRange(home.Switches.Where(x => snapshot.GetValueOrDefault(x.Id) is OnOffValue value && value.State == listed)
                                 .Select(x => new MenuButton($"{StateText(x, listed)} {x.Name}", new MenuAction.Toggle(page.Id, x.Id))));
        }

        var footer = new List<MenuButton>();
        if (page.Parent != null)
            footer.Add(new MenuButton(MenuTexts.BackButton, new MenuAction.OpenPage(page.Parent.Id)));
        footer.Add(new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard()));

        return new MenuPage(page.Title, [], buttons, footer);
    }

    private MenuButton ItemButton(MenuPageNode page, MenuItem item, EntityValue? value)
    {
        switch (item.Entity.Type)
        {
            case EntityType.Valve:
            {
                var running = valves.ActivityOf(item.Entity, value) == (item.Valve == ValveCommand.Open ? ValveActivity.Opening : ValveActivity.Closing);
                return new MenuButton(running ? $"{MenuTexts.PulseRunning} {item.Text}" : item.Text,
                                      new MenuAction.AskValve(page.Id, item.Entity.Id, item.Valve!.Value));
            }
            case EntityType.Switch:
                return new MenuButton(WithState(item, value), new MenuAction.Toggle(page.Id, item.Entity.Id));
            default:
                // Inputs are only shown: the button draws the page again.
                return new MenuButton(WithState(item, value), new MenuAction.OpenPage(page.Id));
        }
    }

    private async Task<MenuPage> AskValveAsync(MenuPageNode page, MenuItem item, CancellationToken cancellationToken)
    {
        var read = await gateway.ReadAsync([item.Entity], cancellationToken);
        if (valves.ActivityOf(item.Entity, read.GetValueOrDefault(item.Entity.Id)) != ValveActivity.Idle)
            return await RenderAsync(page, cancellationToken);

        return new MenuPage(item.Entity.Name,
                            [$"{item.Text}?"],
                            [],
                            [
                                new MenuButton(MenuTexts.Yes, new MenuAction.RunValve(page.Id, item.Entity.Id, item.Valve!.Value)),
                                new MenuButton(MenuTexts.No, new MenuAction.OpenPage(page.Id)),
                            ]);
    }

    private async Task ToggleAsync(Entity entity, CancellationToken cancellationToken)
    {
        var result = await gateway.SwitchAsync(entity, SwitchCommand.Toggle, cancellationToken);
        logger.LogInformation("Menu: toggle {Entity}: {Result}", entity.Id, result.Report());

        if (result.IsSuccess && state.Get(entity.Id)?.Value is OnOffValue known)
            state.Set(entity.Id, new OnOffValue(InputRuleEngine.Expected(known.State, SwitchCommand.Toggle)!.Value), StateSource.Menu);
    }

    private Entity? FindSwitch(MenuPageNode page, string entityId)
    {
        var entity = home.FindEntity(entityId);
        if (entity?.Type != EntityType.Switch)
            return null;

        return page.Items.Any(x => x.Entity == entity) || page.ListSwitches != null ? entity : null;
    }

    private static MenuItem? FindValveItem(MenuPageNode page, string entityId, ValveCommand command) =>
        page.Items.FirstOrDefault(x => x.Entity.Id == entityId && x.Entity.Type == EntityType.Valve && x.Valve == command);

    private static string WithState(MenuItem item, EntityValue? value) =>
        value is OnOffValue known ? $"{StateText(item.Entity, known.State)} {item.Text}" : item.Text;

    private static string StateText(Entity entity, OnOff state) => entity.Icons?.For(state) ?? $"[{state}]";
}
