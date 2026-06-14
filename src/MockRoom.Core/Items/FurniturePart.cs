namespace MockRoom.Core.Items;

/// <summary>
/// A single shaped sub-component of a <see cref="FurnitureItem"/>: a cuboid defined in the
/// item's local frame (before world rotation) with optional per-part color override.
/// </summary>
/// <param name="LocalX">Center X in the item's local frame, meters from item center.</param>
/// <param name="LocalY">Center Y in the item's local frame, meters from item center.</param>
/// <param name="BottomY">Height above the floor where this part starts, in meters.</param>
/// <param name="Width">Part width along the item's local X axis, in meters.</param>
/// <param name="Depth">Part depth along the item's local Y axis, in meters.</param>
/// <param name="Height">Part height, in meters.</param>
/// <param name="ColorHex">Hex color override, or null to inherit the parent item's color.</param>
public sealed record FurniturePart(
    double LocalX,
    double LocalY,
    double BottomY,
    double Width,
    double Depth,
    double Height,
    string? ColorHex = null);
