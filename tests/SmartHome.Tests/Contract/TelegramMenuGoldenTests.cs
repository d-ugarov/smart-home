using SmartHome.Core.Entities;
using SmartHome.Core.Menu;
using SmartHome.Tests.Infrastructure;

namespace SmartHome.Tests.Contract;

/// <summary>
/// Every page of the bot menu (texts, button order, state icons from the controller snapshots) and what every button
/// does: the relay command, or the confirmation of a valve and the scenario it runs. Recorded from <c>master</c>; the
/// valve buttons are the agreed difference.
/// </summary>
[NonParallelizable]
[TestFixtureSource(typeof(House), nameof(House.All))]
public class TelegramMenuGoldenTests(House house)
{
    private HomeServerFactory factory = null!;
    private HttpClient client = null!;
    private BotSession bot = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        house.EnsureAvailable();
        factory = new HomeServerFactory(house);
        client = factory.CreateClient();
        await factory.WaitForBotAsync();
        bot = new BotSession(factory.Network.Telegram);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        client?.Dispose();
        if (factory != null)
            await factory.DisposeAsync();
    }

    [Test]
    public async Task WholeMenu_MatchesMaster()
    {
        factory.Reset();
        var lines = new List<string>();
        var dashboard = await bot.SendTextAsync();
        lines.AddRange(dashboard.Describe());

        foreach (var button in dashboard.Buttons)
            await WalkAsync(dashboard, button.Text, lines);

        Assert.That(factory.Network.UnexpectedRequests, Is.Empty);
        Golden.AssertMatches(house, "telegram-menu.golden", lines);
    }

    private async Task WalkAsync(BotPage parent, string buttonText, List<string> lines)
    {
        factory.Reset();
        var page = await bot.PressAsync(parent, buttonText);
        lines.AddRange(page.Describe());

        foreach (var button in page.Buttons)
        {
            if (button.Text.EndsWith(BotPage.NavigationMark, StringComparison.Ordinal))
            {
                await WalkAsync(page, button.Text, lines);
                continue;
            }

            factory.Reset();
            var result = await bot.PressAsync(page, button.Text);

            if (MenuAction.Parse(button.CallbackData) is MenuAction.AskValve valve)
            {
                lines.Add($"  press [{button.Text}] -> asks: {result.Text.Replace("\n\n", " / ")} {string.Join(" | ", result.Rows.SelectMany(x => x).Select(x => $"[{x.Text}]"))}");

                factory.Reset();
                await bot.PressAsync(result, MenuTexts.Yes);
                lines.Add($"    [{MenuTexts.Yes}] -> {Commands()}");
                await FinishPulseAsync(valve);
                continue;
            }

            lines.Add($"  press [{button.Text}] -> {Commands()}");
        }
    }

    /// <summary> The controller reports the end of the pulse, so the next valve button is not blocked. </summary>
    private async Task FinishPulseAsync(MenuAction.AskValve valve)
    {
        var (open, close) = house.ValveRelays[valve.EntityId];
        var relay = valve.Command == ValveCommand.Open ? open : close;
        await client.GetAsync($"/megad/{relay.DeviceId}?pt={relay.Port}&mdid=1&v=0");
    }

    private string Commands() => factory.Network.Commands is {Count: > 0} commands ? string.Join(" ", commands) : "-";
}
