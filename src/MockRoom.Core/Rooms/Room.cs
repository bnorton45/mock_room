using MockRoom.Core.Items;
using MockRoom.Core.Units;

namespace MockRoom.Core.Rooms;

/// <summary>
/// A room being designed: its dimensions, the items placed in it, the doors in
/// its walls, and the unit system the user prefers for display. This is the root
/// aggregate the UI binds to and the space calculator and persistence operate on.
/// </summary>
public sealed class Room
{
    private readonly List<RoomItem> _items = [];
    private readonly List<Door> _doors = [];

    public Room(RoomDimensions dimensions, UnitSystem preferredUnits = UnitSystem.Metric)
    {
        Dimensions = dimensions;
        PreferredUnits = preferredUnits;
    }

    public RoomDimensions Dimensions { get; set; }
    public UnitSystem PreferredUnits { get; set; }

    public IReadOnlyList<RoomItem> Items => _items;
    public IReadOnlyList<Door> Doors => _doors;

    public Area FloorArea => Dimensions.FloorArea;

    public void AddItem(RoomItem item) => _items.Add(item);
    public bool RemoveItem(RoomItem item) => _items.Remove(item);
    public bool RemoveItem(Guid id) => _items.RemoveAll(i => i.Id == id) > 0;
    public void ClearItems() => _items.Clear();

    public void AddDoor(Door door) => _doors.Add(door);
    public bool RemoveDoor(Door door) => _doors.Remove(door);
    public bool RemoveDoor(Guid id) => _doors.RemoveAll(d => d.Id == id) > 0;
    public void ClearDoors() => _doors.Clear();
}
