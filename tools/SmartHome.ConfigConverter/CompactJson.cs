using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SmartHome.ConfigConverter;

/// <summary> JSON for people: a node that fits into a line stays on one line, larger ones are expanded. </summary>
internal static partial class CompactJson
{
    private const int LineWidth = 140;
    private const string Indent = "  ";

    private static readonly JsonSerializerOptions inline = new() {Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping};

    public static string Write(JsonNode node, string header)
    {
        var text = new StringBuilder();

        foreach (var line in header.Split('\n'))
            text.Append("// ").AppendLine(line.TrimEnd());

        Write(node, text, level: 0);
        return text.AppendLine().ToString();
    }

    private static void Write(JsonNode? node, StringBuilder text, int level)
    {
        var single = Inline(node);
        if (single.Length + level * Indent.Length <= LineWidth || node is not (JsonObject or JsonArray))
        {
            text.Append(single);
            return;
        }

        var (open, close, children) = node switch
        {
            JsonObject o => ('{', '}', o.Select(x => (Key: (string?)x.Key, x.Value)).ToList()),
            JsonArray a => ('[', ']', a.Select(x => (Key: (string?)null, Value: x)).ToList()),
            _ => throw new NotSupportedException(),
        };

        text.Append(open).AppendLine();

        for (var i = 0; i < children.Count; i++)
        {
            text.Append(string.Concat(Enumerable.Repeat(Indent, level + 1)));
            if (children[i].Key is { } key)
                text.Append(JsonSerializer.Serialize(key, inline)).Append(": ");

            Write(children[i].Value, text, level + 1);
            text.AppendLine(i < children.Count - 1 ? "," : "");
        }

        text.Append(string.Concat(Enumerable.Repeat(Indent, level))).Append(close);
    }

    private static string Inline(JsonNode? node)
    {
        var json = SurrogateEscape().Replace(node?.ToJsonString(inline) ?? "null",
                                             x => string.Concat((char)Convert.ToInt32(x.Groups[1].Value, 16), (char)Convert.ToInt32(x.Groups[2].Value, 16)));

        // {"a":1,"b":[1,2]} → { "a": 1, "b": [ 1, 2 ] }, keeping strings as they are
        var text = new StringBuilder();
        var inString = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                text.Append(c);
                if (c == '\\')
                    text.Append(json[++i]);
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    text.Append(c);
                    break;
                case '{' or '[':
                    text.Append(c).Append(' ');
                    break;
                case '}' or ']':
                    text.Append(' ').Append(c);
                    break;
                case ',':
                    text.Append(", ");
                    break;
                case ':':
                    text.Append(": ");
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        return text.ToString().Replace("{  }", "{}").Replace("[  ]", "[]");
    }

    // Emoji are escaped by the encoder even in relaxed mode.
    [GeneratedRegex(@"\\u(D[89AB][0-9A-F]{2})\\u(D[C-F][0-9A-F]{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SurrogateEscape();
}
