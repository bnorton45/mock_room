using System.Numerics;
using MockRoom.Core.Items;

namespace MockRoom.Core.Rendering;

/// <summary>
/// Picks the item a <see cref="Ray"/> hits in the 3D viewport. Each item is the
/// box the renderer draws: its footprint (rotated about the vertical axis by
/// <see cref="RoomItem.Rotation"/>) extruded from the floor to its height. Pure
/// and GL-free so it stays NativeAOT-clean and unit-testable.
/// </summary>
public static class RayPicker
{
    /// <summary>
    /// Returns the front-most item the ray enters, or <c>null</c> if it misses them
    /// all. "Front-most" is the smallest non-negative hit distance along the ray, so
    /// a click selects the item nearest the camera.
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
    /// Distance along the ray to where it enters <paramref name="item"/>'s box, or
    /// <c>null</c> if the ray misses. With a normalized ray direction the distance is
    /// in meters. Returns 0 when the ray origin is already inside the box.
    /// </summary>
    public static double? IntersectItem(Ray ray, RoomItem item)
    {
        var halfW = item.Width.Meters / 2.0;
        var halfD = item.Depth.Meters / 2.0;
        var height = item.Height.Meters;
        if (halfW <= 0 || halfD <= 0 || height <= 0)
            return null;

        var cx = item.Position.X;
        var cy = height / 2.0;        // box center sits at half its height above the floor
        var cz = item.Position.Y;     // floor Y maps to world Z

        // Rotate the ray into the box's local frame (inverse of the footprint's yaw
        // about the vertical axis), so the slab test runs against an axis-aligned box.
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
            return null; // box is entirely behind the ray origin
        return tMin >= 0 ? tMin : 0; // origin inside the box → distance 0
    }

    /// <summary>One ray-vs-slab clip step; narrows [tMin, tMax] and reports whether the ray still hits.</summary>
    private static bool Slab(double origin, double direction, double half, ref double tMin, ref double tMax)
    {
        const double epsilon = 1e-9;
        if (Math.Abs(direction) < epsilon)
            return Math.Abs(origin) <= half; // parallel to the slab: hit only if already inside it

        var t1 = (-half - origin) / direction;
        var t2 = (half - origin) / direction;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        if (t1 > tMin) tMin = t1;
        if (t2 < tMax) tMax = t2;
        return tMin <= tMax;
    }
}
