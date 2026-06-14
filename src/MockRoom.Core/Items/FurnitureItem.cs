using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// A furniture item composed of one or more <see cref="FurniturePart"/> cuboids defined in
/// the item's local frame. Each part is rendered individually in both the 2D plan and the 3D
/// viewport, producing recognisable shapes (sofa arms, chair back, table legs, etc.).
/// </summary>
/// <remarks>
/// Parts are defined in absolute meters for the item's <em>natural</em> (catalog-default)
/// dimensions. When the user resizes the item via the apply-item form the renderers scale
/// each part by <c>current / natural</c> on each axis so the silhouette stretches correctly.
/// <see cref="NaturalWidth"/>, <see cref="NaturalDepth"/>, and <see cref="NaturalHeight"/>
/// are set to <paramref name="width"/>/<paramref name="depth"/>/<paramref name="height"/> on
/// first creation and preserved through serialisation so the ratio stays consistent.
/// </remarks>
public sealed class FurnitureItem(
    string name,
    ItemCategory category,
    Length width,
    Length depth,
    Length height,
    IReadOnlyList<FurniturePart> parts,
    Length? naturalWidth = null,
    Length? naturalDepth = null,
    Length? naturalHeight = null)
    : RoomItem(name, category, width, depth, height)
{
    public IReadOnlyList<FurniturePart> Parts { get; } = parts;

    /// <summary>The Width the parts were originally designed for (catalog default).</summary>
    public Length NaturalWidth { get; } = naturalWidth ?? width;

    /// <summary>The Depth the parts were originally designed for (catalog default).</summary>
    public Length NaturalDepth { get; } = naturalDepth ?? depth;

    /// <summary>The Height the parts were originally designed for (catalog default).</summary>
    public Length NaturalHeight { get; } = naturalHeight ?? height;

    public override string ShapeKind => "furniture";
}
