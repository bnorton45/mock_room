using MockRoom.Core.Units;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The serializable form of a <see cref="Rooms.Room"/>: dimensions in canonical
/// meters, the preferred unit system, and the placed items and doors. This is the
/// root document written to and read from <c>.mockroom</c> files. <see cref="Version"/>
/// lets future loaders migrate older files.
/// </summary>
public sealed record RoomDocument
{
    public int Version { get; init; } = 1;

    public double WidthMeters { get; init; }
    public double LengthMeters { get; init; }
    public double HeightMeters { get; init; }

    public UnitSystem PreferredUnits { get; init; }

    public List<ItemDto> Items { get; init; } = [];
    public List<DoorDto> Doors { get; init; } = [];
}
