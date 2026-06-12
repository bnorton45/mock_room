using System;
using System.Numerics;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rendering;
using MockRoom.Core.Rooms;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class RayPickerTests
{
    private static BoxItem Box(double x, double z, double w = 1, double d = 1, double h = 1, double yaw = 0)
    {
        return new BoxItem("Box", ItemCategory.Custom,
            Length.FromMeters(w), Length.FromMeters(d), Length.FromMeters(h))
        {
            Position = new Vec2(x, z),
            Rotation = yaw,
        };
    }

    private static Ray Down(double x, double z, double fromHeight = 5)
        => new(new Vector3((float)x, (float)fromHeight, (float)z), new Vector3(0, -1, 0));

    [Fact]
    public void RayDownThroughCenter_HitsBoxAtItsTop()
    {
        var box = Box(2.5, 2.0, h: 1); // top face at y = 1
        var distance = RayPicker.IntersectItem(Down(2.5, 2.0, fromHeight: 5), box);

        Assert.NotNull(distance);
        Assert.Equal(4.0, distance!.Value, 4); // 5 (start) - 1 (top)
    }

    [Fact]
    public void RayMissingTheFootprint_ReturnsNull()
    {
        var box = Box(2.5, 2.0); // 1x1 centered at (2.5, 2)
        Assert.Null(RayPicker.IntersectItem(Down(4.5, 3.5), box)); // well outside
    }

    [Fact]
    public void RayPointingAwayFromBox_ReturnsNull()
    {
        var box = Box(2.5, 2.0);
        // Origin above the box but pointing up — the box is behind the ray.
        var up = new Ray(new Vector3(2.5f, 5f, 2f), new Vector3(0, 1, 0));
        Assert.Null(RayPicker.IntersectItem(up, box));
    }

    [Fact]
    public void Rotation_MovesWhichRaysHit()
    {
        // 2 m wide (X) by 1 m deep (Z), rotated 90° so the long axis now runs along Z.
        var box = Box(2.5, 2.0, w: 2, d: 1, yaw: Math.PI / 2);

        // A point 0.9 m off-center along Z is inside the rotated (long) extent...
        Assert.NotNull(RayPicker.IntersectItem(Down(2.5, 2.9), box));
        // ...but 0.9 m off-center along X is now outside the short (0.5 m half) extent.
        Assert.Null(RayPicker.IntersectItem(Down(3.4, 2.0), box));
    }

    [Fact]
    public void OriginInsideBox_ReportsZeroDistance()
    {
        var box = Box(2.5, 2.0, w: 2, d: 2, h: 2);
        var inside = new Ray(new Vector3(2.5f, 1f, 2f), new Vector3(0, 0, 1));
        Assert.Equal(0.0, RayPicker.IntersectItem(inside, box)!.Value, 6);
    }

    [Fact]
    public void Pick_ReturnsFrontMostItemAlongTheRay()
    {
        var near = Box(2.5, 1.0); // world z = 1
        var far = Box(2.5, 3.0);  // world z = 3
        // Horizontal ray going +Z at y = 0.5 (within both boxes' height).
        var ray = new Ray(new Vector3(2.5f, 0.5f, -5f), new Vector3(0, 0, 1));

        Assert.Same(near, RayPicker.Pick(new[] { far, near }, ray));
    }

    [Fact]
    public void Pick_ReturnsNull_WhenNothingIsHit()
    {
        var ray = Down(4.5, 3.5);
        Assert.Null(RayPicker.Pick(new[] { Box(2.5, 2.0) }, ray));
    }

    // --- camera ray --------------------------------------------------------

    private static RoomDimensions Room5x4x2_5 => RoomDimensions.FromMeters(5, 4, 2.5);

    [Fact]
    public void ScreenCenterRay_Orbit_PointsAtTheTarget()
    {
        var camera = Camera.FromRoom(Room5x4x2_5);
        camera.Mode = CameraMode.Orbit;
        camera.OrbitYaw = 0.7f;
        camera.OrbitPitch = 0.4f;

        var ray = camera.ScreenPointToRay(640, 360, 1280, 720);
        var expected = Vector3.Normalize(camera.OrbitTarget - camera.EyePosition);

        Assert.Equal(expected.X, ray.Direction.X, 3);
        Assert.Equal(expected.Y, ray.Direction.Y, 3);
        Assert.Equal(expected.Z, ray.Direction.Z, 3);
    }

    [Fact]
    public void ScreenCenterRay_FirstPerson_PointsAlongForward()
    {
        var camera = new Camera(Room5x4x2_5) { Mode = CameraMode.FirstPerson, Yaw = 0f, Pitch = 0f };

        var ray = camera.ScreenPointToRay(640, 360, 1280, 720);

        // Yaw 0 / pitch 0 looks straight down +Z (the room length axis).
        Assert.Equal(0f, ray.Direction.X, 3);
        Assert.Equal(0f, ray.Direction.Y, 3);
        Assert.Equal(1f, ray.Direction.Z, 3);
    }

    [Fact]
    public void CameraRay_SelectsItemUnderScreenCenter()
    {
        // Orbit camera framing the room; an item sitting at the room center should be
        // picked by a click at the screen center, which is where the camera looks.
        var room = new Room(Room5x4x2_5);
        var item = Box(2.5, 2.0, w: 1.5, d: 1.5, h: 1.5);
        room.AddItem(item);

        var camera = Camera.FromRoom(room.Dimensions);
        camera.Mode = CameraMode.Orbit;
        camera.OrbitYaw = 0.6f;
        camera.OrbitPitch = 0.5f;

        var ray = camera.ScreenPointToRay(640, 360, 1280, 720);
        Assert.Same(item, RayPicker.Pick(room.Items, ray));
    }
}
