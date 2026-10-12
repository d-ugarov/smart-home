using SmartHome.Core.Menu;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SmartHome.Clients.Telegram;

/// <summary>
/// A menu page as a Telegram message: the text and an inline keyboard (a button per row, the rows of the grid, the footer
/// in one row). A page with tables is HTML, the tables in a fixed-width font; any other page is plain text.
/// </summary>
internal static class TelegramMenuRenderer
{
    public static (string Text, InlineKeyboardMarkup Keyboard, ParseMode ParseMode) Render(MenuPage page)
    {
        var paragraphs = page.Paragraphs.Prepend(page.Title).ToList();
        var (text, parseMode) = page.Tables.Count == 0
            ? (string.Join("\n\n", paragraphs), ParseMode.None)
            : (string.Join("\n\n", paragraphs.Select(Escape).Concat(page.Tables.Select(x => $"<pre>{Escape(x)}</pre>"))), ParseMode.Html);

        var rows = page.Buttons.Select(x => new[] {Button(x)}).ToList();
        rows.AddRange(page.Grid.Select(x => x.Select(Button).ToArray()));
        if (page.Footer.Count > 0)
            rows.Add(page.Footer.Select(Button).ToArray());

        return (text, new InlineKeyboardMarkup(rows), parseMode);
    }

    /// <summary> Buttons under a notification, one per row. </summary>
    public static InlineKeyboardMarkup Keyboard(IReadOnlyList<MenuButton> buttons) => new(buttons.Select(x => new[] {Button(x)}));

    /// <summary> Telegram HTML needs only these three escaped; anything else (emoji, Cyrillic) goes as is. </summary>
    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static InlineKeyboardButton Button(MenuButton button) => InlineKeyboardButton.WithCallbackData(button.Text, button.Action.Serialize());
}
