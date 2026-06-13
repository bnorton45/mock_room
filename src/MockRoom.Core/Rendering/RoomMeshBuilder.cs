using System.Globalization;
using System.Numerics;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Spatial;

namespace MockRoom.Core.Rendering;

/// <summary>
/// Turns a <see cref="Room"/> into a flat world-space triangle mesh for the 3D
/// viewport: the floor, the four walls (with door openings cut out), and one
/// extruded cuboid per placed item. Everything is baked into world coordinates so
/// the renderer needs only a view·projection matrix — no per-object transforms.
/// Pure and GL-free so it stays NativeAOT-clean and unit-testable.
/// </summary>
public static class RoomMeshBuilder
{
    /// <summary>Floats per vertex: position xyz, normal xyz, color rgb, metallic, roughness.</summary>
    public const int FloatsPerVertex = 11;

    private readonly record struct Mat(float R, float G, float B, float Metallic, float Roughness);

    private static readonly Mat FreeFloorColor = new(0.18f, 0.40f, 0.66f, 0f, 0.9f);
    private static readonly Mat FallbackItem   = new(0.60f, 0.64f, 0.68f, 0f, 0.8f);

    /// <summary>Thickness of an open door leaf and a window frame ring, in meters.</summary>
    private const float LeafThickness = 0.04f;

    /// <summary>Width of the thin glazing bars within each half-pane, in meters.</summary>
    private const float GlazingBarThickness = 0.035f;

    /// <summary>Width of the thicker mid-rail that separates the top and bottom halves, in meters.</summary>
    private const float GlazingMidRailThickness = 0.07f;

    /// <summary>Lifts the free-floor overlay a hair above the floor to avoid z-fighting.</summary>
    private const float FreeFloorLift = 0.003f;

    /// <summary>Blend fraction toward white for the selected surface, so it reads as highlighted.</summary>
    private const float SelectedHighlight = 0.55f;

    /// <summary>
    /// Builds the room mesh. When <paramref name="freeFloor"/> is supplied its free
    /// cells are painted as a blue overlay just above the floor. <paramref name="selected"/>
    /// identifies the currently selected paint target, which is brightened in the mesh.
    /// </summary>
    public static MeshData Build(Room room, OccupancyGrid? freeFloor = null, PaintTarget? selected = null)
    {
        var dims = room.Dimensions;
        var w = (float)dims.Width.Meters;
        var l = (float)dims.Length.Meters;
        var h = (float)dims.Height.Meters;

        var surfaces = room.Surfaces;
        var isFloorSelected = selected is FloorPaintTarget;
        var floorMat = ParseMaterial(surfaces.FloorColorHex, surfaces.FloorMetallic, surfaces.FloorRoughness);
        if (isFloorSelected) floorMat = Brighten(floorMat);

        var verts = new List<float>(1024);

        // Floor (faces up).
        AddQuad(verts,
            new Vector3(0, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, l), new Vector3(0, 0, l),
            new Vector3(0, 1, 0), floorMat);

        if (freeFloor is not null)
            AddFreeFloor(verts, freeFloor, w, l);

        AddWalls(verts, room, w, l, h, surfaces, selected);
        AddDoorLeaves(verts, room, selected);

        foreach (var item in room.Items)
        {
            var isItemSelected = selected is ItemPaintTarget { Item: var si } && ReferenceEquals(item, si);
            AddItem(verts, item, isItemSelected);
        }

        return new MeshData(verts.ToArray(), verts.Count / FloatsPerVertex);
    }

    private static void AddFreeFloor(List<float> verts, OccupancyGrid grid, float w, float l)
    {
        var cell = (float)grid.CellSize;
        var up = new Vector3(0, 1, 0);
        foreach (var (row, colStart, colEnd) in grid.FreeRuns())
        {
            var x0 = colStart * cell;
            var x1 = MathF.Min(colEnd * cell, w);
            var z0 = row * cell;
            var z1 = MathF.Min((row + 1) * cell, l);
            AddQuad(verts,
                new Vector3(x0, FreeFloorLift, z0), new Vector3(x1, FreeFloorLift, z0),
                new Vector3(x1, FreeFloorLift, z1), new Vector3(x0, FreeFloorLift, z1),
                up, FreeFloorColor);
        }
    }

