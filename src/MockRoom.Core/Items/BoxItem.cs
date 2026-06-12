using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// The basic editable rectangular-box shape that backs every catalog preset.
/// Its width, depth, and height are freely adjustable, so a single primitive
/// covers tables, beds, dressers, and anything else the user adds.
/// </summary>
public sealed class BoxItem(string name, ItemCategory category, Length width, Length depth, Length height)
    : RoomItem(name, category, width, depth, height)
{
    public override string ShapeKind => "box";
}
