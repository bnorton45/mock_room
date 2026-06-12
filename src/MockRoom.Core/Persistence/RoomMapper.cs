using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Units;

namespace MockRoom.Core.Persistence;

/// <summary>
/// Converts between the domain <see cref="Room"/> aggregate and the serializable
/// <see cref="RoomDocument"/>. Keeps all length conversions in one place so the
/// repository and DTOs stay free of domain knowledge.
/// </summary>
public static class RoomMapper
{
    public static RoomDocument ToDocument(Room room)
    {
        var dims = room.Dimensions;
        return new RoomDocument
        {
            Version = 1,
            WidthMeters = dims.Width.Meters,
            LengthMeters = dims.Length.Meters,
            HeightMeters = dims.Height.Meters,
            PreferredUnits = room.PreferredUnits,
            Items = room.Items.Select(ToDto).ToList(),
            Doors = room.Doors.Select(ToDto).ToList(),
        };
    }

    public static Room FromDocument(RoomDocument document)
    {
        var dims = RoomDimensions.FromMeters(document.WidthMeters, document.LengthMeters, document.HeightMeters);
        var room = new Room(dims, document.PreferredUnits);
        foreach (var item in document.Items)
            room.AddItem(FromDto(item));
        foreach (var door in document.Doors)
            room.AddDoor(FromDto(door));
        return room;
    }

    private static ItemDto ToDto(RoomItem item) => new()
    {
        Id = item.Id,
        ShapeKind = item.ShapeKind,
        Name = item.Name,
        Category = item.Category,
        WidthMeters = item.Width.Meters,
        DepthMeters = item.Depth.Meters,
        HeightMeters = item.Height.Meters,
        PositionXMeters = item.Position.X,
        PositionYMeters = item.Position.Y,
        RotationRadians = item.Rotation,
        ColorHex = item.ColorHex,
    };

    private static RoomItem FromDto(ItemDto dto) =>
        // Only the box primitive exists today; unknown kinds fall back to a box.
        new BoxItem(dto.Name, dto.Category,
            Length.FromMeters(dto.WidthMeters),
            Length.FromMeters(dto.DepthMeters),
            Length.FromMeters(dto.HeightMeters))
        {
            Id = dto.Id,
            Position = new Vec2(dto.PositionXMeters, dto.PositionYMeters),
            Rotation = dto.RotationRadians,
            ColorHex = dto.ColorHex,
        };

    private static DoorDto ToDto(Door door) => new()
    {
        Id = door.Id,
        Wall = door.Wall,
        OffsetMeters = door.OffsetAlongWall.Meters,
        WidthMeters = door.Width.Meters,
        HeightMeters = door.Height.Meters,
        SwingClearanceMeters = door.SwingClearance.Meters,
    };

    private static Door FromDto(DoorDto dto) =>
        new(dto.Wall,
            Length.FromMeters(dto.OffsetMeters),
            Length.FromMeters(dto.WidthMeters),
            Length.FromMeters(dto.HeightMeters),
            Length.FromMeters(dto.SwingClearanceMeters))
        {
            Id = dto.Id,
        };
}
