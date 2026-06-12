using System;
using MockRoom.Core.Geometry;
using Xunit;

namespace MockRoom.Tests;

public class GeometryTests
{
    [Fact]
    public void Footprint_Area_IsWidthTimesDepth()
    {
        var rect = new FootprintRect(Vec2.Zero, 2.0, 1.5);
        Assert.Equal(3.0, rect.Area.SquareMeters, 6);
    }

    [Fact]
    public void Footprint_Contains_AxisAligned()
    {
        var rect = new FootprintRect(new Vec2(5, 5), 2.0, 1.0);
        Assert.True(rect.Contains(new Vec2(5, 5)));      // center
        Assert.True(rect.Contains(new Vec2(5.9, 5.4)));  // inside
        Assert.False(rect.Contains(new Vec2(6.1, 5)));   // outside in X
        Assert.False(rect.Contains(new Vec2(5, 5.6)));   // outside in Y
    }

    [Fact]
    public void Footprint_Bounds_GrowWhenRotated45()
    {
        var rect = new FootprintRect(Vec2.Zero, 2.0, 2.0, Math.PI / 4);
        var (minX, minY, maxX, maxY) = rect.Bounds();
        var diag = Math.Sqrt(2); // half-extent of a 2x2 square rotated 45°
        Assert.Equal(-diag, minX, 6);
        Assert.Equal(diag, maxX, 6);
        Assert.Equal(-diag, minY, 6);
        Assert.Equal(diag, maxY, 6);
    }

    [Fact]
    public void Footprint_Contains_RespectsRotation()
    {
        var rect = new FootprintRect(Vec2.Zero, 4.0, 1.0, Math.PI / 2);
        // Rotated 90°, the long axis now runs along Y.
        Assert.True(rect.Contains(new Vec2(0, 1.8)));
        Assert.False(rect.Contains(new Vec2(1.8, 0)));
    }

    [Fact]
    public void Vec3_ToFloor_DropsHeight()
    {
        var floor = new Vec3(3, 2, 7).ToFloor();
        Assert.Equal(3, floor.X, 6);
        Assert.Equal(7, floor.Y, 6);
    }
}
