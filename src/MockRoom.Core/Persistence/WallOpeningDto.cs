using MockRoom.Core.Rooms;

namespace MockRoom.Core.Persistence;

/// <summary>
/// The serializable form of a <see cref="WallOpening"/>: its kind, which wall it sits
/// in, and its offset/size/sill/hinge in canonical meters.
/// </summary>
public sealed record WallOpeningDto
{
    public Guid Id { get; init; }
    public OpeningKind Kind { get; init; }
    public WallSide Wall { get; init; }
    public double OffsetMeters { get; init; }
    public double WidthMeters { get; init; }
    public double HeightMeters { get; init; }
    public double SillMeters { get; init; }
    public HingeSide HingeSide { get; init; }

    // Window frame widths in meters (zero for doors/closets).
    public double FrameTopMeters { get; init; }
    public double FrameBottomMeters { get; init; }
    public double FrameLeftMeters { get; init; }
    public double FrameRightMeters { get; init; }

    /// <summary>Paint color of the opening's leaves and frames. Defaults to a natural wood tone.</summary>
    public string ColorHex { get; init; } = "#D4C4B0";
}
