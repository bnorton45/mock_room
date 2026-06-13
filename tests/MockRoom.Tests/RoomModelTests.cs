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
    public void Door_FloorRegion_IsOneQuarterCircleHingedAtTheChosenEnd()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var door = new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0));

        var regions = door.FloorRegions(dims);
        var arc = Assert.Single(regions);
        Assert.Equal(0.9, arc.Radius, 6);
        Assert.Equal(2.55, arc.Hinge.X, 6);   // hinged at offset - width/2 (HingeSide.Start)
        Assert.Equal(0, arc.Hinge.Y, 6);
        Assert.Equal(Math.PI * 0.9 * 0.9 / 4, arc.Area.SquareMeters, 6);
        Assert.True(arc.Contains(new Vec2(2.7, 0.2)));   // swept into the room
        Assert.False(arc.Contains(new Vec2(2.4, 0.2)));  // behind the hinge, not swept
    }

    [Fact]
    public void Door_HingeSideEnd_FlipsTheArcToTheOtherEnd()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var door = new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0),
            hingeSide: HingeSide.End);

        var arc = Assert.Single(door.FloorRegions(dims));
        Assert.Equal(3.45, arc.Hinge.X, 6);  // hinged at offset + width/2
        Assert.True(arc.Contains(new Vec2(3.3, 0.2)));
        Assert.False(arc.Contains(new Vec2(3.6, 0.2)));
    }

    [Fact]
    public void ClosetDoor_FloorRegion_IsTwoHalfWidthLeaves()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var closet = new WallOpening(OpeningKind.ClosetDoor, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.5), Length.FromMeters(2.0));

        var regions = closet.FloorRegions(dims);
        Assert.Equal(2, regions.Count);
        Assert.All(regions, arc => Assert.Equal(0.75, arc.Radius, 6));
        Assert.Equal(2.25, regions[0].Hinge.X, 6);  // hinged at each end
        Assert.Equal(3.75, regions[1].Hinge.X, 6);
    }

    [Fact]
    public void Window_HasNoFloorRegionAndComputesItsTop()
    {
        var dims = RoomDimensions.FromMeters(6, 8, 2.5);
        var window = new WallOpening(OpeningKind.Window, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.2), Length.FromMeters(1.2),
            Length.FromMeters(0.9));

        Assert.Empty(window.FloorRegions(dims));
        Assert.Equal(2.1, window.Top.Meters, 6);  // sill + height (no frames)
    }

    [Fact]
    public void Window_Frames_AddAroundThePaneInOuterDimensionsAndTop()
    {
        var window = new WallOpening(OpeningKind.Window, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.0), Length.FromMeters(1.0),
            Length.FromMeters(0.8))
        {
            FrameTop = Length.FromMeters(0.1),
            FrameBottom = Length.FromMeters(0.2),
            FrameLeft = Length.FromMeters(0.05),
            FrameRight = Length.FromMeters(0.15),
        };

        Assert.Equal(1.2, window.OuterWidth.Meters, 6);   // pane 1.0 + 0.05 + 0.15
        Assert.Equal(1.3, window.OuterHeight.Meters, 6);  // pane 1.0 + 0.1 + 0.2
        Assert.Equal(2.1, window.Top.Meters, 6);          // sill 0.8 + outer height 1.3
    }

    [Fact]
    public void Door_HasNoFrameSoOuterEqualsPane()
    {
        var door = new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0));

        Assert.Equal(0.9, door.OuterWidth.Meters, 6);
        Assert.Equal(2.0, door.OuterHeight.Meters, 6);
        Assert.Equal(2.0, door.Top.Meters, 6);  // sill 0 + height
    }
}
