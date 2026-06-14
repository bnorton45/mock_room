using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// A furniture item composed of one or more <see cref="FurniturePart"/> cuboids defined in
/// the item's local frame. Each part is rendered individually in both the 2D plan and the 3D
/// viewport, producing recognisable shapes (sofa arms, chair back, table legs, etc.).
/// </summary>
public sealed class FurnitureItem(
    string name,
    ItemCategory category,
    Length width,
    Length depth,
    Length height,
    IReadOnlyList<FurniturePart> parts)
    : RoomItem(name, category, width, depth, height)
{
    public IReadOnlyList<FurniturePart> Parts { get; } = parts;

    public override string ShapeKind => "furniture";
}
