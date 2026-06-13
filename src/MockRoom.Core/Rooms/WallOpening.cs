using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Rooms;

/// <summary>
/// An opening cut into one of the room's walls — a door, a closet door, or a window.
/// Positioned by its center offset measured from the start of the wall (X=0 for
/// North/South, Y=0 for East/West). Doors and closet doors swing into the room and
/// their swept arc(s) consume usable floor; windows sit above a <see cref="SillHeight"/>
/// and consume none. The opening's top (<see cref="Top"/> = sill + height) must never
/// exceed the wall height — the editor enforces and clamps this.
/// </summary>
public sealed class WallOpening
{
    public WallOpening(OpeningKind kind, WallSide wall, Length offsetAlongWall, Length width, Length height,
        Length? sillHeight = null, HingeSide hingeSide = HingeSide.Start)
    {
        Id = Guid.NewGuid();
        Kind = kind;
        Wall = wall;
        OffsetAlongWall = offsetAlongWall;
        Width = width;
        Height = height;
        // Doors and closet doors sit on the floor; windows default to no sill unless given one.
        SillHeight = sillHeight ?? Length.Zero;
        HingeSide = hingeSide;
    }

    public Guid Id { get; init; }
    public OpeningKind Kind { get; set; }
    public WallSide Wall { get; set; }
    public Length OffsetAlongWall { get; set; }

    /// <summary>The pane size: glass for a window, the leaf opening for a door/closet.</summary>
    public Length Width { get; set; }
    public Length Height { get; set; }
    public Length SillHeight { get; set; }
    public HingeSide HingeSide { get; set; }

    /// <summary>The paint color of this opening's leaves and frames (hex, e.g. "#D4C4B0").</summary>
    public string ColorHex { get; set; } = "#D4C4B0";

    // Frame widths added around the opening on each side.
    // Doors use top/left/right only (FrameBottom stays zero — the floor is the threshold).
    // Closet doors have no frame. Windows use all four sides.
    public Length FrameTop { get; set; } = Length.Zero;
    public Length FrameBottom { get; set; } = Length.Zero;
    public Length FrameLeft { get; set; } = Length.Zero;
    public Length FrameRight { get; set; } = Length.Zero;

    /// <summary>The opening's total width cut into the wall: pane plus its left/right frames.</summary>
    public Length OuterWidth => Width + FrameLeft + FrameRight;

    /// <summary>The opening's total height: pane plus its top/bottom frames.</summary>
    public Length OuterHeight => Height + FrameTop + FrameBottom;

    /// <summary>The height of the opening's top above the floor (sill + outer height).</summary>
    public Length Top => SillHeight + OuterHeight;

    /// <summary>True for kinds whose swing consumes floor (doors and closet doors).</summary>
    public bool Swings => Kind is OpeningKind.Door or OpeningKind.ClosetDoor;

    /// <summary>
    /// The floor region(s) the opening's swing occupies, projected into the room:
    /// one quarter-circle arc for a door, two (one per leaf) for a closet door, and
    /// none for a window.
    /// </summary>
    public IReadOnlyList<SwingArc> FloorRegions(RoomDimensions dims)
    {
        if (!Swings)
            return [];

        var (p0, p1, tangent, inward) = Span(dims);

        if (Kind == OpeningKind.ClosetDoor)
        {
            // Two leaves, each half the width, hinged at the two ends, swinging to meet.
            var leaf = Width.Meters / 2;
            return
            [
                new SwingArc(p0, leaf, tangent, inward),
                new SwingArc(p1, leaf, tangent * -1, inward),
            ];
        }

        // Single leaf hinged at the chosen end; the closed leaf points toward the far end.
        return HingeSide == HingeSide.Start
            ? [new SwingArc(p0, Width.Meters, tangent, inward)]
            : [new SwingArc(p1, Width.Meters, tangent * -1, inward)];
    }

    /// <summary>
    /// The opening's two endpoints in world space (<paramref name="dims"/>-relative),
    /// plus the unit tangent pointing from the start endpoint toward the end one and
    /// the unit inward normal pointing into the room.
    /// </summary>
    private (Vec2 Start, Vec2 End, Vec2 Tangent, Vec2 Inward) Span(RoomDimensions dims)
    {
        var off = OffsetAlongWall.Meters;
        var half = Width.Meters / 2;
        var s0 = off - half;
        var s1 = off + half;
        var w = dims.Width.Meters;
        var l = dims.Length.Meters;

        return Wall switch
        {
            // North/South walls run along X; tangent +X, inward toward the opposite wall.
            WallSide.South => (new Vec2(s0, 0), new Vec2(s1, 0), new Vec2(1, 0), new Vec2(0, 1)),
            WallSide.North => (new Vec2(s0, l), new Vec2(s1, l), new Vec2(1, 0), new Vec2(0, -1)),
            // East/West walls run along Y; tangent +Y.
            WallSide.West => (new Vec2(0, s0), new Vec2(0, s1), new Vec2(0, 1), new Vec2(1, 0)),
            _ => (new Vec2(w, s0), new Vec2(w, s1), new Vec2(0, 1), new Vec2(-1, 0)),
        };
    }
}
