using System.Numerics;

namespace MockRoom.Core.Rendering;

/// <summary>
/// A world-space ray for 3D picking: an <see cref="Origin"/> and a unit
/// <see cref="Direction"/>. Produced by <see cref="Camera.ScreenPointToRay"/> and
/// consumed by <see cref="RayPicker"/>. GL-free so it stays NativeAOT-clean and
/// unit-testable.
/// </summary>
public readonly struct Ray(Vector3 origin, Vector3 direction)
{
    public Vector3 Origin { get; } = origin;

    /// <summary>The ray direction; expected to be normalized (1 unit ≈ 1 meter).</summary>
    public Vector3 Direction { get; } = direction;

    /// <summary>The point at distance <paramref name="t"/> along the ray.</summary>
    public Vector3 PointAt(float t) => Origin + Direction * t;
}
