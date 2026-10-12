using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartHome.Core.Menu;

/// <summary> The digits typed on the keypad of a reading: up to 9 before the point and 3 after it, no leading zeros. </summary>
public static partial class MeterReadingInput
{
    public const int MaxWhole = 9;
    public const int MaxFraction = 3;

    public static readonly string Longest = new string('9', MaxWhole) + "." + new string('9', MaxFraction);

    public static bool IsValid(string digits) => digits.Length == 0 || DigitsRegex().IsMatch(digits);

    /// <summary> A key that does not fit (an eleventh digit, a second point) leaves the digits as they are. </summary>
    public static string Add(string digits, char key)
    {
        if (key == '.')
            return digits.Contains('.') ? digits : (digits.Length == 0 ? "0" : digits) + ".";

        if (!char.IsAsciiDigit(key))
            return digits;

        if (digits == "0")
            return key.ToString();

        var point = digits.IndexOf('.');
        var fits = point < 0 ? digits.Length < MaxWhole : digits.Length - point - 1 < MaxFraction;
        return fits ? digits + key : digits;
    }

    public static string Erase(string digits) => digits.Length == 0 ? digits : digits[..^1];

    /// <summary> Null - nothing typed yet. </summary>
    public static decimal? Value(string digits) =>
        IsValid(digits) && digits.Length > 0 && decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;

    [GeneratedRegex(@"^(0|[1-9]\d{0,8})(\.\d{0,3})?$")]
    private static partial Regex DigitsRegex();
}
