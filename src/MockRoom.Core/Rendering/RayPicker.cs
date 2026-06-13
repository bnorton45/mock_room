using System.Numerics;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;

namespace MockRoom.Core.Rendering;

/// <summary>
/// Picks the surface or item a <see cref="Ray"/> hits in the 3D viewport.
/// All geometry is axis-aligned world-space (the room occupies [0,W]×[0,H]×[0,L]).
/// Pure and GL-free so it stays NativeAOT-clean and unit-testable.
/// </summary>
public static class RayPicker
{
    /// <summary>
    /// Returns the front-most item the ray enters, or <c>null</c> if it misses them
    /// all. "Front-most" is the smallest non-negative hit distance along the ray.
    /// </summary>
    public static RoomItem? Pick(IReadOnlyList<RoomItem> items, Ray ray)
    {
        RoomItem? best = null;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < items.Count; i++)
        {
            var distance = IntersectItem(ray, items[i]);
            if (distance is { } d && d < bestDistance)
            {
                bestDistance = d;
                best = items[i];
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the front-most <see cref="PaintTarget"/> the ray intersects: items take
    /// priority over room geometry. Wall hits check whether they fall inside any opening,
    /// returning an <see cref="OpeningPaintTarget"/> instead of a <see cref="WallPaintTarget"/>
    /// in that case. Returns <c>null</c> if the ray hits nothing.
    /// </summary>
    public static PaintTarget? PickTarget(Room room, Ray ray)
    {
        var dims = room.Dimensions;
        var w = (float)dims.Width.Meters;
        var l = (float)dims.Length.Meters;
        var h = (float)dims.Height.Meters;

        // Items take priority — they sit inside the room so they're always in front of walls.
        var item = Pick(room.Items, ray);
        if (item is not null)
            return new ItemPaintTarget(item);

        // Test all five room surfaces and keep the closest hit.
        PaintTarget? bestTarget = null;
        var bestT = double.PositiveInfinity;

        // Floor: y = 0 plane, hit must land within [0,w] × [0,l].
        if (IntersectYPlane(ray, 0f) is { } tFloor && tFloor < bestT)
        {
            var hit = HitPoint(ray, tFloor);
            if (InBounds(hit.X, 0, w) && InBounds(hit.Z, 0, l))
            {
                bestTarget = new FloorPaintTarget();
                bestT = tFloor;
            }
        }

        // South wall: z = 0, hit within x[0,w] × y[0,h].
        if (IntersectZPlane(ray, 0f) is { } tSouth && tSouth < bestT)
        {
            var hit = HitPoint(ray, tSouth);
            if (InBounds(hit.X, 0, w) && InBounds(hit.Y, 0, h))
            {
                bestTarget = WallOrOpening(room, WallSide.South, hit.X, hit.Y);
                bestT = tSouth;
            }
        }

        // North wall: z = l.
        if (IntersectZPlane(ray, l) is { } tNorth && tNorth < bestT)
        {
            var hit = HitPoint(ray, tNorth);
            if (InBounds(hit.X, 0, w) && InBounds(hit.Y, 0, h))
            {
                bestTarget = WallOrOpening(room, WallSide.North, hit.X, hit.Y);
                bestT = tNorth;
            }
        }

        // West wall: x = 0, hit within z[0,l] × y[0,h].
        if (IntersectXPlane(ray, 0f) is { } tWest && tWest < bestT)
        {
            var hit = HitPoint(ray, tWest);
            if (InBounds(hit.Z, 0, l) && InBounds(hit.Y, 0, h))
            {
                bestTarget = WallOrOpening(room, WallSide.West, hit.Z, hit.Y);
                bestT = tWest;
            }
        }

        // East wall: x = w.
        if (IntersectXPlane(ray, w) is { } tEast && tEast < bestT)
        {
            var hit = HitPoint(ray, tEast);
            if (InBounds(hit.Z, 0, l) && InBounds(hit.Y, 0, h))
            {
                bestTarget = WallOrOpening(room, WallSide.East, hit.Z, hit.Y);
                bestT = tEast;
            }
        }

        return bestTarget;
    }

    /// <summary>
    /// If the hit point (expressed as an along-wall position and a height) falls within
    /// any opening on the given wall, returns that opening; otherwise returns the wall.
    /// </summary>
    private static PaintTarget WallOrOpening(Room room, WallSide side, float along, float height)
    {
        foreach (var opening in room.Openings)
        {
            if (opening.Wall != side) continue;
            var half = (float)(opening.OuterWidth.Meters / 2);
            var center = (float)opening.OffsetAlongWall.Meters;
            var sill = (float)opening.SillHeight.Meters;
            var top = (float)opening.Top.Meters;
            if (along >= center - half && along <= center + half && height >= sill && height <= top)
                return new OpeningPaintTarget(opening);
        }
        return new WallPaintTarget(side);
    }

    // ── Ray / plane intersections ────────────────────────────────────────────

    private static float? IntersectXPlane(Ray ray, float x)
    {
        var d = ray.Direction.X;
        if (MathF.Abs(d) < 1e-9f) return null;
        var t = (x - ray.Origin.X) / d;
        return t > 0 ? t : null;
    }

    private static float? IntersectZPlane(Ray ray, float z)
    {
        var d = ray.Direction.Z;
        if (MathF.Abs(d) < 1e-9f) return null;
        var t = (z - ray.Origin.Z) / d;
        return t > 0 ? t : null;
    }

    private static float? IntersectYPlane(Ray ray, float y)
    {
        var d = ray.Direction.Y;
        if (MathF.Abs(d) < 1e-9f) return null;
        var t = (y - ray.Origin.Y) / d;
        return t > 0 ? t : null;
    }

    private static Vector3 HitPoint(Ray ray, float t)
        => ray.Origin + ray.Direction * t;

    private static bool InBounds(float v, float lo, float hi) => v >= lo && v <= hi;

    // ── Item intersection (kept for backward compat / unit tests) ────────────

    /// <summary>
    /// Distance along the ray to where it enters <paramref name="item"/>'s box, or
    /// <c>null</c> if the ray misses. Returns 0 when the ray origin is inside the box.
    /// </summary>
    public static double? IntersectItem(Ray ray, RoomItem item)
    {
        var halfW = item.Width.Meters / 2.0;
        var halfD = item.Depth.Meters / 2.0;
        var height = item.Height.Meters;
        if (halfW <= 0 || halfD <= 0 || height <= 0)
            return null;

        var cx = item.Position.X;
        var cy = height / 2.0;
        var cz = item.Position.Y;

        var cos = Math.Cos(item.Rotation);
        var sin = Math.Sin(item.Rotation);

        var ox = ray.Origin.X - cx;
        var oy = ray.Origin.Y - cy;
        var oz = ray.Origin.Z - cz;
        var localOx = ox * cos + oz * sin;
        var localOy = oy;
        var localOz = -ox * sin + oz * cos;

        double dx = ray.Direction.X;
        double dy = ray.Direction.Y;
        double dz = ray.Direction.Z;
        var localDx = dx * cos + dz * sin;
        var localDy = dy;
        var localDz = -dx * sin + dz * cos;

        var tMin = double.NegativeInfinity;
        var tMax = double.PositiveInfinity;
        if (!Slab(localOx, localDx, halfW, ref tMin, ref tMax) ||
            !Slab(localOy, localDy, height / 2.0, ref tMin, ref tMax) ||
            !Slab(localOz, localDz, halfD, ref tMin, ref tMax))
        {
            return null;
        }

        if (tMax < 0)
            return null;
        return tMin >= 0 ? tMin : 0;
    }

    private static bool Slab(double origin, double direction, double half, ref double tMin, ref double tMax)
    {
        const double epsilon = 1e-9;
        if (Math.Abs(direction) < epsilon)
            return Math.Abs(origin) <= half;

        var t1 = (-half - origin) / direction;
        var t2 = (half - origin) / direction;
        if (t1 > t2) (t1, t2) = (t2, t1);

        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;
        return tMin <= tMax;
    }
}