    private static void AddWalls(List<float> verts, Room room, float w, float l, float h,
        RoomSurfaces surfaces, PaintTarget? selected)
    {
        var selectedSide = (selected as WallPaintTarget)?.Side;
        var selectedOpeningId = (selected as OpeningPaintTarget)?.Opening.Id;

        // Each wall is parameterized by distance `a` along the wall and height `y`,
        // mapped into world space. Openings on the wall become voids along `a`, each
        // with a sill below (windows) and a lintel above.
        AddWall(verts, (a, y) => new Vector3(a, y, 0), w, h, new Vector3(0, 0, 1),
            ParseMat(surfaces, WallSide.South, selectedSide), room, WallSide.South, selectedOpeningId);
        AddWall(verts, (a, y) => new Vector3(a, y, l), w, h, new Vector3(0, 0, -1),
            ParseMat(surfaces, WallSide.North, selectedSide), room, WallSide.North, selectedOpeningId);
        AddWall(verts, (a, y) => new Vector3(0, y, a), l, h, new Vector3(1, 0, 0),
            ParseMat(surfaces, WallSide.West, selectedSide), room, WallSide.West, selectedOpeningId);
        AddWall(verts, (a, y) => new Vector3(w, y, a), l, h, new Vector3(-1, 0, 0),
            ParseMat(surfaces, WallSide.East, selectedSide), room, WallSide.East, selectedOpeningId);
    }

    private static Mat ParseMat(RoomSurfaces surfaces, WallSide side, WallSide? selectedSide)
    {
        var mat = ParseMaterial(surfaces.WallColorFor(side), surfaces.WallMetallic, surfaces.WallRoughness);
        return selectedSide == side ? Brighten(mat) : mat;
    }

    /// <summary>
    /// One opening reduced to wall coordinates: the outer hole (cut into the wall) plus
    /// the inner pane rectangle (outer shrunk by the frame). For doors/closets the pane
    /// equals the outer hole and <see cref="HasFrame"/> is false.
    /// Windows additionally set <see cref="HasGlazing"/> to draw the internal bar grid.
    /// </summary>
    private readonly record struct WallCut(
        float Start, float End, float Sill, float Top,
        float PaneStart, float PaneEnd, float PaneSill, float PaneTop,
        bool HasFrame, bool HasGlazing,
        Mat OpeningMat, Guid OpeningId);

    private static List<WallCut> OpeningsOn(Room room, WallSide side, float wallLength, float wallHeight,
        Guid? selectedOpeningId)
    {
        var cuts = new List<WallCut>();
        foreach (var opening in room.Openings)
        {
            if (opening.Wall != side)
                continue;
            var half = (float)(opening.OuterWidth.Meters / 2);
            var center = (float)opening.OffsetAlongWall.Meters;
            var start = Math.Clamp(center - half, 0f, wallLength);
            var end = Math.Clamp(center + half, 0f, wallLength);
            var sill = Math.Clamp((float)opening.SillHeight.Meters, 0f, wallHeight);
            var top = Math.Clamp((float)opening.Top.Meters, sill, wallHeight);
            if (end <= start || top <= sill)
                continue;

            var paneStart = Math.Clamp(start + (float)opening.FrameLeft.Meters, start, end);
            var paneEnd = Math.Clamp(end - (float)opening.FrameRight.Meters, paneStart, end);
            var paneSill = Math.Clamp(sill + (float)opening.FrameBottom.Meters, sill, top);
            var paneTop = Math.Clamp(top - (float)opening.FrameTop.Meters, paneSill, top);
            var hasFrame = paneStart > start || paneEnd < end || paneSill > sill || paneTop < top;
            var hasGlazing = opening.Kind == OpeningKind.Window;

            var baseMat = ParseMaterial(opening.ColorHex, 0f, 0.85f);
            var openingMat = selectedOpeningId == opening.Id ? Brighten(baseMat) : baseMat;

            cuts.Add(new WallCut(start, end, sill, top, paneStart, paneEnd, paneSill, paneTop,
                hasFrame, hasGlazing, openingMat, opening.Id));
        }

        cuts.Sort((x, y) => x.Start.CompareTo(y.Start));
        return cuts;
    }

