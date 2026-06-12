using MockRoom.Core.Units;

namespace MockRoom.Core.Spatial;

/// <summary>
/// The result of a space calculation: the room's total floor area, how much is
/// used by items and door swings, how much remains free, and the occupancy grid
/// the figures were derived from (used to render the free-space overlay).
/// </summary>
public sealed record SpaceReport(Area Total, Area Used, Area Free, OccupancyGrid Grid)
{
    /// <summary>Fraction of the floor still free, in the range [0, 1].</summary>
    public double FreeFraction => Total.SquareMeters <= 0 ? 0 : Free.SquareMeters / Total.SquareMeters;
}
