using System.Globalization;
using System.Text.RegularExpressions;

namespace MockRoom.Core.Units;

/// <summary>
/// Parses free-form user input into a canonical <see cref="Length"/>, interpreting
/// bare numbers according to the active <see cref="UnitSystem"/>:
///   Metric   — meters by default; accepts a "cm"/"m" suffix.
///   Imperial — feet by default; accepts feet/inches notation such as
///              "8 ft 2 in", "8' 2\"", "8'2", or "98 in".
/// </summary>
public static partial class UnitConverter
{
    public static bool TryParse(string? input, UnitSystem system, out Length length)
    {
        length = Length.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim().ToLowerInvariant();
        return system == UnitSystem.Imperial
            ? TryParseImperial(text, out length)
            : TryParseMetric(text, out length);
    }

    private static bool TryParseMetric(string text, out Length length)
    {
        length = Length.Zero;

        if (text.EndsWith("cm", StringComparison.Ordinal))
            return TryNumber(text[..^2], v => Length.FromCentimeters(v), out length);
        if (text.EndsWith('m'))
            return TryNumber(text[..^1], v => Length.FromMeters(v), out length);

        return TryNumber(text, v => Length.FromMeters(v), out length);
    }

    private static bool TryParseImperial(string text, out Length length)
    {
        length = Length.Zero;

        // Combined feet + inches, e.g. "8 ft 2 in", "8' 2\"", "8'2".
        // Tried before the inches-only forms so a trailing "in"/'"' on a
        // feet+inches string isn't mistaken for an inches-only value.
        var match = FeetInchesPattern().Match(text);
        if (match.Success)
        {
            var feetOk = double.TryParse(match.Groups["ft"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var feet);
            if (!feetOk) return false;

            double inches = 0;
            if (match.Groups["in"].Success && match.Groups["in"].Value.Length > 0 &&
                !double.TryParse(match.Groups["in"].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out inches))
            {
                return false;
            }

            length = Length.FromFeetInches(feet, inches);
            return true;
        }

        // Inches only, e.g. "98 in" or "98\"".
        if (text.EndsWith("in", StringComparison.Ordinal))
            return TryNumber(text[..^2], v => Length.FromInches(v), out length);
        if (text.EndsWith('"'))
            return TryNumber(text[..^1], v => Length.FromInches(v), out length);

        // Bare number → feet.
        return TryNumber(text, v => Length.FromFeet(v), out length);
    }

    private static bool TryNumber(string token, Func<double, Length> build, out Length length)
    {
        length = Length.Zero;
        if (double.TryParse(token.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            length = build(value);
            return true;
        }
        return false;
    }

    // Matches "<feet>" with an optional foot marker (' or "ft") followed by optional inches.
    [GeneratedRegex(@"^(?<ft>\d+(\.\d+)?)\s*(?:'|ft)\s*(?<in>\d+(\.\d+)?)?\s*(?:""|in)?$")]
    private static partial Regex FeetInchesPattern();
}
