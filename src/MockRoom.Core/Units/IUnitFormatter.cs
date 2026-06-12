namespace MockRoom.Core.Units;

/// <summary>
/// Formats canonical <see cref="Length"/> and <see cref="Area"/> values into
/// display strings for a given <see cref="UnitSystem"/>. Abstracted so alternative
/// presentations (e.g. fractional inches, localized separators) can be swapped in.
/// </summary>
public interface IUnitFormatter
{
    string FormatLength(Length length, UnitSystem system);
    string FormatArea(Area area, UnitSystem system);
}
