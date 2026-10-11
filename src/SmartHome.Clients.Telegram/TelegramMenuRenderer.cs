using SmartHome.Core.Menu;
using Telegram.Bot.Types.ReplyMarkups;

namespace SmartHome.Clients.Telegram;

/// <summary> A menu page as a Telegram message: plain text and an inline keyboard (a button per row, the footer in one row). </summary>
internal static class TelegramMenuRenderer
{
    public static (string Text, InlineKeyboardMarkup Keyboard) Render(MenuPage page)
    {
        var text = string.Join("\n\n", page.Paragraphs.Prepend(page.Title));

        var rows = page.Buttons.Select(x => new[] {Button(x)}).ToList();
        if (page.Footer.Count > 0)
            rows.Add(page.Footer.Select(Button).ToArray());

        return (text, new InlineKeyboardMarkup(rows));
    }

    /// <summary> Buttons under a notification, one per row. </summary>
    public static InlineKeyboardMarkup Keyboard(IReadOnlyList<MenuButton> buttons) => new(buttons.Select(x => new[] {Button(x)}));

    private static InlineKeyboardButton Button(MenuButton button) => InlineKeyboardButton.WithCallbackData(button.Text, button.Action.Serialize());
}
