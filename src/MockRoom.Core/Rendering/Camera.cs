using System.Numerics;
using MockRoom.Core.Rooms;

namespace MockRoom.Core.Rendering;

/// <summary>
/// View/projection math for the 3D viewport, kept free of any GL types so it is
/// pure and unit-testable. Right-handed, matching <c>Vec3</c>'s convention
/// (X = width, Y = up, Z = length); floor coordinates <c>(x, y)</c> map to world
/// <c>(x, _, y)</c>.
///
/// In <see cref="CameraMode.FirstPerson"/> the eye is at (<see cref="FirstPersonX"/>,
/// <see cref="EyeHeight"/>, <see cref="FirstPersonZ"/>) and only rotates
/// (<see cref="Yaw"/>/<see cref="Pitch"/>). In <see cref="CameraMode.Orbit"/> the eye
/// circles the room center at <see cref="OrbitDistance"/>.
/// </summary>
public sealed class Camera
{
    /// <summary>Pitch limit (~89°): keeps the view from flipping at straight up/down.</summary>
    public const float MaxPitch = 1.5533431f;

    /// <summary>Lowest the first-person eye can sit, in meters (just above the floor).</summary>
    public const float MinEyeHeight = 0.1f;

    private const float MinOrbitDistance = 0.5f;
    private const float MaxOrbitDistance = 200f;
    private const float DefaultFov = MathF.PI / 3f; // 60°
    private const float NearPlane = 0.05f;
    private const float FarPlane = 200f;
    private const float ViewpointInset = 0.15f; // meters inside from wall/corner

    private float _pitch;
    private float _orbitPitch = 0.5f;
    private float _orbitDistance = 8f;

    public Camera(RoomDimensions dimensions)
    {
        SetRoom(dimensions);
        FirstPersonX = RoomWidth / 2f;
        FirstPersonZ = RoomLength / 2f;
    }

    /// <summary>Builds a camera framing the given room with a sensible default orbit distance.</summary>
    public static Camera FromRoom(RoomDimensions dimensions)
    {
        var camera = new Camera(dimensions);
        camera.OrbitDistance = camera.RoomDiagonal * 1.1f;
        return camera;
    }

    public CameraMode Mode { get; set; } = CameraMode.FirstPerson;

    public float RoomWidth { get; private set; }
    public float RoomLength { get; private set; }
    public float RoomHeight { get; private set; }

    /// <summary>First-person floor X position in world space. Defaults to room center.</summary>
    public float FirstPersonX { get; set; }

    /// <summary>First-person floor Z position in world space. Defaults to room center.</summary>
    public float FirstPersonZ { get; set; }

    /// <summary>First-person heading, in radians. 0 looks along +Z (down the room length).</summary>
    public float Yaw { get; set; }

