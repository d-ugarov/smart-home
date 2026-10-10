using SmartHome.Tests.Fakes;
using System.Text.Json.Nodes;

namespace SmartHome.Tests.Infrastructure;

public sealed record BotButton(string Text, string CallbackData);

/// <summary> A bot message with an inline keyboard as the user sees it. </summary>
public sealed record BotPage(int MessageId, string Text, List<List<BotButton>> Rows)
{
    public const string NavigationMark = " ▶";

    public bool HasFooter => Rows.Count > 0 && Rows[^1].Any(x => x.Text is "🔼 Dashboard");

    /// <summary> Content rows: sub-pages and device buttons, one per row. </summary>
    public IEnumerable<BotButton> Buttons => Rows.Take(Rows.Count - (HasFooter ? 1 : 0)).Select(x => x.Single());

    /// <summary> A button by its text; device buttons may be found without the state icon in front. </summary>
    public BotButton Find(string text) => Rows.SelectMany(x => x).SingleOrDefault(x => x.Text == text || x.Text.EndsWith(" " + text, StringComparison.Ordinal))
                                          ?? throw new AssertionException($"Button '{text}' not found on page '{Text}'");

    public IEnumerable<string> Describe()
    {
        yield return $"# {Text}";

        foreach (var row in Rows)
            yield return "  " + string.Join(" | ", row.Select(x => $"[{x.Text}]"));
    }

    public static BotPage From(JsonObject body, int messageId)
    {
        var rows = body["reply_markup"]!["inline_keyboard"]!.AsArray()
                                                            .Select(r => r!.AsArray()
                                                                           .Select(b => new BotButton(b!["text"]!.GetValue<string>(), b["callback_data"]!.GetValue<string>()))
                                                                           .ToList())
                                                            .ToList();
        return new BotPage(messageId, body["text"]!.GetValue<string>(), rows);
    }
}

/// <summary> The owner talking to the bot through the fake Telegram Bot API. </summary>
public sealed class BotSession(FakeTelegramApi telegram, long userId = HomeServerFactory.OwnerId)
{
    public async Task<BotPage> SendTextAsync(string text = "/dashboard")
    {
        var sent = telegram.Calls.Count(x => x.Method == "sendMessage");
        telegram.SendText(userId, text);
        var call = await telegram.WaitForCallAsync("sendMessage", skip: sent);
        return BotPage.From(call.Body, messageId: 1);
    }

    public async Task<BotPage> PressAsync(BotPage page, string buttonText)
    {
        var edits = telegram.Calls.Count(x => x.Method == "editMessageText");
        telegram.PressButton(userId, page.MessageId, page.Find(buttonText).CallbackData);
        var call = await telegram.WaitForCallAsync("editMessageText", skip: edits);
        return BotPage.From(call.Body, page.MessageId);
    }
}
