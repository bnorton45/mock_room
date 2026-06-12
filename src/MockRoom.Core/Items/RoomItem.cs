using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// Base class for anything placed on the room floor. Holds the common placement
/// and dimension data; concrete shapes (currently only <see cref="BoxItem"/>)
/// describe their geometry via <see cref="ShapeKind"/>. New shapes subclass this
/// without changing the room, catalog, or space-calculation code.
/// </summary>
public abstract class RoomItem
{
    protected RoomItem(string name, ItemCategory category, Length width, Length depth, Length height)
    {
        Id = Guid.NewGuid();
        Name = name;
        Category = category;
        Width = width;
        Depth = depth;
        Height = height;
    }

    public Guid Id { get; init; }
    public string Name { get; set; }
    public ItemCategory Category { get; set; }

    /// <summary>Footprint width (local X). Editable so the user can resize the shape.</summary>
    public Length Width { get; set; }

    /// <summary>Footprint depth (local Y). Editable.</summary>
    public Length Depth { get; set; }

    /// <summary>Vertical extent. Editable; does not affect floor-area math.</summary>
    public Length Height { get; set; }

    /// <summary>Center of the footprint on the floor plane, in meters.</summary>
    public Vec2 Position { get; set; }

    /// <summary>Yaw rotation about the vertical axis, in radians.</summary>
    public double Rotation { get; set; }

    /// <summary>Display color as a "#RRGGBB" hex string.</summary>
    public string ColorHex { get; set; } = "#9AA0A6";

    /// <summary>Discriminator for rendering and serialization, e.g. "box".</summary>
    public abstract string ShapeKind { get; }

    public FootprintRect Footprint => new(Position, Width.Meters, Depth.Meters, Rotation);

    public Area FootprintArea => Footprint.Area;
}
