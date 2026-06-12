using MockRoom.Core.Rooms;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The serializable form of a <see cref="Door"/>: which wall it sits in and its
/// offset/size/swing in canonical meters.
/// </summary>
public sealed record DoorDto
{
    public Guid Id { get; init; }
    public WallSide Wall { get; init; }
    public double OffsetMeters { get; init; }
    public double WidthMeters { get; init; }
    public double HeightMeters { get; init; }
    public double SwingClearanceMeters { get; init; }
}
