namespace MockRoom.Core.Rooms;

/// <summary>
/// The kind of opening cut into a wall. Doors and closet doors swing into the room
/// and consume floor by their swing arc; windows sit above a sill and consume no
/// floor. A closet door is modelled as two leaves (each half the width); a regular
/// door as a single leaf.
/// </summary>
public enum OpeningKind
{
    Door,
    ClosetDoor,
    Window,
}
