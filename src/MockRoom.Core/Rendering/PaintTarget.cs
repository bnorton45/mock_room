using MockRoom.Core.Items;
using MockRoom.Core.Rooms;

namespace MockRoom.Core.Rendering;

/// <summary>
/// Discriminated union of everything the user can click-select and paint: the floor,
/// an individual wall face, a wall opening (door/window/closet), or a placed item.
/// </summary>
public abstract class PaintTarget { }

/// <summary>The room's floor surface.</summary>
public sealed class FloorPaintTarget : PaintTarget { }

/// <summary>One of the four wall faces, identified by its side.</summary>
public sealed class WallPaintTarget : PaintTarget
{
    public WallSide Side { get; }
    public WallPaintTarget(WallSide side) => Side = side;
}

/// <summary>A wall opening (door, closet door, or window frame).</summary>
public sealed class OpeningPaintTarget : PaintTarget
{
    public WallOpening Opening { get; }
    public OpeningPaintTarget(WallOpening opening) => Opening = opening;
}

/// <summary>A placed room item (box, etc.).</summary>
public sealed class ItemPaintTarget : PaintTarget
{
    public RoomItem Item { get; }
    public ItemPaintTarget(RoomItem item) => Item = item;
}
