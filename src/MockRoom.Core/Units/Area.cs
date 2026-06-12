namespace MockRoom.Core.Units;

/// <summary>
/// A planar area stored canonically in square meters. Produced by multiplying two
/// <see cref="Length"/> values; read back in metric or imperial as needed.
/// </summary>
public readonly struct Area : IEquatable<Area>, IComparable<Area>
{
    public static readonly double SquareMetersPerSquareFoot =
        Length.MetersPerFoot * Length.MetersPerFoot;

    private Area(double squareMeters) => SquareMeters = squareMeters;

    public double SquareMeters { get; }
    public double SquareFeet => SquareMeters / SquareMetersPerSquareFoot;

    public static Area Zero => new(0);

    public static Area FromSquareMeters(double squareMeters) => new(squareMeters);
    public static Area FromSquareFeet(double squareFeet) => new(squareFeet * SquareMetersPerSquareFoot);

    public static Area operator +(Area a, Area b) => new(a.SquareMeters + b.SquareMeters);
    public static Area operator -(Area a, Area b) => new(a.SquareMeters - b.SquareMeters);

    public static bool operator <(Area a, Area b) => a.SquareMeters < b.SquareMeters;
    public static bool operator >(Area a, Area b) => a.SquareMeters > b.SquareMeters;
    public static bool operator <=(Area a, Area b) => a.SquareMeters <= b.SquareMeters;
    public static bool operator >=(Area a, Area b) => a.SquareMeters >= b.SquareMeters;
    public static bool operator ==(Area a, Area b) => a.SquareMeters.Equals(b.SquareMeters);
    public static bool operator !=(Area a, Area b) => !a.SquareMeters.Equals(b.SquareMeters);

    /// <summary>Clamps a (possibly negative) area to zero. Used when subtracting footprints.</summary>
    public Area ClampNonNegative() => SquareMeters < 0 ? Zero : this;

    public int CompareTo(Area other) => SquareMeters.CompareTo(other.SquareMeters);
    public bool Equals(Area other) => SquareMeters.Equals(other.SquareMeters);
    public override bool Equals(object? obj) => obj is Area other && Equals(other);
    public override int GetHashCode() => SquareMeters.GetHashCode();
    public override string ToString() => $"{SquareMeters:0.##} m²";
}
