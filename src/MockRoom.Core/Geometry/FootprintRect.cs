using MockRoom.Core.Units;

namespace MockRoom.Core.Geometry;

/// <summary>
/// An axis-rotatable rectangle on the floor plane, in meters: the footprint an
/// item occupies. Defined by its center, width (local X), depth (local Y), and a
/// yaw rotation in radians. Provides corner/AABB queries and a point-containment
/// test used by the occupancy-grid space calculator.
/// </summary>
public readonly struct FootprintRect : IFloorRegion
{
    public FootprintRect(Vec2 center, double width, double depth, double yawRadians = 0)
    {
        Center = center;
        Width = width;
        Depth = depth;
        Yaw = yawRadians;
    }

    public Vec2 Center { get; }
    public double Width { get; }
    public double Depth { get; }
    public double Yaw { get; }

    public Area Area => Units.Area.FromSquareMeters(Width * Depth);

    /// <summary>The four corners in world space, ordered counter-clockwise.</summary>
    public (Vec2 P0, Vec2 P1, Vec2 P2, Vec2 P3) Corners()
    {
        var hw = Width / 2;
        var hd = Depth / 2;
        var cos = Math.Cos(Yaw);
        var sin = Math.Sin(Yaw);
        var cx = Center.X;
        var cy = Center.Y;

        Vec2 Rotate(double lx, double ly) =>
            new(cx + lx * cos - ly * sin, cy + lx * sin + ly * cos);

        return (Rotate(-hw, -hd), Rotate(hw, -hd), Rotate(hw, hd), Rotate(-hw, hd));
    }

    /// <summary>Axis-aligned bounding box as (minX, minY, maxX, maxY).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        var (p0, p1, p2, p3) = Corners();
        var minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        var minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        var maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        var maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return (minX, minY, maxX, maxY);
    }

    /// <summary>True if the world-space point lies within the rectangle.</summary>
    public bool Contains(Vec2 point)
    {
        // Transform the point into the rectangle's local frame and test the extents.
        var d = point - Center;
        var cos = Math.Cos(-Yaw);
        var sin = Math.Sin(-Yaw);
        var localX = d.X * cos - d.Y * sin;
        var localY = d.X * sin + d.Y * cos;
        return Math.Abs(localX) <= Width / 2 && Math.Abs(localY) <= Depth / 2;
    }
}
