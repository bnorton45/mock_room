namespace MockRoom.Core.Geometry;

/// <summary>
/// A point or vector on the floor plane, in meters. X runs along the room width,
/// Y along the room length (depth). The origin is the room's near-left corner.
/// </summary>
public readonly struct Vec2(double x, double y) : IEquatable<Vec2>
{
    public double X { get; } = x;
    public double Y { get; } = y;

    public static Vec2 Zero => new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
