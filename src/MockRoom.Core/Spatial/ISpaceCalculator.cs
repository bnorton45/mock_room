using MockRoom.Core.Rooms;

namespace MockRoom.Core.Spatial;

/// <summary>
/// Computes how much of a room's floor is used versus free. Abstracted so the
/// fidelity (grid resolution, polygon clipping, etc.) can change without touching
/// callers in the UI.
/// </summary>
public interface ISpaceCalculator
{
    SpaceReport Compute(Room room);
}
