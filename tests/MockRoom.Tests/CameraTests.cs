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
}
