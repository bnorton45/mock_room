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
}
