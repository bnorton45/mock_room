namespace MockRoom.Core.Rooms;

/// <summary>
/// Which end of a door opening the hinge sits on. <see cref="Start"/> is the lower
/// offset along the wall, <see cref="End"/> the higher. The leaf always swings into
/// the room; the hinge side only chooses which way the swing arc faces.
/// </summary>
public enum HingeSide
{
    Start,
    End,
}
