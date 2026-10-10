using Microsoft.Extensions.Options;
using SmartHome.Clients.Telegram;
using SmartHome.Core.Common;
using SmartHome.Core.Menu;
using SmartHome.Integrations.MegaD;
using System.Text;

namespace SmartHome.Host;

public static class Endpoints
{
    /// <summary>
    /// Requests of the controllers. The controller waits for the answer: 200 - the server has handled the press,
    /// 204 - the controller runs its own scenario (also when anything goes wrong here).
    /// </summary>
    public static void MapMegaD(this WebApplication app)
    {
        app.MapGet("/megad/{id}", async (string id, HttpRequest request, IMegaDRequestHandler handler, ILogger<IMegaDRequestHandler> logger) =>
        {
            var query = request.Query.Select(x => KeyValuePair.Create(x.Key, x.Value.ToString())).ToList();

            // Not the request token: once started, the processing goes to the end, as in master.
            var result = await InvokeOperations.InvokeAsync(logger, $"MegaD {id}", () => handler.ProcessAsync(id, query, CancellationToken.None));

            return result is {IsSuccess: true, Data: true} ? Results.Ok() : Results.NoContent();
        });
    }

    /// <summary>
    /// The menu without Telegram for local runs (<see cref="TelegramOptions.UseFakeBot"/> only):
    /// <c>/debug/menu?action=...</c> presses a button and shows the page it leads to.
    /// </summary>
    public static void MapDebugMenu(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value.UseFakeBot)
            return;

        app.MapGet("/debug/menu", async (string? action, MenuService menu, CancellationToken cancellationToken) =>
        {
            var page = await menu.HandleAsync(MenuAction.Parse(action), cancellationToken);
            return Results.Text(Describe(page), "text/plain", Encoding.UTF8);
        });
    }

    private static string Describe(MenuPage page)
    {
        var text = new StringBuilder();
        text.AppendLine(page.Title);

        foreach (var paragraph in page.Paragraphs)
            text.AppendLine().AppendLine(paragraph);

        text.AppendLine();
        foreach (var button in page.Buttons.Concat(page.Footer))
            text.AppendLine($"[{button.Text}]  /debug/menu?action={Uri.EscapeDataString(button.Action.Serialize())}");

        return text.ToString();
    }
}
