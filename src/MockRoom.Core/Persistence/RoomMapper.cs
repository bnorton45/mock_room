using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rendering;
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
        var surfaces = room.Surfaces;
        return new RoomDocument
        {
            Version = RoomDocument.CurrentVersion,
            WidthMeters = dims.Width.Meters,
            LengthMeters = dims.Length.Meters,
            HeightMeters = dims.Height.Meters,
            PreferredUnits = room.PreferredUnits,
            Items = room.Items.Select(ToDto).ToList(),
            Openings = room.Openings.Select(ToDto).ToList(),
            FloorColorHex = surfaces.FloorColorHex,
            FloorMetallic = surfaces.FloorMetallic,
            FloorRoughness = surfaces.FloorRoughness,
            NorthWallColorHex = surfaces.NorthWallColorHex,
            SouthWallColorHex = surfaces.SouthWallColorHex,
            EastWallColorHex = surfaces.EastWallColorHex,
            WestWallColorHex = surfaces.WestWallColorHex,
            WallMetallic = surfaces.WallMetallic,
            WallRoughness = surfaces.WallRoughness,
        };
    }

    public static Room FromDocument(RoomDocument document)
    {
        var dims = RoomDimensions.FromMeters(document.WidthMeters, document.LengthMeters, document.HeightMeters);
        // Per-wall colors fall back to the legacy WallColorHex when the v3 fields are absent.
        var fallback = document.WallColorHex;
        var room = new Room(dims, document.PreferredUnits)
        {
            Surfaces = new RoomSurfaces
            {
                FloorColorHex = document.FloorColorHex,
                FloorMetallic = document.FloorMetallic,
                FloorRoughness = document.FloorRoughness,
                NorthWallColorHex = document.NorthWallColorHex ?? fallback,
                SouthWallColorHex = document.SouthWallColorHex ?? fallback,
                EastWallColorHex = document.EastWallColorHex ?? fallback,
                WestWallColorHex = document.WestWallColorHex ?? fallback,
                WallMetallic = document.WallMetallic,
                WallRoughness = document.WallRoughness,
            },
        };
        foreach (var item in document.Items)
            room.AddItem(FromDto(item));
        // Openings may be absent in older files; the legacy "Doors" list below covers those.
        foreach (var opening in document.Openings ?? [])
            room.AddOpening(FromDto(opening));
        // Upgrade any version-1 doors that an older file stored under "Doors".
        if (document.Doors is { Count: > 0 })
        {
            foreach (var door in document.Doors)
                room.AddOpening(FromLegacy(door));
        }
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
        Metallic = item.Metallic,
        Roughness = item.Roughness,
        Parts = item is FurnitureItem fi
            ? fi.Parts.Select(p => new FurniturePartDto
            {
                LocalX = p.LocalX,
                LocalY = p.LocalY,
                BottomY = p.BottomY,
                W = p.Width,
                D = p.Depth,
                H = p.Height,
                ColorHex = p.ColorHex,
            }).ToList()
            : null,
    };

    private static RoomItem FromDto(ItemDto dto)
    {
        if (dto.ShapeKind == "furniture" && dto.Parts is { Count: > 0 })
        {
            var parts = dto.Parts
                .Select(p => new FurniturePart(p.LocalX, p.LocalY, p.BottomY, p.W, p.D, p.H, p.ColorHex))
                .ToList();
            return new FurnitureItem(dto.Name, dto.Category,
                Length.FromMeters(dto.WidthMeters),
                Length.FromMeters(dto.DepthMeters),
                Length.FromMeters(dto.HeightMeters), parts)
            {
                Id = dto.Id,
                Position = new Vec2(dto.PositionXMeters, dto.PositionYMeters),
                Rotation = dto.RotationRadians,
                ColorHex = dto.ColorHex,
                Metallic = dto.Metallic,
                Roughness = dto.Roughness,
            };
        }
        // Box primitive and unknown kinds fall back to BoxItem.
        return new BoxItem(dto.Name, dto.Category,
            Length.FromMeters(dto.WidthMeters),
            Length.FromMeters(dto.DepthMeters),
            Length.FromMeters(dto.HeightMeters))
        {
            Id = dto.Id,
            Position = new Vec2(dto.PositionXMeters, dto.PositionYMeters),
            Rotation = dto.RotationRadians,
            ColorHex = dto.ColorHex,
            Metallic = dto.Metallic,
            Roughness = dto.Roughness,
        };
    }

    private static WallOpeningDto ToDto(WallOpening opening) => new()
    {
        Id = opening.Id,
        Kind = opening.Kind,
        Wall = opening.Wall,
        OffsetMeters = opening.OffsetAlongWall.Meters,
        WidthMeters = opening.Width.Meters,
        HeightMeters = opening.Height.Meters,
        SillMeters = opening.SillHeight.Meters,
        HingeSide = opening.HingeSide,
        FrameTopMeters = opening.FrameTop.Meters,
        FrameBottomMeters = opening.FrameBottom.Meters,
        FrameLeftMeters = opening.FrameLeft.Meters,
        FrameRightMeters = opening.FrameRight.Meters,
        ColorHex = opening.ColorHex,
    };

    private static WallOpening FromDto(WallOpeningDto dto) =>
        new(dto.Kind, dto.Wall,
            Length.FromMeters(dto.OffsetMeters),
            Length.FromMeters(dto.WidthMeters),
            Length.FromMeters(dto.HeightMeters),
            Length.FromMeters(dto.SillMeters),
            dto.HingeSide)
        {
            Id = dto.Id,
            FrameTop = Length.FromMeters(dto.FrameTopMeters),
            FrameBottom = Length.FromMeters(dto.FrameBottomMeters),
            FrameLeft = Length.FromMeters(dto.FrameLeftMeters),
            FrameRight = Length.FromMeters(dto.FrameRightMeters),
            ColorHex = dto.ColorHex,
        };

    private static WallOpening FromLegacy(LegacyDoorDto dto) =>
        new(OpeningKind.Door, dto.Wall,
            Length.FromMeters(dto.OffsetMeters),
            Length.FromMeters(dto.WidthMeters),
            Length.FromMeters(dto.HeightMeters))
        {
            Id = dto.Id,
        };
}
