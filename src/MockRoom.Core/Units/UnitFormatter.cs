using System.Globalization;

namespace MockRoom.Core.Units;

/// <summary>
/// Default <see cref="IUnitFormatter"/>: metric renders meters, imperial renders
/// feet and inches. All output uses the invariant culture (per the AOT
/// <c>InvariantGlobalization</c> setting).
/// </summary>
public sealed class UnitFormatter : IUnitFormatter
{
    public static UnitFormatter Instance { get; } = new();

    public string FormatLength(Length length, UnitSystem system) => system switch
    {
        UnitSystem.Imperial => FormatImperialLength(length),
        _ => string.Create(CultureInfo.InvariantCulture, $"{length.Meters:0.##} m"),
    };

    public string FormatArea(Area area, UnitSystem system) => system switch
    {
        UnitSystem.Imperial => string.Create(CultureInfo.InvariantCulture, $"{area.SquareFeet:0.#} ft²"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{area.SquareMeters:0.##} m²"),
    };

    private static string FormatImperialLength(Length length)
    {
        var totalInches = length.Inches;
        var feet = (int)(totalInches / 12);
        var inches = totalInches - feet * 12;

        // Round inches to one decimal; carry into feet if it lands on 12.
        inches = Math.Round(inches, 1);
        if (inches >= 12)
        {
            feet += 1;
            inches -= 12;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{feet} ft {inches:0.#} in");
    }
}
