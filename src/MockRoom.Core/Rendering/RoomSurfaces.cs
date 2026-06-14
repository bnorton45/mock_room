using MockRoom.Core.Rooms;

namespace MockRoom.Core.Rendering;

/// <summary>
/// Per-surface visual material for a room. Each of the four wall faces has an
/// independent colour; the floor has its own colour. All walls share a single
/// metallic/roughness pair (per-wall material variation not exposed in the UI).
/// </summary>
public sealed record RoomSurfaces
{
    public string FloorColorHex { get; init; } = "#292F38";
    public float FloorMetallic { get; init; } = 0f;
    public float FloorRoughness { get; init; } = 0.9f;

    public string NorthWallColorHex { get; init; } = "#C7CCCE";
    public string SouthWallColorHex { get; init; } = "#C7CCCE";
    public string EastWallColorHex { get; init; } = "#C7CCCE";
    public string WestWallColorHex { get; init; } = "#C7CCCE";
    public float WallMetallic { get; init; } = 0f;
    public float WallRoughness { get; init; } = 0.85f;

    /// <summary>Returns the hex color for the given wall face.</summary>
    public string WallColorFor(WallSide side) => side switch
    {
        WallSide.North => NorthWallColorHex,
        WallSide.South => SouthWallColorHex,
        WallSide.East => EastWallColorHex,
        _ => WestWallColorHex,
    };

    /// <summary>Returns a copy of this record with the given wall face's color replaced.</summary>
    public RoomSurfaces WithWallColor(WallSide side, string hex) => side switch
    {
        WallSide.North => this with { NorthWallColorHex = hex },
        WallSide.South => this with { SouthWallColorHex = hex },
        WallSide.East => this with { EastWallColorHex = hex },
        _ => this with { WestWallColorHex = hex },
    };
}
