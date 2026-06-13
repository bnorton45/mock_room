namespace MockRoom.Core.Rendering;

/// <summary>
/// Per-surface visual material for a room's floor and walls. Holds a hex colour
/// and Blinn-Phong material parameters (metallic, roughness) so every surface can
/// be independently styled by the user.
/// </summary>
public sealed record RoomSurfaces
{
    public string FloorColorHex { get; init; } = "#292F38";
    public float FloorMetallic  { get; init; } = 0f;
    public float FloorRoughness { get; init; } = 0.9f;

    public string WallColorHex { get; init; } = "#C7CCCE";
    public float WallMetallic  { get; init; } = 0f;
    public float WallRoughness { get; init; } = 0.85f;
}
