using System;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class RoomModelTests
{
    [Fact]
    public void Catalog_SeedsAllStandardPresets()
    {
        var catalog = new ItemCatalog();
        Assert.Equal(8, catalog.Templates.Count);
        foreach (var id in new[] { "table", "chair", "couch", "tv-stand", "coffee-table", "bed", "dresser", "chest" })
            Assert.True(catalog.TryGet(id, out _), $"missing template: {id}");
    }

    [Fact]
    public void Catalog_Create_UsesTemplateDefaults()
    {
        var catalog = new ItemCatalog();
        var bed = catalog.Create("bed", new Vec2(1, 2));

        Assert.Equal(ItemCategory.Bed, bed.Category);
        Assert.Equal(2.0, bed.Width.Meters, 6);
        Assert.Equal(1.5, bed.Depth.Meters, 6);
        Assert.Equal(3.0, bed.FootprintArea.SquareMeters, 6);
        Assert.Equal(new Vec2(1, 2), bed.Position);
    }

    [Fact]
    public void Catalog_AcceptsAdditionalTemplates()
    {
        var extra = new ItemTemplate("desk", "Desk", ItemCategory.Custom,
            Length.FromMeters(1.4), Length.FromMeters(0.7), Length.FromMeters(0.75), "#333333");
        var catalog = new ItemCatalog([extra]);

        Assert.True(catalog.TryGet("desk", out var t));
        Assert.Equal("Desk", t.DisplayName);
    }

    [Fact]
    public void Catalog_Create_UnknownId_Throws()
    {
        var catalog = new ItemCatalog();
        Assert.Throws<KeyNotFoundException>(() => catalog.Create("spaceship", Vec2.Zero));
    }

    [Fact]
    public void Room_AddAndRemoveItems()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        var catalog = new ItemCatalog();
        var bed = catalog.Create("bed", new Vec2(2, 2));

        room.AddItem(bed);
        Assert.Single(room.Items);
        Assert.True(room.RemoveItem(bed.Id));
        Assert.Empty(room.Items);
    }

    [Fact]
    public void Room_FloorArea_IsWidthTimesLength()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        Assert.Equal(48, room.FloorArea.SquareMeters, 6);
    }

    [Fact]
    public void Door_SwingFootprint_SitsInsideRoomAgainstWall()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var door = new Door(WallSide.South, Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0));

        var swing = door.SwingFootprint(dims);
        Assert.Equal(3.0, swing.Center.X, 6);          // centered at the offset
        Assert.True(swing.Center.Y > 0 && swing.Center.Y < 8); // projects into the room
        Assert.Equal(0.9 * 0.9, swing.Area.SquareMeters, 6);   // width × clearance (clearance defaults to width)
    }

    [Fact]
    public void Door_EastWall_SwingRotatedAlongLength()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var door = new Door(WallSide.East, Length.FromMeters(4), Length.FromMeters(1.0), Length.FromMeters(2.0));

        var swing = door.SwingFootprint(dims);
        var (minX, minY, maxX, maxY) = swing.Bounds();
        // Door width (1.0) lies along Y; clearance (1.0) along X near the east wall (X=6).
        Assert.Equal(1.0, maxY - minY, 6);
        Assert.True(maxX > 5.9); // hugs the east wall
    }
}
