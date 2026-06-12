using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Rooms;

/// <summary>
/// A door in one of the room's walls. Positioned by its center offset measured
/// from the start of the wall (X=0 for North/South, Y=0 for East/West).
/// The door's swing clearance consumes nearby floor and counts against usable area.
/// </summary>
public sealed class Door
{
    public Door(WallSide wall, Length offsetAlongWall, Length width, Length height, Length? swingClearance = null)
    {
        Id = Guid.NewGuid();
        Wall = wall;
        OffsetAlongWall = offsetAlongWall;
        Width = width;
        Height = height;
        // Default the swing depth to the door's width (a 90° swing reaches ~one door-width in).
        SwingClearance = swingClearance ?? width;
    }

    public Guid Id { get; init; }
    public WallSide Wall { get; set; }
    public Length OffsetAlongWall { get; set; }
    public Length Width { get; set; }
    public Length Height { get; set; }
    public Length SwingClearance { get; set; }

    /// <summary>
    /// The rectangular floor region the door's swing occupies, projected inward
    /// from its wall. Approximates the quarter-circle swing as a door-width ×
    /// clearance rectangle — simple, rectangular, and grid-friendly.
    /// </summary>
    public FootprintRect SwingFootprint(RoomDimensions dims)
    {
        var offset = OffsetAlongWall.Meters;
        var doorWidth = Width.Meters;
        var clearance = SwingClearance.Meters;
        var roomW = dims.Width.Meters;
        var roomL = dims.Length.Meters;

        return Wall switch
        {
            // North/South walls run along X: width along X, clearance into Y.
            WallSide.South => new FootprintRect(new Vec2(offset, clearance / 2), doorWidth, clearance),
            WallSide.North => new FootprintRect(new Vec2(offset, roomL - clearance / 2), doorWidth, clearance),
            // East/West walls run along Y: rotate 90° so the door width lies along Y.
            WallSide.West => new FootprintRect(new Vec2(clearance / 2, offset), doorWidth, clearance, Math.PI / 2),
            _ => new FootprintRect(new Vec2(roomW - clearance / 2, offset), doorWidth, clearance, Math.PI / 2),
        };
    }
}
