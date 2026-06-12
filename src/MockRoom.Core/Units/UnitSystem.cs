namespace MockRoom.Core.Units;

/// <summary>
/// The measurement system used for displaying and entering dimensions.
/// All values are stored canonically in meters; the unit system only affects
/// parsing and formatting at the presentation boundary.
/// </summary>
public enum UnitSystem
{
    /// <summary>Meters and centimeters.</summary>
    Metric,

    /// <summary>Feet and inches (US customary).</summary>
    Imperial,
}