    /// <summary>
    /// Emits a wall as full-height solid panels between openings, a sill apron below each
    /// opening and a lintel above it, and — for framed openings — a frame ring around the
    /// (void) pane. <paramref name="map"/> turns (alongWall, height) into world space.
    /// </summary>
    private static void AddWall(List<float> verts, Func<float, float, Vector3> map, float wallLength, float wallHeight,
        Vector3 normal, Mat wallMat, Room room, WallSide side, Guid? selectedOpeningId)
    {
        var openings = OpeningsOn(room, side, wallLength, wallHeight, selectedOpeningId);
        var cursor = 0f;
        foreach (var cut in openings)
        {
            if (cut.Start > cursor)
                AddPanel(verts, map, cursor, cut.Start, 0, wallHeight, normal, wallMat);
            if (cut.Sill > 0)
                AddPanel(verts, map, cut.Start, cut.End, 0, cut.Sill, normal, wallMat);
            if (cut.Top < wallHeight)
                AddPanel(verts, map, cut.Start, cut.End, cut.Top, wallHeight, normal, wallMat);

            if (cut.HasFrame)
            {
                AddPanel(verts, map, cut.Start, cut.PaneStart, cut.Sill, cut.Top, normal, cut.OpeningMat);
                AddPanel(verts, map, cut.PaneEnd, cut.End, cut.Sill, cut.Top, normal, cut.OpeningMat);
                AddPanel(verts, map, cut.PaneStart, cut.PaneEnd, cut.Sill, cut.PaneSill, normal, cut.OpeningMat);
                AddPanel(verts, map, cut.PaneStart, cut.PaneEnd, cut.PaneTop, cut.Top, normal, cut.OpeningMat);
            }

            if (cut.HasGlazing)
                AddGlazingBars(verts, map, normal, cut.PaneStart, cut.PaneEnd, cut.PaneSill, cut.PaneTop, cut.OpeningMat);

            cursor = cut.End;
        }

        if (cursor < wallLength)
            AddPanel(verts, map, cursor, wallLength, 0, wallHeight, normal, wallMat);
    }

    /// <summary>Adds a thin open-leaf cuboid for each door/closet swing arc, at 90°.</summary>
    private static void AddDoorLeaves(List<float> verts, Room room, PaintTarget? selected)
    {
        var selectedOpeningId = (selected as OpeningPaintTarget)?.Opening.Id;
        var dims = room.Dimensions;
        foreach (var opening in room.Openings)
        {
            if (!opening.Swings)
                continue;
            var baseMat = ParseMaterial(opening.ColorHex, 0f, 0.9f);
            var leafMat = selectedOpeningId == opening.Id ? Brighten(baseMat) : baseMat;
            var leafHeight = (float)opening.Height.Meters;
            foreach (var arc in opening.FloorRegions(dims))
            {
                var center = arc.Hinge + arc.DirB * (arc.Radius / 2);
                var yaw = Math.Atan2(arc.DirB.Y, arc.DirB.X);
                var footprint = new FootprintRect(center, arc.Radius, LeafThickness, yaw);
                AddCuboid(verts, footprint, leafHeight, leafMat);
            }
        }
    }

    /// <summary>
    /// Draws the internal glazing bar grid for a window pane.
    /// </summary>
    private static void AddGlazingBars(List<float> verts, Func<float, float, Vector3> map, Vector3 normal,
        float paneStart, float paneEnd, float paneSill, float paneTop, Mat frameMat)
    {
        var halfT    = GlazingBarThickness / 2f;
        var halfMidT = GlazingMidRailThickness / 2f;
        var midX = (paneStart + paneEnd) * 0.5f;
        var midY = (paneSill + paneTop) * 0.5f;
        var quarterY = (paneSill + midY) * 0.5f;
        var threeQuarterY = (midY + paneTop) * 0.5f;

        AddPanel(verts, map, paneStart, paneEnd, midY - halfMidT,       midY + halfMidT,       normal, frameMat);
        AddPanel(verts, map, paneStart, paneEnd, quarterY - halfT,      quarterY + halfT,      normal, frameMat);
        AddPanel(verts, map, paneStart, paneEnd, threeQuarterY - halfT, threeQuarterY + halfT, normal, frameMat);

        AddPanel(verts, map, midX - halfT, midX + halfT, paneSill,              quarterY - halfT,      normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, quarterY + halfT,      midY - halfMidT,       normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, midY + halfMidT,       threeQuarterY - halfT, normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, threeQuarterY + halfT, paneTop,               normal, frameMat);
    }

