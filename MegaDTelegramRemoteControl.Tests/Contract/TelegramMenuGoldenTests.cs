using MegaDTelegramRemoteControl.Tests.Infrastructure;

namespace MegaDTelegramRemoteControl.Tests.Contract;

/// <summary>
/// Every page of the bot menu (texts, button order, state icons from the controller snapshots) and the relay
/// command of every device button. Recorded from <c>master</c>.
/// </summary>
[NonParallelizable]
[TestFixtureSource(typeof(House), nameof(House.All))]
public class TelegramMenuGoldenTests(House house)
{
    private HomeServerFactory factory = null!;
    private BotSession bot = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        house.EnsureAvailable();
        factory = new HomeServerFactory(house);
        await factory.WaitForBotAsync();
        bot = new BotSession(factory.Network.Telegram);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
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
            await bot.PressAsync(page, button.Text);
            var commands = factory.Network.Commands;
            lines.Add($"  press [{button.Text}] -> {(commands.Count > 0 ? string.Join(" ", commands) : "-")}");
        }
    }
}
