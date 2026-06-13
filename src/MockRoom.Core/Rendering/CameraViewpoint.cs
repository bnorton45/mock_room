namespace MockRoom.Core.Rendering;

/// <summary>
/// A named first-person camera position on the room floor plan.
/// <see cref="X"/> and <see cref="Z"/> are world-space floor coordinates;
/// <see cref="Yaw"/> is the initial look direction in radians (same convention as
/// <see cref="Camera.Yaw"/>: 0 looks along +Z, positive values rotate clockwise).
/// </summary>
public readonly record struct CameraViewpoint(string Name, float X, float Z, float Yaw);
