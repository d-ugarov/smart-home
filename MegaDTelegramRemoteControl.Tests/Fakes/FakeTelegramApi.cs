using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace MegaDTelegramRemoteControl.Tests.Fakes;

public sealed record TelegramCall(string Method, JsonObject Body);

/// <summary>
/// Telegram Bot API (api.telegram.org) as seen by the bot library: long polling via <c>getUpdates</c>,
/// every other method is recorded and answered with a plausible result.
/// </summary>
public sealed class FakeTelegramApi
{
    private const long BotUserId = 999;

    private readonly Channel<JsonObject> updates = Channel.CreateUnbounded<JsonObject>();
    private readonly Lock sync = new();
    private readonly List<TelegramCall> calls = [];
    private long nextUpdateId = 1;
    private int nextMessageId = 1000;

    public IReadOnlyList<TelegramCall> Calls
    {
        get
        {
            lock (sync)
                return calls.ToList();
        }
    }

    public void ClearCalls()
    {
        lock (sync)
            calls.Clear();
    }

    public void SendText(long userId, string text)
    {
        Enqueue(new JsonObject
                {
                    ["message"] = new JsonObject
                                  {
                                      ["message_id"] = Interlocked.Increment(ref nextMessageId),
                                      ["from"] = User(userId),
                                      ["chat"] = Chat(userId),
                                      ["date"] = 1_700_000_000,
                                      ["text"] = text,
                                  },
                });
    }

    public void PressButton(long userId, int messageId, string callbackData)
    {
        Enqueue(new JsonObject
                {
                    ["callback_query"] = new JsonObject
                                         {
                                             ["id"] = $"cb{Interlocked.Increment(ref nextMessageId)}",
                                             ["from"] = User(userId),
                                             ["message"] = new JsonObject
                                                           {
                                                               ["message_id"] = messageId,
                                                               ["from"] = User(BotUserId, isBot: true),
                                                               ["chat"] = Chat(userId),
                                                               ["date"] = 1_700_000_000,
                                                               ["text"] = "menu",
                                                           },
                                             ["chat_instance"] = "1",
                                             ["data"] = callbackData,
                                         },
                });
    }

    public async Task<TelegramCall> WaitForCallAsync(string method, int skip = 0, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            var found = Calls.Where(x => x.Method == method).Skip(skip).FirstOrDefault();
            if (found != null)
                return found;

            await Task.Delay(10);
        }

        throw new TimeoutException($"Telegram method '{method}' was not called (skip {skip})");
    }

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.Segments[^1].Trim('/');
        var body = await ReadBodyAsync(request, cancellationToken);

        if (method.Equals("getUpdates", StringComparison.OrdinalIgnoreCase))
            return Ok(await GetUpdatesAsync(cancellationToken));

        lock (sync)
            calls.Add(new TelegramCall(method, body));

        JsonNode result = method switch
        {
            "sendMessage" or "editMessageText" => new JsonObject
                                                  {
                                                      ["message_id"] = body["message_id"]?.GetValue<int>() ?? Interlocked.Increment(ref nextMessageId),
                                                      ["from"] = User(BotUserId, isBot: true),
                                                      ["chat"] = Chat(body["chat_id"]!.GetValue<long>()),
                                                      ["date"] = 1_700_000_000,
                                                      ["text"] = body["text"]?.DeepClone(),
                                                  },
            "getMe" => User(BotUserId, isBot: true),
            _ => JsonValue.Create(true),
        };

        return Ok(result);
    }

    private async Task<JsonArray> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        var result = new JsonArray();

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(TimeSpan.FromMilliseconds(100));

        try
        {
            if (await updates.Reader.WaitToReadAsync(wait.Token))
            {
                while (updates.Reader.TryRead(out var update))
                    result.Add(update);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return result;
    }

    private void Enqueue(JsonObject update)
    {
        update["update_id"] = Interlocked.Increment(ref nextUpdateId);
        updates.Writer.TryWrite(update);
    }

    private static async Task<JsonObject> ReadBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is null)
            return [];

        var text = await request.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? [] : JsonNode.Parse(text)!.AsObject();
    }

    private static JsonObject User(long id, bool isBot = false) => new()
                                                                   {
                                                                       ["id"] = id,
                                                                       ["is_bot"] = isBot,
                                                                       ["first_name"] = isBot ? "Bot" : $"User{id}",
                                                                       ["username"] = isBot ? "home_bot" : $"user{id}",
                                                                   };

    private static JsonObject Chat(long id) => new() {["id"] = id, ["type"] = "private"};

    private static HttpResponseMessage Ok(JsonNode result) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(new JsonObject {["ok"] = true, ["result"] = result}.ToJsonString(), Encoding.UTF8, "application/json"),
        };
}
