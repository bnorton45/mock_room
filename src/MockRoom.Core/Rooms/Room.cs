using MockRoom.Core.Items;
using MockRoom.Core.Units;

namespace MockRoom.Core.Rooms;

/// <summary>
/// A room being designed: its dimensions, the items placed in it, the openings
/// (doors, closet doors, windows) in its walls, and the unit system the user prefers
/// for display. This is the root aggregate the UI binds to and the space calculator
/// and persistence operate on.
/// </summary>
public sealed class Room
{
    private readonly List<RoomItem> _items = [];
    private readonly List<WallOpening> _openings = [];

    public Room(RoomDimensions dimensions, UnitSystem preferredUnits = UnitSystem.Metric)
    {
        Dimensions = dimensions;
        PreferredUnits = preferredUnits;
    }

    public RoomDimensions Dimensions { get; set; }
    public UnitSystem PreferredUnits { get; set; }

    public IReadOnlyList<RoomItem> Items => _items;
    public IReadOnlyList<WallOpening> Openings => _openings;

    public Area FloorArea => Dimensions.FloorArea;

    public void AddItem(RoomItem item) => _items.Add(item);
    public bool RemoveItem(RoomItem item) => _items.Remove(item);
    public bool RemoveItem(Guid id) => _items.RemoveAll(i => i.Id == id) > 0;
    public void ClearItems() => _items.Clear();

    public void AddOpening(WallOpening opening) => _openings.Add(opening);
    public bool RemoveOpening(WallOpening opening) => _openings.Remove(opening);
    public bool RemoveOpening(Guid id) => _openings.RemoveAll(o => o.Id == id) > 0;
    public void ClearOpenings() => _openings.Clear();
}
