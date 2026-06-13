using MockRoom.Core.Units;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The serializable form of a <see cref="Rooms.Room"/>: dimensions in canonical
/// meters, the preferred unit system, the placed items, and the wall openings (doors,
/// closet doors, windows). This is the root document written to and read from
/// <c>.mockroom</c> files. <see cref="Version"/> lets loaders migrate older files;
/// version-1 files stored openings under <see cref="Doors"/>.
/// </summary>
public sealed record RoomDocument
{
    public const int CurrentVersion = 2;

    public int Version { get; init; } = CurrentVersion;

    public double WidthMeters { get; init; }
    public double LengthMeters { get; init; }
    public double HeightMeters { get; init; }

    public UnitSystem PreferredUnits { get; init; }

    public List<ItemDto> Items { get; init; } = [];
    public List<WallOpeningDto> Openings { get; init; } = [];

    // Room surface materials (floor and walls). Absent in older files → defaults apply.
    public string FloorColorHex { get; init; } = "#292F38";
    public float  FloorMetallic  { get; init; } = 0f;
    public float  FloorRoughness { get; init; } = 0.9f;

    // Legacy single wall color, written by v2 files. When loading, per-wall fields take
    // precedence; this is used as a fallback so old files still render correctly.
    public string WallColorHex  { get; init; } = "#C7CCCE";
    public float  WallMetallic   { get; init; } = 0f;
    public float  WallRoughness  { get; init; } = 0.85f;

    // Per-wall colors written by v3+ files. Null means "fall back to WallColorHex".
    public string? NorthWallColorHex { get; init; }
    public string? SouthWallColorHex { get; init; }
    public string? EastWallColorHex  { get; init; }
    public string? WestWallColorHex  { get; init; }

    /// <summary>Version-1 doors, read for backward compatibility. Never written.</summary>
    public List<LegacyDoorDto>? Doors { get; init; }
}
