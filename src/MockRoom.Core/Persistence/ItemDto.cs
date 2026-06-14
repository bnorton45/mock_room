using MockRoom.Core.Items;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The serializable form of a <see cref="RoomItem"/>. All lengths are canonical
/// meters and rotation is radians. <see cref="ShapeKind"/> mirrors
/// <see cref="RoomItem.ShapeKind"/> so future shapes can be reconstructed.
/// </summary>
public sealed record ItemDto
{
    public Guid Id { get; init; }
    public string ShapeKind { get; init; } = "box";
    public string Name { get; init; } = "";
    public ItemCategory Category { get; init; }

    public double WidthMeters { get; init; }
    public double DepthMeters { get; init; }
    public double HeightMeters { get; init; }

    public double PositionXMeters { get; init; }
    public double PositionYMeters { get; init; }
    public double RotationRadians { get; init; }

    public string ColorHex { get; init; } = "#9AA0A6";
    public float Metallic { get; init; } = 0f;
    public float Roughness { get; init; } = 0.8f;

    /// <summary>Non-null for <see cref="MockRoom.Core.Items.FurnitureItem"/>; null for plain boxes.</summary>
    public List<FurniturePartDto>? Parts { get; init; }

    /// <summary>
    /// Original catalog dimensions the parts were designed for.
    /// Zero in older saved files — the loader falls back to the current dimensions,
    /// which is correct when the item has never been resized.
    /// </summary>
    public double NaturalWidthMeters { get; init; }
    public double NaturalDepthMeters { get; init; }
    public double NaturalHeightMeters { get; init; }
}
