namespace MockRoom.Core.Units;

/// <summary>
/// A linear distance stored canonically in meters. Construct via the factory
/// methods so the call site states its unit explicitly; read it back in whatever
/// unit the consumer needs. Unit-system concerns never leak into the stored value.
/// </summary>
public readonly struct Length : IEquatable<Length>, IComparable<Length>
{
    public const double MetersPerFoot = 0.3048;
    public const double MetersPerInch = 0.0254;
    public const double MetersPerCentimeter = 0.01;

    private Length(double meters) => Meters = meters;

    public double Meters { get; }
    public double Centimeters => Meters / MetersPerCentimeter;
    public double Feet => Meters / MetersPerFoot;
    public double Inches => Meters / MetersPerInch;

    public static Length Zero => new(0);

    public static Length FromMeters(double meters) => new(meters);
    public static Length FromCentimeters(double cm) => new(cm * MetersPerCentimeter);
    public static Length FromFeet(double feet) => new(feet * MetersPerFoot);
    public static Length FromInches(double inches) => new(inches * MetersPerInch);

    /// <summary>Builds a length from whole feet plus inches (US customary input).</summary>
    public static Length FromFeetInches(double feet, double inches)
        => new(feet * MetersPerFoot + inches * MetersPerInch);

    public static Length operator +(Length a, Length b) => new(a.Meters + b.Meters);
    public static Length operator -(Length a, Length b) => new(a.Meters - b.Meters);
    public static Length operator *(Length a, double factor) => new(a.Meters * factor);
    public static Length operator *(double factor, Length a) => new(a.Meters * factor);
    public static Length operator /(Length a, double divisor) => new(a.Meters / divisor);

    /// <summary>Multiplying two lengths yields an <see cref="Area"/>.</summary>
    public static Area operator *(Length a, Length b) => Area.FromSquareMeters(a.Meters * b.Meters);

    public static bool operator <(Length a, Length b) => a.Meters < b.Meters;
    public static bool operator >(Length a, Length b) => a.Meters > b.Meters;
    public static bool operator <=(Length a, Length b) => a.Meters <= b.Meters;
    public static bool operator >=(Length a, Length b) => a.Meters >= b.Meters;
    public static bool operator ==(Length a, Length b) => a.Meters.Equals(b.Meters);
    public static bool operator !=(Length a, Length b) => !a.Meters.Equals(b.Meters);

    public int CompareTo(Length other) => Meters.CompareTo(other.Meters);
    public bool Equals(Length other) => Meters.Equals(other.Meters);
    public override bool Equals(object? obj) => obj is Length other && Equals(other);
    public override int GetHashCode() => Meters.GetHashCode();
    public override string ToString() => $"{Meters:0.###} m";
}
