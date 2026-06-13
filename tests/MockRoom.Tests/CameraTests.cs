using System.Numerics;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;
using Xunit;

namespace MockRoom.Tests;

public class CameraTests
{
    private static RoomDimensions Room5x4x2_5 => RoomDimensions.FromMeters(5, 4, 2.5);

    [Fact]
    public void FirstPerson_EyeStaysAtRoomCenter_ForAnyRotation()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.FirstPerson, EyeHeight = 1.6f };

        foreach (var yaw in new[] { 0f, 1f, 3f, -2f })
            foreach (var pitch in new[] { 0f, 0.5f, -0.5f })
            {
                camera.Yaw = yaw;
                camera.Pitch = pitch;
                var eye = camera.EyePosition;
                Assert.Equal(2.5f, eye.X, 4); // width / 2
                Assert.Equal(2.0f, eye.Z, 4); // length / 2
                Assert.Equal(1.6f, eye.Y, 4); // unchanged by rotation
            }
    }

    [Fact]
    public void FirstPerson_EyeHeight_DrivesOnlyY()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.FirstPerson };

        camera.EyeHeight = 0.5f;
        var low = camera.EyePosition;
        camera.EyeHeight = 2.0f;
        var high = camera.EyePosition;

        Assert.Equal(low.X, high.X, 4);
        Assert.Equal(low.Z, high.Z, 4);
        Assert.Equal(0.5f, low.Y, 4);
        Assert.Equal(2.0f, high.Y, 4);
    }

    [Fact]
    public void FirstPerson_EyeHeight_ClampedToRoomInterior()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.FirstPerson, EyeHeight = 100f };
        Assert.True(camera.EyePosition.Y <= 2.5f);

        camera.EyeHeight = -5f;
        Assert.True(camera.EyePosition.Y >= Camera.MinEyeHeight);
    }

    [Fact]
    public void Pitch_IsClampedToMax()
    {
        var camera = new Camera(Room5x4x2_5);
        camera.Pitch = 10f;
        Assert.Equal(Camera.MaxPitch, camera.Pitch, 4);
        camera.Pitch = -10f;
        Assert.Equal(-Camera.MaxPitch, camera.Pitch, 4);
    }

    [Fact]
    public void Orbit_EyeStaysAtDistanceFromCenter()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.Orbit, OrbitDistance = 9f };

        foreach (var yaw in new[] { 0f, 1.2f, -2.5f })
            foreach (var pitch in new[] { 0f, 0.4f, -0.4f })
            {
                camera.OrbitYaw = yaw;
                camera.OrbitPitch = pitch;
                var d = Vector3.Distance(camera.EyePosition, camera.OrbitTarget);
                Assert.Equal(9f, d, 3);
            }
    }

    [Fact]
    public void ModeSwitch_ChangesEyePosition_WithoutThrowing()
    {
        var camera = Camera.FromRoom(Room5x4x2_5);

        camera.Mode = CameraMode.FirstPerson;
        var fp = camera.EyePosition;
        camera.Mode = CameraMode.Orbit;
        var orbit = camera.EyePosition;

        Assert.NotEqual(fp, orbit);
        // Orbit eye sits outside the room volume; first-person sits at the center.
        Assert.Equal(2.5f, fp.X, 4);
    }

    [Fact]
    public void Projection_IsFinite()
    {
        var camera = Camera.FromRoom(Room5x4x2_5);
        var m = camera.ViewProjection(16f / 9f);
        Assert.True(float.IsFinite(m.M11) && float.IsFinite(m.M44));
    }

    [Fact]
    public void FirstPersonX_Z_Override_MovesEye()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.FirstPerson, EyeHeight = 1.6f };

        camera.FirstPersonX = 1.0f;
        camera.FirstPersonZ = 0.5f;

        Assert.Equal(1.0f, camera.EyePosition.X, 4);
        Assert.Equal(0.5f, camera.EyePosition.Z, 4);
        Assert.Equal(1.6f, camera.EyePosition.Y, 4);
    }

    [Fact]
    public void BuildViewpoints_Rectangle_Returns9Viewpoints()
    {
        var viewpoints = Camera.BuildViewpoints(5f, 4f);

        // 1 center + 4 corners + 4 wall centres = 9
        Assert.Equal(9, viewpoints.Count);
    }

    [Fact]
    public void BuildViewpoints_FirstViewpoint_IsCenter()
    {
        var viewpoints = Camera.BuildViewpoints(6f, 4f);

        var center = viewpoints[0];
        Assert.Equal("Center", center.Name);
        Assert.Equal(3f, center.X, 3); // 6 / 2
        Assert.Equal(2f, center.Z, 3); // 4 / 2
    }

    [Fact]
    public void BuildViewpoints_WallAndCorner_AreInsetFromSurface()
    {
        var viewpoints = Camera.BuildViewpoints(6f, 4f);

        // Every non-centre viewpoint must be strictly inside the room (0 < X < W, 0 < Z < L).
        foreach (var vp in viewpoints.Skip(1))
        {
            Assert.True(vp.X > 0f && vp.X < 6f, $"{vp.Name}.X={vp.X} not inside [0,6]");
            Assert.True(vp.Z > 0f && vp.Z < 4f, $"{vp.Name}.Z={vp.Z} not inside [0,4]");
        }
    }

    [Fact]
    public void BuildViewpoints_Yaw_PointsTowardRoomCenter()
    {
        var viewpoints = Camera.BuildViewpoints(6f, 4f);
        var cx = 3f;
        var cz = 2f;

        foreach (var vp in viewpoints.Skip(1))
        {
            // forward direction from this yaw
            var fwdX = MathF.Sin(vp.Yaw);
            var fwdZ = MathF.Cos(vp.Yaw);
            var toCenterX = cx - vp.X;
            var toCenterZ = cz - vp.Z;
            var dot = fwdX * toCenterX + fwdZ * toCenterZ;
            Assert.True(dot > 0f, $"{vp.Name} yaw does not point toward center (dot={dot:F3})");
        }
    }

    [Fact]
    public void BuildViewpoints_ArbitraryPolygon_ReturnsCorrectCount()
    {
        // Triangle (3 vertices) → 2*3 + 1 = 7 viewpoints
        var viewpoints = Camera.BuildViewpoints(
        [
            new(0f, 0f),
            new(4f, 0f),
            new(2f, 4f),
        ]);

        Assert.Equal(7, viewpoints.Count);
    }
}
