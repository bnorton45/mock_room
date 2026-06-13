namespace MockRoom.Core.Geometry;

/// <summary>
/// A region of the room floor that can be rasterized onto the occupancy grid: an
/// axis-aligned bounding box for cheap cell culling plus an exact point-containment
/// test. Implemented by item footprints (<see cref="FootprintRect"/>) and door swing
/// arcs (<see cref="SwingArc"/>), so the grid marks both through one code path.
/// </summary>
public interface IFloorRegion
{
    /// <summary>Axis-aligned bounding box as (minX, minY, maxX, maxY), in meters.</summary>
    (double MinX, double MinY, double MaxX, double MaxY) Bounds();

    /// <summary>True if the world-space point lies within the region.</summary>
    bool Contains(Vec2 point);
}
