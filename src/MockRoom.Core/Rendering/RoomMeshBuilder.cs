using System.Globalization;
using System.Numerics;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;

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

    private static readonly Mat FallbackItem = new(0.60f, 0.64f, 0.68f, 0f, 0.8f);

    /// <summary>Thickness of an open door leaf and a window frame ring, in meters.</summary>
    private const float LeafThickness = 0.04f;

    /// <summary>Width of the thin glazing bars within each half-pane, in meters.</summary>
    private const float GlazingBarThickness = 0.035f;

    /// <summary>Width of the thicker mid-rail that separates the top and bottom halves, in meters.</summary>
    private const float GlazingMidRailThickness = 0.07f;

    public static MeshData Build(Room room, PaintTarget? selected = null)
    {
        var dims = room.Dimensions;
        var w = (float)dims.Width.Meters;
        var l = (float)dims.Length.Meters;
        var h = (float)dims.Height.Meters;

        var surfaces = room.Surfaces;
        var floorMat = ParseMaterial(surfaces.FloorColorHex, surfaces.FloorMetallic, surfaces.FloorRoughness);

        var verts = new List<float>(1024);

        // Floor (faces up).
        AddQuad(verts,
            new Vector3(0, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, l), new Vector3(0, 0, l),
            new Vector3(0, 1, 0), floorMat);

        AddWalls(verts, room, w, l, h, surfaces);
        AddDoorLeaves(verts, room);

        foreach (var item in room.Items)
            AddItem(verts, item);

        return new MeshData(verts.ToArray(), verts.Count / FloatsPerVertex);
    }

    private static void AddWalls(List<float> verts, Room room, float w, float l, float h,
        RoomSurfaces surfaces)
    {
        // Each wall is parameterized by distance `a` along the wall and height `y`,
        // mapped into world space. Openings on the wall become voids along `a`, each
        // with a sill below (windows) and a lintel above.
        AddWall(verts, (a, y) => new Vector3(a, y, 0), w, h, new Vector3(0, 0, 1),
            ParseMat(surfaces, WallSide.South), room, WallSide.South);
        AddWall(verts, (a, y) => new Vector3(a, y, l), w, h, new Vector3(0, 0, -1),
            ParseMat(surfaces, WallSide.North), room, WallSide.North);
        AddWall(verts, (a, y) => new Vector3(0, y, a), l, h, new Vector3(1, 0, 0),
            ParseMat(surfaces, WallSide.West), room, WallSide.West);
        AddWall(verts, (a, y) => new Vector3(w, y, a), l, h, new Vector3(-1, 0, 0),
            ParseMat(surfaces, WallSide.East), room, WallSide.East);
    }

    private static Mat ParseMat(RoomSurfaces surfaces, WallSide side)
        => ParseMaterial(surfaces.WallColorFor(side), surfaces.WallMetallic, surfaces.WallRoughness);

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

    private static List<WallCut> OpeningsOn(Room room, WallSide side, float wallLength, float wallHeight)
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

            var openingMat = ParseMaterial(opening.ColorHex, 0f, 0.85f);

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
        Vector3 normal, Mat wallMat, Room room, WallSide side)
    {
        var openings = OpeningsOn(room, side, wallLength, wallHeight);
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
    private static void AddDoorLeaves(List<float> verts, Room room)
    {
        var dims = room.Dimensions;
        foreach (var opening in room.Openings)
        {
            if (!opening.Swings)
                continue;
            var leafMat = ParseMaterial(opening.ColorHex, 0f, 0.9f);
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
        var halfT = GlazingBarThickness / 2f;
        var halfMidT = GlazingMidRailThickness / 2f;
        var midX = (paneStart + paneEnd) * 0.5f;
        var midY = (paneSill + paneTop) * 0.5f;
        var quarterY = (paneSill + midY) * 0.5f;
        var threeQuarterY = (midY + paneTop) * 0.5f;

        AddPanel(verts, map, paneStart, paneEnd, midY - halfMidT, midY + halfMidT, normal, frameMat);
        AddPanel(verts, map, paneStart, paneEnd, quarterY - halfT, quarterY + halfT, normal, frameMat);
        AddPanel(verts, map, paneStart, paneEnd, threeQuarterY - halfT, threeQuarterY + halfT, normal, frameMat);

        AddPanel(verts, map, midX - halfT, midX + halfT, paneSill, quarterY - halfT, normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, quarterY + halfT, midY - halfMidT, normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, midY + halfMidT, threeQuarterY - halfT, normal, frameMat);
        AddPanel(verts, map, midX - halfT, midX + halfT, threeQuarterY + halfT, paneTop, normal, frameMat);
    }

    private static void AddPanel(List<float> verts, Func<float, float, Vector3> map,
        float aLo, float aHi, float yLo, float yHi, Vector3 normal, Mat color)
        => AddQuad(verts, map(aLo, yLo), map(aHi, yLo), map(aHi, yHi), map(aLo, yHi), normal, color);

    private static void AddItem(List<float> verts, RoomItem item)
    {
        if (item is FurnitureItem furniture)
            AddFurnitureItem(verts, furniture);
        else
        {
            var color = ParseMaterial(item.ColorHex, item.Metallic, item.Roughness);
            AddCuboid(verts, item.Footprint, (float)item.Height.Meters, color);
        }
    }

    private static void AddFurnitureItem(List<float> verts, FurnitureItem item)
    {
        var pos = item.Position;
        var rot = item.Rotation;
        var cos = Math.Cos(rot);
        var sin = Math.Sin(rot);

        foreach (var part in item.Parts)
        {
            var mat = ParseMaterial(part.ColorHex ?? item.ColorHex, item.Metallic, item.Roughness);
            // Rotate the part's local center offset into world space.
            var worldCenter = new Vec2(
                pos.X + part.LocalX * cos - part.LocalY * sin,
                pos.Y + part.LocalX * sin + part.LocalY * cos);
            var footprint = new FootprintRect(worldCenter, part.Width, part.Depth, rot);
            AddCuboid(verts, footprint, (float)part.Height, mat, (float)part.BottomY);
        }
    }

    /// <summary>Extrudes a floor footprint into a colored cuboid of the given height.</summary>
    private static void AddCuboid(List<float> verts, FootprintRect footprint, float h, Mat color,
        float bottomY = 0f)
    {
        var (p0, p1, p2, p3) = footprint.Corners();

        Vector3 Bot(Vec2 p) => new((float)p.X, bottomY, (float)p.Y);
        Vector3 Top(Vec2 p) => new((float)p.X, bottomY + h, (float)p.Y);

        AddQuad(verts, Top(p0), Top(p1), Top(p2), Top(p3), new Vector3(0, 1, 0), color);
        AddQuad(verts, Bot(p0), Bot(p3), Bot(p2), Bot(p1), new Vector3(0, -1, 0), color);

        AddSide(verts, p0, p1, h, color, bottomY);
        AddSide(verts, p1, p2, h, color, bottomY);
        AddSide(verts, p2, p3, h, color, bottomY);
        AddSide(verts, p3, p0, h, color, bottomY);
    }

    private static void AddSide(List<float> verts, Vec2 a, Vec2 b, float h, Mat color, float bottomY = 0f)
    {
        var a0 = new Vector3((float)a.X, bottomY, (float)a.Y);
        var b0 = new Vector3((float)b.X, bottomY, (float)b.Y);
        var a1 = new Vector3((float)a.X, bottomY + h, (float)a.Y);
        var b1 = new Vector3((float)b.X, bottomY + h, (float)b.Y);
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
