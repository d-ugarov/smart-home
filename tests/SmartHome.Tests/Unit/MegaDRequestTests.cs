using SmartHome.Core.Entities;
using SmartHome.Integrations.MegaD;
using System.Web;

namespace SmartHome.Tests.Unit;

public class MegaDRequestTests
{
    [TestCase("pt=19&cnt=5", 19, PressKind.Press, 5)]
    [TestCase("pt=19&m=1&cnt=5", 19, PressKind.Release, 5)]
    [TestCase("pt=19&m=2", 19, PressKind.LongPress, null)]
    [TestCase("pt=19&click=1", 19, PressKind.Click, null)]
    [TestCase("pt=19&click=2&cnt=1", 19, PressKind.DoubleClick, 1)]
    [TestCase("pt=19&m=0", 19, PressKind.Press, null)]
    [TestCase("pt=19&click=1&m=2", 19, PressKind.LongPress, null)]
    [TestCase("cnt=3&pt=19&click=2", 19, PressKind.DoubleClick, 3)]
    public void Input(string query, int port, PressKind press, int? counter)
    {
        var request = MegaDRequest.Parse(Query(query));

        Assert.That(request, Is.EqualTo(new MegaDRequest.Port(port, press, counter, null)));
    }

    [TestCase("pt=24&mdid=1&v=1", OnOff.On)]
    [TestCase("pt=24&v=0", OnOff.Off)]
    [TestCase("pt=24&v=7", null)]
    public void Output(string query, OnOff? output)
    {
        var request = (MegaDRequest.Port)MegaDRequest.Parse(Query(query));

        Assert.That(request.Output, Is.EqualTo(output));
    }

    [TestCase("st=1", typeof(MegaDRequest.Started))]
    [TestCase("st=1&pt=3", typeof(MegaDRequest.Started), Description = "the first of st/pt decides")]
    [TestCase("", typeof(MegaDRequest.Unknown))]
    [TestCase("cmd=all", typeof(MegaDRequest.Unknown))]
    [TestCase("pt=abc", typeof(MegaDRequest.Unknown))]
    public void Kind(string query, Type expected)
    {
        Assert.That(MegaDRequest.Parse(Query(query)), Is.InstanceOf(expected));
    }

    private static List<KeyValuePair<string, string>> Query(string query)
    {
        var parsed = HttpUtility.ParseQueryString(query);
        return parsed.AllKeys.Where(x => x != null).Select(x => KeyValuePair.Create(x!, parsed[x]!)).ToList();
    }
}
