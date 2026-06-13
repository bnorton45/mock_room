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
    /// <summary>Floats per vertex: position xyz, normal xyz, color rgb.</summary>
    public const int FloatsPerVertex = 9;

    private static readonly (float R, float G, float B) FloorColor = (0.16f, 0.18f, 0.21f);
    private static readonly (float R, float G, float B) FreeFloorColor = (0.18f, 0.40f, 0.66f);
    private static readonly (float R, float G, float B) WallColor = (0.78f, 0.80f, 0.83f);
    private static readonly (float R, float G, float B) FallbackItemColor = (0.60f, 0.64f, 0.68f);
    private static readonly (float R, float G, float B) DoorLeafColor = (0.58f, 0.42f, 0.27f);
    private static readonly (float R, float G, float B) FrameColor = (0.90f, 0.90f, 0.92f);

    /// <summary>Thickness of an open door leaf and a window frame ring, in meters.</summary>
    private const float LeafThickness = 0.04f;

    /// <summary>Lifts the free-floor overlay a hair above the floor to avoid z-fighting.</summary>
    private const float FreeFloorLift = 0.003f;

    /// <summary>Blend fraction toward white for the selected item, so it reads as highlighted.</summary>
    private const float SelectedHighlight = 0.55f;

    /// <summary>
    /// Builds the room mesh. When <paramref name="freeFloor"/> is supplied, its free
    /// cells are painted as a blue overlay just above the floor (the usable-space
    /// highlight); pass <c>null</c> for a plain floor. The <paramref name="selected"/>
    /// item, if any, is brightened so the 3D view shows which item is selected.
    /// </summary>
    public static MeshData Build(Room room, OccupancyGrid? freeFloor = null, RoomItem? selected = null)
    {
        var dims = room.Dimensions;
        var w = (float)dims.Width.Meters;
        var l = (float)dims.Length.Meters;
        var h = (float)dims.Height.Meters;

        var verts = new List<float>(1024);

        // Floor (faces up).
        AddQuad(verts,
            new Vector3(0, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, l), new Vector3(0, 0, l),
            new Vector3(0, 1, 0), FloorColor);

        if (freeFloor is not null)
            AddFreeFloor(verts, freeFloor, w, l);

        AddWalls(verts, room, w, l, h);
        AddDoorLeaves(verts, room);

        foreach (var item in room.Items)
            AddItem(verts, item, ReferenceEquals(item, selected));

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

    private static void AddWalls(List<float> verts, Room room, float w, float l, float h)
    {
        // Each wall is parameterized by distance `a` along the wall and height `y`,
        // mapped into world space. Openings on the wall become voids along `a`, each
        // with a sill below (windows) and a lintel above.
        AddWall(verts, (a, y) => new Vector3(a, y, 0), w, h, new Vector3(0, 0, 1), WallColor,
            OpeningsOn(room, WallSide.South, w, h));
        AddWall(verts, (a, y) => new Vector3(a, y, l), w, h, new Vector3(0, 0, -1), WallColor,
            OpeningsOn(room, WallSide.North, w, h));
        AddWall(verts, (a, y) => new Vector3(0, y, a), l, h, new Vector3(1, 0, 0), WallColor,
            OpeningsOn(room, WallSide.West, l, h));
        AddWall(verts, (a, y) => new Vector3(w, y, a), l, h, new Vector3(-1, 0, 0), WallColor,
            OpeningsOn(room, WallSide.East, l, h));
    }

    /// <summary>
    /// One opening reduced to wall coordinates: the outer hole (cut into the wall) plus
    /// the inner pane rectangle (outer shrunk by the frame). For doors/closets the pane
    /// equals the outer hole and <see cref="HasFrame"/> is false.
    /// </summary>
    private readonly record struct WallCut(
        float Start, float End, float Sill, float Top,
        float PaneStart, float PaneEnd, float PaneSill, float PaneTop, bool HasFrame);

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

            // Pane = outer hole shrunk by the frame widths, clamped within the hole.
            var paneStart = Math.Clamp(start + (float)opening.FrameLeft.Meters, start, end);
            var paneEnd = Math.Clamp(end - (float)opening.FrameRight.Meters, paneStart, end);
            var paneSill = Math.Clamp(sill + (float)opening.FrameBottom.Meters, sill, top);
            var paneTop = Math.Clamp(top - (float)opening.FrameTop.Meters, paneSill, top);
            var hasFrame = paneStart > start || paneEnd < end || paneSill > sill || paneTop < top;

            cuts.Add(new WallCut(start, end, sill, top, paneStart, paneEnd, paneSill, paneTop, hasFrame));
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
        Vector3 normal, (float, float, float) color, List<WallCut> openings)
    {
        var cursor = 0f;
        foreach (var cut in openings)
        {
            if (cut.Start > cursor)
                AddPanel(verts, map, cursor, cut.Start, 0, wallHeight, normal, color);
            if (cut.Sill > 0)
                AddPanel(verts, map, cut.Start, cut.End, 0, cut.Sill, normal, color); // apron below a window
            if (cut.Top < wallHeight)
                AddPanel(verts, map, cut.Start, cut.End, cut.Top, wallHeight, normal, color); // lintel above

            if (cut.HasFrame)
            {
                // Frame ring around the void pane: left, right, bottom, top.
                AddPanel(verts, map, cut.Start, cut.PaneStart, cut.Sill, cut.Top, normal, FrameColor);
                AddPanel(verts, map, cut.PaneEnd, cut.End, cut.Sill, cut.Top, normal, FrameColor);
                AddPanel(verts, map, cut.PaneStart, cut.PaneEnd, cut.Sill, cut.PaneSill, normal, FrameColor);
                AddPanel(verts, map, cut.PaneStart, cut.PaneEnd, cut.PaneTop, cut.Top, normal, FrameColor);
            }

            cursor = cut.End;
        }

        if (cursor < wallLength)
            AddPanel(verts, map, cursor, wallLength, 0, wallHeight, normal, color);
    }

    /// <summary>Adds a thin open-leaf cuboid for each door/closet swing arc, at 90°.</summary>
    private static void AddDoorLeaves(List<float> verts, Room room)
    {
        var dims = room.Dimensions;
        foreach (var opening in room.Openings)
        {
            if (!opening.Swings)
                continue;
            var leafHeight = (float)opening.Height.Meters;
            foreach (var arc in opening.FloorRegions(dims))
            {
                // The fully-open leaf lies along DirB (into the room), length = the leaf radius.
                var center = arc.Hinge + arc.DirB * (arc.Radius / 2);
                var yaw = Math.Atan2(arc.DirB.Y, arc.DirB.X);
                var footprint = new FootprintRect(center, arc.Radius, LeafThickness, yaw);
                AddCuboid(verts, footprint, leafHeight, DoorLeafColor);
            }
        }
    }

    private static void AddPanel(List<float> verts, Func<float, float, Vector3> map,
        float aLo, float aHi, float yLo, float yHi, Vector3 normal, (float, float, float) color)
        => AddQuad(verts, map(aLo, yLo), map(aHi, yLo), map(aHi, yHi), map(aLo, yHi), normal, color);

    private static void AddItem(List<float> verts, RoomItem item, bool selected)
    {
        var color = ParseColor(item.ColorHex);
        if (selected)
            color = Lerp(color, (1f, 1f, 1f), SelectedHighlight);
        AddCuboid(verts, item.Footprint, (float)item.Height.Meters, color);
    }

    /// <summary>Extrudes a floor footprint into a colored cuboid of the given height.</summary>
    private static void AddCuboid(List<float> verts, FootprintRect footprint, float h, (float, float, float) color)
    {
        var (p0, p1, p2, p3) = footprint.Corners();

        Vector3 Floor(Vec2 p) => new((float)p.X, 0, (float)p.Y);
        Vector3 Top(Vec2 p) => new((float)p.X, h, (float)p.Y);

        // Top and bottom caps.
        AddQuad(verts, Top(p0), Top(p1), Top(p2), Top(p3), new Vector3(0, 1, 0), color);
        AddQuad(verts, Floor(p0), Floor(p3), Floor(p2), Floor(p1), new Vector3(0, -1, 0), color);

        // Four sides. Lighting is two-sided, so exact normal orientation is not critical.
        AddSide(verts, p0, p1, h, color);
        AddSide(verts, p1, p2, h, color);
        AddSide(verts, p2, p3, h, color);
        AddSide(verts, p3, p0, h, color);
    }

    private static void AddSide(List<float> verts, Vec2 a, Vec2 b, float h, (float, float, float) color)
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
        Vector3 normal, (float R, float G, float B) color)
    {
        AddVertex(verts, p0, normal, color);
        AddVertex(verts, p1, normal, color);
        AddVertex(verts, p2, normal, color);
        AddVertex(verts, p0, normal, color);
        AddVertex(verts, p2, normal, color);
        AddVertex(verts, p3, normal, color);
    }

    private static void AddVertex(List<float> verts, Vector3 p, Vector3 normal, (float R, float G, float B) color)
    {
        verts.Add(p.X);
        verts.Add(p.Y);
        verts.Add(p.Z);
        verts.Add(normal.X);
        verts.Add(normal.Y);
        verts.Add(normal.Z);
        verts.Add(color.R);
        verts.Add(color.G);
        verts.Add(color.B);
    }

    /// <summary>Linearly blends <paramref name="a"/> toward <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
    private static (float R, float G, float B) Lerp((float R, float G, float B) a, (float R, float G, float B) b, float t)
        => (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    /// <summary>Parses a "#RRGGBB" (or "RRGGBB") string to normalized rgb; falls back to grey.</summary>
    private static (float R, float G, float B) ParseColor(string hex)
    {
        var s = hex.AsSpan().Trim();
        if (s.Length > 0 && s[0] == '#')
            s = s[1..];
        if (s.Length != 6
            || !byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return FallbackItemColor;
        }

        return (r / 255f, g / 255f, b / 255f);
    }
}
