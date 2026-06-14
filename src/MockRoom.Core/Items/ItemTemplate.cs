using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// A catalog entry describing a furniture preset: its key, label, category,
/// default box dimensions, color, and optional part list. When <see cref="Parts"/>
/// is non-empty the template produces a <see cref="FurnitureItem"/>; otherwise a plain
/// <see cref="BoxItem"/> is created for backward compatibility.
/// </summary>
public sealed record ItemTemplate(
    string Id,
    string DisplayName,
    ItemCategory Category,
    Length DefaultWidth,
    Length DefaultDepth,
    Length DefaultHeight,
    string ColorHex,
    IReadOnlyList<FurniturePart>? Parts = null)
{
    /// <summary>
    /// Creates a fresh item from this template at the given position.
    /// Returns a <see cref="FurnitureItem"/> when <see cref="Parts"/> are defined,
    /// otherwise a <see cref="BoxItem"/>.
    /// </summary>
    public RoomItem CreateItem(Vec2 position)
    {
        if (Parts is { Count: > 0 })
            return new FurnitureItem(DisplayName, Category, DefaultWidth, DefaultDepth, DefaultHeight, Parts)
            {
                Position = position,
                ColorHex = ColorHex,
            };
        return new BoxItem(DisplayName, Category, DefaultWidth, DefaultDepth, DefaultHeight)
        {
            Position = position,
            ColorHex = ColorHex,
        };
    }
}