    private static void AddPanel(List<float> verts, Func<float, float, Vector3> map,
        float aLo, float aHi, float yLo, float yHi, Vector3 normal, Mat color)
        => AddQuad(verts, map(aLo, yLo), map(aHi, yLo), map(aHi, yHi), map(aLo, yHi), normal, color);

    private static void AddItem(List<float> verts, RoomItem item, bool selected)
    {
        var color = ParseMaterial(item.ColorHex, item.Metallic, item.Roughness);
        if (selected) color = Brighten(color);
        AddCuboid(verts, item.Footprint, (float)item.Height.Meters, color);
    }

    /// <summary>Extrudes a floor footprint into a colored cuboid of the given height.</summary>
    private static void AddCuboid(List<float> verts, FootprintRect footprint, float h, Mat color)
    {
        var (p0, p1, p2, p3) = footprint.Corners();

        Vector3 Floor(Vec2 p) => new((float)p.X, 0, (float)p.Y);
        Vector3 Top(Vec2 p) => new((float)p.X, h, (float)p.Y);

        AddQuad(verts, Top(p0), Top(p1), Top(p2), Top(p3), new Vector3(0, 1, 0), color);
        AddQuad(verts, Floor(p0), Floor(p3), Floor(p2), Floor(p1), new Vector3(0, -1, 0), color);

        AddSide(verts, p0, p1, h, color);
        AddSide(verts, p1, p2, h, color);
        AddSide(verts, p2, p3, h, color);
        AddSide(verts, p3, p0, h, color);
    }

    private static void AddSide(List<float> verts, Vec2 a, Vec2 b, float h, Mat color)
    {
        var a0 = new Vector3((float)a.X, 0, (float)a.Y);
        var b0 = new Vector3((float)b.X, 0, (float)b.Y);
        var a1 = new Vector3((float)a.X, h, (float)a.Y);
        var b1 = new Vector3((float)b.X, h, (float)b.Y);
        var edge = b0 - a0;
        var normal = Vector3.Cross(new Vector3(0, 1, 0), edge);
        AddQuad(verts, a0, b0, b1, a1, normal, color);
    }

    private static void AddQuad(List<float> verts, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        Vector3 normal, Mat color)
    {
        AddVertex(verts, p0, normal, color);
        AddVertex(verts, p1, normal, color);
        AddVertex(verts, p2, normal, color);
        AddVertex(verts, p0, normal, color);
        AddVertex(verts, p2, normal, color);
        AddVertex(verts, p3, normal, color);
    }

    private static void AddVertex(List<float> verts, Vector3 p, Vector3 normal, Mat m)
    {
        verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
        verts.Add(normal.X); verts.Add(normal.Y); verts.Add(normal.Z);
        verts.Add(m.R); verts.Add(m.G); verts.Add(m.B);
        verts.Add(m.Metallic); verts.Add(m.Roughness);
    }

    private static Mat Brighten(Mat color) => new(
        Lerp(color.R, 1f, SelectedHighlight),
        Lerp(color.G, 1f, SelectedHighlight),
        Lerp(color.B, 1f, SelectedHighlight),
        color.Metallic, color.Roughness);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static Mat ParseMaterial(string hex, float metallic, float roughness)
    {
        var s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
            s = s[1..];
        if (s.Length != 6
            || !byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return FallbackItem with { Metallic = metallic, Roughness = roughness };
        }

        return new Mat(r / 255f, g / 255f, b / 255f, metallic, roughness);
    }
}
