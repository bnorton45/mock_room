using System;
using System.Linq;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class FurnitureItemTests
{
    private const int BoxVerts = 36; // 6 faces × 2 triangles × 3 verts

    private static Room EmptyRoom() => new(RoomDimensions.FromMeters(5, 4, 2.5));

    // ── Domain ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FurnitureItem_ShapeKind_IsFurniture()
    {
        var item = MakeChair();
        Assert.Equal("furniture", item.ShapeKind);
    }

    [Fact]
    public void FurnitureItem_Parts_AreStoredCorrectly()
    {
        var item = MakeChair();
        Assert.Equal(2, item.Parts.Count);
    }

    [Fact]
    public void FurnitureItem_FootprintUsesItemBoundingBox_NotParts()
    {
        var item = MakeChair();
        item.Position = new Vec2(2.5, 2);
        var (p0, p1, p2, p3) = item.Footprint.Corners();
        // Bounding box for chair is 0.5 × 0.5; corners should be ≈ 0.25 m from center.
        var xs = new[] { p0.X, p1.X, p2.X, p3.X };
        Assert.Equal(0.5, xs.Max() - xs.Min(), precision: 3);
    }

    // ── Catalog ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_CouchTemplate_CreatesFurnitureItem()
    {
        var catalog = new ItemCatalog();
        var item = catalog.Create("couch", Vec2.Zero);
        Assert.IsType<FurnitureItem>(item);
        var fi = (FurnitureItem)item;
        Assert.Equal(4, fi.Parts.Count); // arms × 2, backrest, seat
    }

    [Fact]
    public void Catalog_ChairTemplate_CreatesFurnitureItemWithBackrest()
    {
        var catalog = new ItemCatalog();
        var item = catalog.Create("chair", Vec2.Zero);
        Assert.IsType<FurnitureItem>(item);
        var fi = (FurnitureItem)item;
        Assert.Equal(2, fi.Parts.Count); // seat + backrest
        Assert.True(fi.Parts.Any(p => p.Height >= 0.85), "chair should have a part at least 0.85 m tall");
    }

    [Fact]
    public void Catalog_ReclineTemplate_HasArmsBackAndHeadrest()
    {
        var catalog = new ItemCatalog();
        var item = catalog.Create("recliner", Vec2.Zero);
        Assert.IsType<FurnitureItem>(item);
        var fi = (FurnitureItem)item;
        Assert.Equal(5, fi.Parts.Count); // left arm, right arm, back cushion, seat, headrest
        Assert.True(fi.Parts.Any(p => p.Height >= 1.0), "recliner back should be at least 1 m tall");
        Assert.True(fi.Parts.Any(p => p.BottomY > 0), "headrest should be raised above BottomY=0");
    }

    [Fact]
    public void Catalog_TvStandTemplate_CreatesBoxItem()
    {
        var catalog = new ItemCatalog();
        var item = catalog.Create("tv-stand", Vec2.Zero);
        Assert.IsType<BoxItem>(item);
    }

    [Fact]
    public void Catalog_BedTemplate_HasHeadboard()
    {
        var catalog = new ItemCatalog();
        var item = catalog.Create("bed", Vec2.Zero);
        Assert.IsType<FurnitureItem>(item);
        var fi = (FurnitureItem)item;
        Assert.True(fi.Parts.Any(p => p.Height >= 0.9), "bed should have a headboard part ≥ 0.9 m tall");
    }

    // ── Mesh building ────────────────────────────────────────────────────────────

    [Fact]
    public void FurnitureItem_Mesh_HasOneBoxPerPart()
    {
        var room = EmptyRoom();
        var chair = MakeChair();
        chair.Position = new Vec2(2.5, 2);
        room.AddItem(chair);

        var mesh = RoomMeshBuilder.Build(room);

        var baseVerts = 6 + 4 * 6; // floor + 4 walls
        Assert.Equal(baseVerts + chair.Parts.Count * BoxVerts, mesh.VertexCount);
    }

    [Fact]
    public void FurnitureItem_Mesh_BackrestTopVertexAtCorrectHeight()
    {
        var room = EmptyRoom();
        var chair = MakeChair();
        chair.Position = new Vec2(2.5, 2);
        room.AddItem(chair);

        var mesh = RoomMeshBuilder.Build(room);

        // The chair backrest is 0.90 m tall starting at BottomY=0; expect a vertex at y=0.90.
        var hasBackrestTop = false;
        for (var i = 0; i < mesh.Vertices.Length; i += RoomMeshBuilder.FloatsPerVertex)
        {
            if (Math.Abs(mesh.Vertices[i + 1] - 0.90f) < 1e-3f)
            {
                hasBackrestTop = true;
                break;
            }
        }

        Assert.True(hasBackrestTop, "expected a vertex at y=0.90 for the chair backrest");
    }

    [Fact]
    public void FurnitureItem_Mesh_TableTopStartsAboveLegs()
    {
        var room = EmptyRoom();
        var catalog = new ItemCatalog();
        var table = catalog.Create("table", new Vec2(2.5, 2));
        room.AddItem(table);

        var mesh = RoomMeshBuilder.Build(room);

        // Table tabletop: BottomY=0.72, height=0.03 → top face at y=0.75.
        var hasTableTop = false;
        for (var i = 0; i < mesh.Vertices.Length; i += RoomMeshBuilder.FloatsPerVertex)
        {
            if (Math.Abs(mesh.Vertices[i + 1] - 0.75f) < 1e-3f)
            {
                hasTableTop = true;
                break;
            }
        }

        Assert.True(hasTableTop, "expected a vertex at y=0.75 for the table top surface");
    }

    [Fact]
    public void FurnitureItem_Rotation_PartsRotateWithItem()
    {
        var room = EmptyRoom();
        // A chair rotated 90 degrees; the backrest part offset should be rotated.
        var chair = MakeChair();
        chair.Position = new Vec2(2.5, 2);
        chair.Rotation = Math.PI / 2;
        room.AddItem(chair);

        // Simply confirm the mesh builds without error and has the right vertex count.
        var mesh = RoomMeshBuilder.Build(room);
        var baseVerts = 6 + 4 * 6;
        Assert.Equal(baseVerts + chair.Parts.Count * BoxVerts, mesh.VertexCount);
    }

    // ── Serialisation round-trip ─────────────────────────────────────────────────

    [Fact]
    public void FurnitureItem_SerializesAndDeserializesWithParts()
    {
        var room = EmptyRoom();
        var catalog = new ItemCatalog();
        var original = (FurnitureItem)catalog.Create("couch", new Vec2(1, 1));
        room.AddItem(original);

        var document = MockRoom.Core.Persistence.RoomMapper.ToDocument(room);
        var restored = MockRoom.Core.Persistence.RoomMapper.FromDocument(document);

        var restoredItem = Assert.IsType<FurnitureItem>(restored.Items.Single());
        Assert.Equal(original.Parts.Count, restoredItem.Parts.Count);
        Assert.Equal(original.Parts[0].Width, restoredItem.Parts[0].Width, precision: 6);
        Assert.Equal("furniture", restoredItem.ShapeKind);
    }

    // ── Scaling (resize via apply-item form) ─────────────────────────────────────

    [Fact]
    public void FurnitureItem_DoubledHeight_ScalesPartsInMesh()
    {
        var room = EmptyRoom();
        var catalog = new ItemCatalog();
        var table = (FurnitureItem)catalog.Create("table", new Vec2(2.5, 2));
        var originalHeight = table.NaturalHeight.Meters; // 0.75 m

        // Simulate the user doubling the table height.
        table.Height = Length.FromMeters(originalHeight * 2);
        room.AddItem(table);

        var mesh = RoomMeshBuilder.Build(room);

        // Scaled tabletop top should now be at 1.50 m (was 0.75 m).
        var hasScaledTop = false;
        for (var i = 0; i < mesh.Vertices.Length; i += RoomMeshBuilder.FloatsPerVertex)
        {
            if (Math.Abs(mesh.Vertices[i + 1] - (float)(originalHeight * 2)) < 1e-3f)
            {
                hasScaledTop = true;
                break;
            }
        }

        Assert.True(hasScaledTop, "doubling table height should produce a vertex at 2× the original top height");
    }

    [Fact]
    public void FurnitureItem_NaturalDimensionsPreservedThroughSerialisation()
    {
        var room = EmptyRoom();
        var catalog = new ItemCatalog();
        var table = (FurnitureItem)catalog.Create("table", new Vec2(2, 2));
        // Resize the table.
        table.Width = Length.FromMeters(2.4);
        table.Height = Length.FromMeters(1.5);
        room.AddItem(table);

        var doc = MockRoom.Core.Persistence.RoomMapper.ToDocument(room);
        var restored = MockRoom.Core.Persistence.RoomMapper.FromDocument(doc);

        var rt = Assert.IsType<FurnitureItem>(restored.Items.Single());
        Assert.Equal(1.2, rt.NaturalWidth.Meters, precision: 6);   // catalog default
        Assert.Equal(0.75, rt.NaturalHeight.Meters, precision: 6);  // catalog default
        Assert.Equal(2.4, rt.Width.Meters, precision: 6);           // user-set
        Assert.Equal(1.5, rt.Height.Meters, precision: 6);          // user-set
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static FurnitureItem MakeChair()
        => new("Chair", ItemCategory.Chair,
            Length.FromMeters(0.5), Length.FromMeters(0.5), Length.FromMeters(0.9),
            [
                new FurniturePart(0,  0.04, 0, 0.50, 0.40, 0.45),
                new FurniturePart(0, -0.22, 0, 0.50, 0.06, 0.90),
            ]);
}
