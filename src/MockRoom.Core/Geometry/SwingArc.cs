using MockRoom.Core.Units;

namespace MockRoom.Core.Geometry;

/// <summary>
/// A quarter-disc on the floor plane, in meters: the region a hinged door leaf sweeps
/// as it opens 90° into the room. Defined by the <see cref="Hinge"/> point, a
/// <see cref="Radius"/> equal to the leaf length, and two perpendicular unit
/// directions — <see cref="DirA"/> (the closed leaf, lying along the wall) and
/// <see cref="DirB"/> (the fully-open leaf, pointing into the room). Because the two
/// directions are perpendicular, the swept wedge is exactly the set of points within
/// the radius whose offset from the hinge projects non-negatively onto both.
/// </summary>
public readonly struct SwingArc : IFloorRegion
{
    public SwingArc(Vec2 hinge, double radius, Vec2 dirA, Vec2 dirB)
    {
        Hinge = hinge;
        Radius = radius;
        DirA = dirA;
        DirB = dirB;
    }

    public Vec2 Hinge { get; }
    public double Radius { get; }
    public Vec2 DirA { get; }
    public Vec2 DirB { get; }

    /// <summary>The area swept: a quarter of a full disc of the given radius.</summary>
    public Area Area => Units.Area.FromSquareMeters(Math.PI * Radius * Radius / 4);

    /// <summary>True if the world-space point lies within the swept quarter-disc.</summary>
    public bool Contains(Vec2 point)
    {
        var d = point - Hinge;
        if (Vec2.Dot(d, d) > Radius * Radius)
            return false;
        return Vec2.Dot(d, DirA) >= 0 && Vec2.Dot(d, DirB) >= 0;
    }

    /// <summary>
    /// Conservative bounding box: the full hinge-centered square of side 2·radius.
    /// The grid re-tests each cell with <see cref="Contains"/>, so a loose box only
    /// scans a few extra cells and never over-marks.
    /// </summary>
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
        => (Hinge.X - Radius, Hinge.Y - Radius, Hinge.X + Radius, Hinge.Y + Radius);

    /// <summary>
    /// True if the footprint rectangle overlaps the swept quarter-disc. Approximate but
    /// biased toward reporting an overlap (used to keep furniture out of door swings):
    /// tests the rect's corners against the sector, the hinge and both radius endpoints
    /// against the rect, and a handful of points sampled along the arc curve.
    /// </summary>
    public bool Intersects(FootprintRect rect)
    {
        var (p0, p1, p2, p3) = rect.Corners();
        if (Contains(p0) || Contains(p1) || Contains(p2) || Contains(p3))
            return true;

        if (rect.Contains(Hinge) ||
            rect.Contains(Hinge + DirA * Radius) ||
            rect.Contains(Hinge + DirB * Radius))
            return true;

        const int samples = 8;
        for (var i = 1; i < samples; i++)
        {
            var t = (double)i / samples * (Math.PI / 2);
            var dir = DirA * Math.Cos(t) + DirB * Math.Sin(t);
            if (rect.Contains(Hinge + dir * Radius))
                return true;
        }

        return false;
    }
}
