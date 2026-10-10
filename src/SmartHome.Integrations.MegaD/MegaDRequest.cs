using SmartHome.Core.Entities;

namespace SmartHome.Integrations.MegaD;

/// <summary> A request of a controller to the server: <c>GET /megad/{id}?pt=19&amp;click=1&amp;cnt=5</c>. </summary>
public abstract record MegaDRequest
{
    /// <summary> An event of a port. Whether it is an input or an output is known from the binding of the port. </summary>
    /// <param name="Press"> For an input: the press kind (a press when nothing says otherwise). </param>
    /// <param name="Counter"> For an input: <c>cnt</c>, the number of presses since the controller started. </param>
    /// <param name="Output"> For an output: the new state from <c>v=</c>. </param>
    public sealed record Port(int Number, PressKind Press, int? Counter, OnOff? Output) : MegaDRequest;

    /// <summary> <c>st=1</c>: the controller has (re)started. </summary>
    public sealed record Started : MegaDRequest;

    public sealed record Unknown : MegaDRequest;

    /// <summary>
    /// As in <c>master</c>: the first of <c>st</c> / <c>pt</c> in the query decides the kind; <c>m</c> and <c>click</c> are
    /// applied in the order of the query.
    /// </summary>
    public static MegaDRequest Parse(IReadOnlyList<KeyValuePair<string, string>> query)
    {
        foreach (var (key, value) in query)
        {
            switch (key)
            {
                case "st":
                    return new Started();
                case "pt":
                    return int.TryParse(value, out var port) ? ParsePort(port, query) : new Unknown();
            }
        }

        return new Unknown();
    }

    private static Port ParsePort(int port, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        var press = PressKind.Press;
        int? counter = null;
        OnOff? output = null;

        foreach (var (key, value) in query)
        {
            if (!int.TryParse(value, out var number))
                continue;

            switch (key)
            {
                case "m":
                    press = number switch {1 => PressKind.Release, 2 => PressKind.LongPress, _ => press};
                    break;
                case "click":
                    press = number switch {1 => PressKind.Click, 2 => PressKind.DoubleClick, _ => press};
                    break;
                case "cnt":
                    counter = number;
                    break;
                case "v":
                    output = number switch {0 => OnOff.Off, 1 => OnOff.On, _ => output};
                    break;
            }
        }

        return new Port(port, press, counter, output);
    }
}
