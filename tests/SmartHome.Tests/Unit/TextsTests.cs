using SmartHome.Core.Common;

namespace SmartHome.Tests.Unit;

public class TextsTests
{
    [TestCase("00:00:00", "<1 min")]
    [TestCase("00:00:20", "1 min", TestName = "Rounded up: an action in 20 s is not 'now'")]
    [TestCase("00:24:01", "25 min")]
    [TestCase("01:00:00", "1 h")]
    [TestCase("01:05:00", "1 h 5 min")]
    [TestCase("08:00:00", "8 h")]
    [TestCase("1.02:00:00", "26 h")]
    public void Duration(string value, string expected)
    {
        Assert.That(Texts.Duration(TimeSpan.Parse(value)), Is.EqualTo(expected));
    }
}
