using MockRoom.Core.Rooms;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The version-1 serialized door shape, kept only so existing <c>.mockroom</c> files
/// still load. Newer documents write <see cref="WallOpeningDto"/> instead; the mapper
/// upgrades any legacy doors it finds into door-kind openings.
/// </summary>
public sealed record LegacyDoorDto
{
    public Guid Id { get; init; }
    public WallSide Wall { get; init; }
    public double OffsetMeters { get; init; }
    public double WidthMeters { get; init; }
    public double HeightMeters { get; init; }
    public double SwingClearanceMeters { get; init; }
}
