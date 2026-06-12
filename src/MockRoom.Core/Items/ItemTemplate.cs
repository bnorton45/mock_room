using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// A catalog entry describing a furniture preset: its key, label, category,
/// default box dimensions, and color. Adding a new furniture type is just a new
/// template registered with the catalog — no renderer or room changes required.
/// </summary>
public sealed record ItemTemplate(
    string Id,
    string DisplayName,
    ItemCategory Category,
    Length DefaultWidth,
    Length DefaultDepth,
    Length DefaultHeight,
    string ColorHex)
{
    /// <summary>Creates a fresh <see cref="BoxItem"/> from this template at the given position.</summary>
    public BoxItem CreateItem(Vec2 position)
        => new(DisplayName, Category, DefaultWidth, DefaultDepth, DefaultHeight)
        {
            Position = position,
            ColorHex = ColorHex,
        };
}