    /// <summary>First-person up/down look, in radians, clamped to ±<see cref="MaxPitch"/>.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -MaxPitch, MaxPitch);
    }

    /// <summary>First-person eye height in meters; the effective value is clamped to the room.</summary>
    public float EyeHeight { get; set; } = 1.6f;

    public float OrbitYaw { get; set; }

    public float OrbitPitch
    {
        get => _orbitPitch;
        set => _orbitPitch = Math.Clamp(value, -MaxPitch, MaxPitch);
    }

    public float OrbitDistance
    {
        get => _orbitDistance;
        set => _orbitDistance = Math.Clamp(value, MinOrbitDistance, MaxOrbitDistance);
    }

    /// <summary>Updates the framed room (e.g. after the user edits dimensions).</summary>
    public void SetRoom(RoomDimensions dimensions)
    {
        RoomWidth = (float)dimensions.Width.Meters;
        RoomLength = (float)dimensions.Length.Meters;
        RoomHeight = (float)dimensions.Height.Meters;
    }

    /// <summary>The point the orbit camera looks at: the center of the room volume.</summary>
    public Vector3 OrbitTarget => new(RoomWidth / 2f, RoomHeight / 2f, RoomLength / 2f);

    private float RoomDiagonal => MathF.Sqrt(RoomWidth * RoomWidth + RoomLength * RoomLength + RoomHeight * RoomHeight);

    /// <summary>The first-person eye height after clamping to the room interior.</summary>
    private float EffectiveEyeHeight => Math.Clamp(EyeHeight, MinEyeHeight, MathF.Max(MinEyeHeight, RoomHeight));

    /// <summary>The camera position in world space for the current mode.</summary>
    public Vector3 EyePosition => Mode == CameraMode.FirstPerson
        ? new Vector3(FirstPersonX, EffectiveEyeHeight, FirstPersonZ)
        : OrbitTarget + OrbitOffset();

    private Vector3 OrbitOffset()
    {
        var cp = MathF.Cos(_orbitPitch);
        return new Vector3(cp * MathF.Sin(OrbitYaw), MathF.Sin(_orbitPitch), cp * MathF.Cos(OrbitYaw)) * _orbitDistance;
    }

    public Matrix4x4 View
    {
        get
        {
            if (Mode == CameraMode.FirstPerson)
            {
                var cp = MathF.Cos(_pitch);
                var forward = new Vector3(MathF.Sin(Yaw) * cp, MathF.Sin(_pitch), MathF.Cos(Yaw) * cp);
                var eye = EyePosition;
                return Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitY);
            }

            return Matrix4x4.CreateLookAt(EyePosition, OrbitTarget, Vector3.UnitY);
        }
    }

    public Matrix4x4 Projection(float aspect)
        => Matrix4x4.CreatePerspectiveFieldOfView(DefaultFov, MathF.Max(aspect, 0.01f), NearPlane, FarPlane);

    /// <summary>
    /// Combined view·projection in System.Numerics row-vector order. Uploaded to GL
    /// verbatim (transpose = false): the row-major field layout is read by GL as the
    /// column-major transpose, which is exactly the matrix a column-vector shader needs.
    /// </summary>
    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    /// <summary>
    /// Projects a world-space point into NDC (Normalized Device Coordinates, [-1, 1]).
    /// Returns null when the point is behind the near plane (clip W ≤ 0), which means
    /// it should not be drawn.
    /// </summary>
    public Vector2? WorldToNdc(Vector3 world, float aspect)
    {
        var vp = ViewProjection(aspect);
        // Row-vector convention: clip = world4 · vp (matches ViewProjection's layout).
        var clip = Vector4.Transform(new Vector4(world, 1f), vp);
        if (clip.W <= 0f)
            return null;
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    /// <summary>
    /// Builds the world-space picking ray through a pixel in a viewport of the given
    /// size. Unprojects the pixel's near and far clip-space points through the inverse
    /// view·projection and connects them. <paramref name="pixelX"/>/<paramref name="pixelY"/>
    /// use the usual top-left origin (Y grows downward); units cancel, so device-independent
    /// pixels are fine as long as both arguments share the same space.
    /// </summary>
    public Ray ScreenPointToRay(double pixelX, double pixelY, double viewportWidth, double viewportHeight)
    {
        var width = MathF.Max(1f, (float)viewportWidth);
        var height = MathF.Max(1f, (float)viewportHeight);
        // Pixel → normalized device coordinates ([-1, 1], Y up).
        var ndcX = (float)(2.0 * pixelX / width - 1.0);
        var ndcY = (float)(1.0 - 2.0 * pixelY / height);

        var vp = ViewProjection(width / height);
        if (!Matrix4x4.Invert(vp, out var inv))
        {
            // Degenerate matrix (shouldn't happen for a valid room); fall back to looking forward.
            var fwd = Vector3.Normalize(OrbitTarget - EyePosition);
            return new Ray(EyePosition, fwd);
        }

        // System.Numerics depth range is [0, 1]: 0 = near plane, 1 = far plane.
        var near = Unproject(inv, ndcX, ndcY, 0f);
        var far = Unproject(inv, ndcX, ndcY, 1f);
        return new Ray(near, Vector3.Normalize(far - near));
    }

    private static Vector3 Unproject(Matrix4x4 invViewProjection, float ndcX, float ndcY, float ndcZ)
    {
        // Row-vector transform (clip · inv = world), matching ViewProjection's convention.
        var world = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), invViewProjection);
        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    // --- viewpoint generation -----------------------------------------------

    /// <summary>
    /// Builds a first-person viewpoint list for a rectangular room.
    /// Returns 2N + 1 viewpoints for an N-sided polygon: one room-centre viewpoint,
    /// then for each edge a corner viewpoint followed by a wall-centre viewpoint,
    /// all inset <c>0.15 m</c> inside from the surface and aimed toward the room centre.
    /// </summary>
    public static IReadOnlyList<CameraViewpoint> BuildViewpoints(float roomWidth, float roomLength)
    {
        Vector2[] vertices =
        [
            new(0f, 0f),
            new(roomWidth, 0f),
            new(roomWidth, roomLength),
            new(0f, roomLength),
        ];
        return BuildViewpoints(vertices);
    }

    /// <summary>
    /// Builds a first-person viewpoint list for an arbitrary floor polygon defined by
    /// <paramref name="vertices"/> in (world X, world Z) order.
    /// </summary>
    public static IReadOnlyList<CameraViewpoint> BuildViewpoints(Vector2[] vertices)
    {
        if (vertices.Length < 2)
            return [new CameraViewpoint("Center", 0f, 0f, 0f)];

        var center = Vector2.Zero;
        foreach (var v in vertices) center += v;
        center /= vertices.Length;

        var list = new List<CameraViewpoint>(2 * vertices.Length + 1)
        {
            new("Center", center.X, center.Y, 0f)
        };

        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Length];
            var mid = (a + b) * 0.5f;

            var cornerPos = InsetToward(a, center);
            var wallPos = InsetToward(mid, center);

            list.Add(new CameraViewpoint($"Corner {i + 1}", cornerPos.X, cornerPos.Y, YawToward(cornerPos, center)));
            list.Add(new CameraViewpoint($"Wall {i + 1}", wallPos.X, wallPos.Y, YawToward(wallPos, center)));
        }

        return list;
    }

    private static Vector2 InsetToward(Vector2 point, Vector2 toward)
    {
        var dir = toward - point;
        var len = dir.Length();
        return len > ViewpointInset ? point + dir / len * ViewpointInset : toward;
    }

    private static float YawToward(Vector2 from, Vector2 to)
    {
        var dx = to.X - from.X;
        var dz = to.Y - from.Y;
        return MathF.Atan2(dx, dz);
    }
}
