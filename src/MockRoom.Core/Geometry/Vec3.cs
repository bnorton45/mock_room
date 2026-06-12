namespace MockRoom.Core.Geometry;

/// <summary>
/// A point or vector in 3D space, in meters. X = width, Y = height (up),
/// Z = length (depth), matching the renderer's right-handed convention.
/// </summary>
public readonly struct Vec3(double x, double y, double z) : IEquatable<Vec3>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Z { get; } = z;

    public static Vec3 Zero => new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Projects onto the floor plane, dropping the height (Y) component.</summary>
    public Vec2 ToFloor() => new(X, Z);

    public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
