namespace MockRoom.Core.Persistence;

/// <summary>Serialisable form of a <see cref="MockRoom.Core.Items.FurniturePart"/>.</summary>
public sealed class FurniturePartDto
{
    public double LocalX { get; init; }
    public double LocalY { get; init; }
    public double BottomY { get; init; }
    public double W { get; init; }
    public double D { get; init; }
    public double H { get; init; }
    public string? ColorHex { get; init; }
}
