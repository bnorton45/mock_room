using System;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Rendering;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class RoomMeshBuilderTests
{
    private const int FloorVerts = 6;     // 1 quad
    private const int WallVerts = 6;      // 1 quad per undoored wall
    private const int BoxVerts = 36;      // 6 faces

    private static Room EmptyRoom() => new(RoomDimensions.FromMeters(5, 4, 2.5));

    [Fact]
    public void EmptyRoom_HasFloorPlusFourWalls()
    {
        var mesh = RoomMeshBuilder.Build(EmptyRoom());

        Assert.Equal(FloorVerts + 4 * WallVerts, mesh.VertexCount); // 30
        Assert.Equal(mesh.VertexCount * RoomMeshBuilder.FloatsPerVertex, mesh.Vertices.Length);
    }

    [Fact]
    public void AddingABox_AddsACuboid()
    {
        var room = EmptyRoom();
        var side = Length.FromMeters(1);
        room.AddItem(new BoxItem("Box", ItemCategory.Custom, side, side, side) { Position = new Vec2(2.5, 2) });

        var mesh = RoomMeshBuilder.Build(room);

        Assert.Equal(FloorVerts + 4 * WallVerts + BoxVerts, mesh.VertexCount); // 66
    }

    [Fact]
    public void CenteredDoor_SplitsItsWallIntoThreePanels()
    {
        var room = EmptyRoom();
        // A 1 m wide, 2 m tall door centered on the 5 m south wall (does not reach the 2.5 m ceiling).
        room.AddOpening(new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(2.5), Length.FromMeters(1), Length.FromMeters(2)));

        var mesh = RoomMeshBuilder.Build(room);

        // Floor + 3 plain walls + doored wall (left + right + lintel = 3 quads) + open leaf (a cuboid).
        var dooredWallVerts = 3 * 6;
        Assert.Equal(FloorVerts + 3 * WallVerts + dooredWallVerts + BoxVerts, mesh.VertexCount); // 78
    }

    [Fact]
    public void CenteredWindow_AddsApronLintelAndFrameRingToItsWall()
    {
        var room = EmptyRoom();
        // A framed pane (1 m + 0.1 m frame all round → 1.2 m outer) above a 0.8 m sill,
        // outer top 2.0 m (below the 2.5 m ceiling), centered. Windows do not swing → no leaf.
        room.AddOpening(new WallOpening(OpeningKind.Window, WallSide.South,
            Length.FromMeters(2.5), Length.FromMeters(1.0), Length.FromMeters(1.0), Length.FromMeters(0.8))
        {
            FrameTop = Length.FromMeters(0.1),
            FrameBottom = Length.FromMeters(0.1),
            FrameLeft = Length.FromMeters(0.1),
            FrameRight = Length.FromMeters(0.1),
        });

        var mesh = RoomMeshBuilder.Build(room);

        // Floor + 3 plain walls + windowed wall:
        //   left, right, apron, lintel = 4 quads
        //   4-quad frame ring = 4 quads
        //   glazing bars = 7 quads (3 horizontal + 4 vertical segments)
        //   total = 15 quads
        var windowedWallVerts = 15 * 6;
        Assert.Equal(FloorVerts + 3 * WallVerts + windowedWallVerts, mesh.VertexCount); // 114
    }

    [Fact]
    public void ItemCuboid_ExtrudesToItemHeight()
    {
        var room = EmptyRoom();
        var footprint = Length.FromMeters(1);
        var height = Length.FromMeters(0.75);
        room.AddItem(new BoxItem("Stool", ItemCategory.Custom, footprint, footprint, height) { Position = new Vec2(2.5, 2) });

        var mesh = RoomMeshBuilder.Build(room);

        // Some vertex should sit exactly at the box's top (y = 0.75), proving extrusion by Height.
        var hasTop = false;
        for (var i = 0; i < mesh.Vertices.Length; i += RoomMeshBuilder.FloatsPerVertex)
        {
            if (Math.Abs(mesh.Vertices[i + 1] - 0.75f) < 1e-4f)
            {
                hasTop = true;
                break;
            }
        }

        Assert.True(hasTop);
    }

    [Fact]
    public void SelectedItem_DoesNotAlterThe3DMesh()
    {
        var room = EmptyRoom();
        var side = Length.FromMeters(1);
        var item = new BoxItem("Box", ItemCategory.Custom, side, side, side) { Position = new Vec2(2.5, 2) };
        room.AddItem(item);

        var plain = RoomMeshBuilder.Build(room);
        var withSelection = RoomMeshBuilder.Build(room, selected: new ItemPaintTarget(item));

        // Selection no longer bakes a highlight into vertex colors — 3D always shows the true color.
        Assert.Equal(plain.VertexCount, withSelection.VertexCount);
        Assert.Equal(plain.Vertices, withSelection.Vertices);
    }

    [Fact]
    public void SelectingAnUnrelatedItem_LeavesTheMeshUnchanged()
    {
        var room = EmptyRoom();
        var side = Length.FromMeters(1);
        room.AddItem(new BoxItem("Box", ItemCategory.Custom, side, side, side) { Position = new Vec2(2.5, 2) });
        var notInRoom = new BoxItem("Other", ItemCategory.Custom, side, side, side);

        var plain = RoomMeshBuilder.Build(room);
        var withSelection = RoomMeshBuilder.Build(room, selected: new ItemPaintTarget(notInRoom));

        Assert.Equal(plain.Vertices, withSelection.Vertices);
    }

    [Fact]
    public void CustomSurfaceColors_ProduceSameVertexCount()
    {
        var room = EmptyRoom();
        room.Surfaces = new RoomSurfaces
        {
            FloorColorHex = "#FF0000",
            FloorMetallic = 0.5f,
            FloorRoughness = 0.3f,
            NorthWallColorHex = "#00FF00",
            SouthWallColorHex = "#00FF00",
            EastWallColorHex = "#00FF00",
            WestWallColorHex = "#00FF00",
            WallMetallic = 1.0f,
            WallRoughness = 0.1f,
        };

        var mesh = RoomMeshBuilder.Build(room);

        // Surface colors only change vertex data, not vertex count.
        Assert.Equal(FloorVerts + 4 * WallVerts, mesh.VertexCount);
        Assert.Equal(mesh.VertexCount * RoomMeshBuilder.FloatsPerVertex, mesh.Vertices.Length);
    }

    [Fact]
    public void ItemMaterial_MetallicAndRoughnessAreBakedIntoVertices()
    {
        var room = EmptyRoom();
        var side = Length.FromMeters(1);
        var item = new BoxItem("Box", ItemCategory.Custom, side, side, side)
        {
            Position = new Vec2(2.5, 2),
            Metallic = 0.9f,
            Roughness = 0.1f,
        };
        room.AddItem(item);

        var mesh = RoomMeshBuilder.Build(room);

        // The item occupies the last BoxVerts vertices. Each vertex has
        // metallic at index 9 and roughness at index 10 within its stride.
        var itemVertOffset = (FloorVerts + 4 * WallVerts) * RoomMeshBuilder.FloatsPerVertex;
        var foundMetallic = false;
        var foundRoughness = false;
        for (var i = itemVertOffset; i < mesh.Vertices.Length; i += RoomMeshBuilder.FloatsPerVertex)
        {
            if (Math.Abs(mesh.Vertices[i + 9] - 0.9f) < 1e-5f) foundMetallic = true;
            if (Math.Abs(mesh.Vertices[i + 10] - 0.1f) < 1e-5f) foundRoughness = true;
        }

        Assert.True(foundMetallic, "metallic not baked into item vertices");
        Assert.True(foundRoughness, "roughness not baked into item vertices");
    }

}
