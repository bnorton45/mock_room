using MockRoom.Core.Units;

namespace MockRoom.Core.Rooms;

/// <summary>
/// The interior dimensions of a rectangular room. Width runs along X, Length
/// along Y (depth), Height is the ceiling height. Stored as canonical lengths.
/// </summary>
public readonly struct RoomDimensions(Length width, Length length, Length height)
{
    public Length Width { get; } = width;
    public Length Length { get; } = length;
    public Length Height { get; } = height;

    /// <summary>The total interior floor area (Width × Length).</summary>
    public Area FloorArea => Width * Length;

    public static RoomDimensions FromMeters(double width, double length, double height)
        => new(Units.Length.FromMeters(width), Units.Length.FromMeters(length), Units.Length.FromMeters(height));
}
