using MockRoom.Core.Rooms;
using MockRoom.Core.Units;

namespace MockRoom.Core.Spatial;

/// <summary>
/// <see cref="ISpaceCalculator"/> that rasterizes every item footprint and door
/// swing arc onto an <see cref="OccupancyGrid"/>, then reports used and free area.
/// Overlapping regions are counted once. Free area is taken as the room total minus
/// used area (clamped at zero) so grid overhang past the room edge can't inflate the
/// result.
/// </summary>
public sealed class OccupancyGridSpaceCalculator(double cellSizeMeters = 0.05) : ISpaceCalculator
{
    public const double DefaultCellSizeMeters = 0.05;

    private readonly double _cellSize = cellSizeMeters;

    /// <summary>When true, door and closet-door swing arcs are counted as used floor.</summary>
    public bool IncludeDoorSwing { get; init; } = true;

    public SpaceReport Compute(Room room)
    {
        var dims = room.Dimensions;
        var grid = new OccupancyGrid(dims.Width.Meters, dims.Length.Meters, _cellSize);

        foreach (var item in room.Items)
            grid.Mark(item.Footprint);

        if (IncludeDoorSwing)
        {
            foreach (var opening in room.Openings)
            {
                foreach (var arc in opening.FloorRegions(dims))
                    grid.Mark(arc);
            }
        }

        var total = room.FloorArea;
        var used = grid.OccupiedArea;
        if (used > total) used = total;
        var free = (total - used).ClampNonNegative();

        return new SpaceReport(total, used, free, grid);
    }
}
