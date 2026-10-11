using SmartHome.Core.Home;
using SmartHome.Core.Valves;
using System.Globalization;

namespace SmartHome.Core.Menu;

/// <summary>
/// The cleaning of a bathroom from the menu: the button among the items of the page of its valve and the page of the
/// cleaning. Times are the local time of the house.
/// </summary>
public sealed class CleaningMenu(CleaningService cleaning, HouseClock clock)
{
    public bool IsAvailable => cleaning.IsAvailable;

    public string ButtonText(MenuItem item) =>
        cleaning.UntilOf(item.Entity) is { } until
            ? $"{MenuTexts.CleaningMark} {item.Text} — until {Time(until)}"
            : $"{MenuTexts.CleaningMark} {item.Text}";

    public MenuPage Render(MenuPageNode page, MenuItem item)
    {
        var until = cleaning.UntilOf(item.Entity);

        var text = until is { } end
            ? $"Until {Time(end)} the leak sensor does not close the valves. Then it does again, and if it is still wet, the valves are closed. " +
              "A button below sets the end anew, from now."
            : "The leak sensor closes the valves. During the cleaning it does not, for the time chosen below.";

        var buttons = new List<MenuButton>
        {
            Change("30 min", CleaningChange.HalfHour),
            Change("1 h", CleaningChange.Hour),
            Change("2 h", CleaningChange.TwoHours),
        };
        if (until != null)
            buttons.Add(Change("⏹ End the cleaning", CleaningChange.End));

        return new MenuPage($"{MenuTexts.CleaningMark} {item.Entity.Name}",
                            [text],
                            buttons,
                            [new MenuButton(MenuTexts.BackButton, new MenuAction.OpenPage(page.Id)), new MenuButton(MenuTexts.DashboardButton, new MenuAction.Dashboard())]);

        MenuButton Change(string buttonText, CleaningChange change) => new(buttonText, new MenuAction.ChangeCleaning(page.Id, item.Entity.Id, change));
    }

    public async Task<MenuPage> ChangeAsync(MenuPageNode page, MenuItem item, CleaningChange change, CancellationToken cancellationToken)
    {
        if (change == CleaningChange.End)
            await cleaning.EndCleaningAsync(item.Entity, cancellationToken);
        else
            await cleaning.StartCleaningAsync(item.Entity, Duration(change), cancellationToken);

        return Render(page, item);
    }

    private static TimeSpan Duration(CleaningChange change) => change switch
    {
        CleaningChange.HalfHour => TimeSpan.FromMinutes(30),
        CleaningChange.Hour => TimeSpan.FromHours(1),
        CleaningChange.TwoHours => TimeSpan.FromHours(2),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, null),
    };

    private string Time(DateTimeOffset time) => clock.ToLocal(time).ToString("HH:mm", CultureInfo.InvariantCulture);
}
