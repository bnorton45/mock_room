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
    public void SwingArc_ContainsOnlyTheQuarterDisc()
    {
        // Hinged at the origin, sweeping from +X (DirA) to +Y (DirB), radius 1.
        var arc = new SwingArc(Vec2.Zero, 1.0, new Vec2(1, 0), new Vec2(0, 1));

        Assert.True(arc.Contains(new Vec2(0.5, 0.5)));   // inside the wedge and radius
        Assert.False(arc.Contains(new Vec2(0.8, 0.8)));  // within the wedge but past the radius
        Assert.False(arc.Contains(new Vec2(-0.2, 0.5))); // behind DirA
        Assert.False(arc.Contains(new Vec2(0.5, -0.2))); // behind DirB
        Assert.Equal(Math.PI / 4, arc.Area.SquareMeters, 6);
    }

    [Fact]
    public void SwingArc_Intersects_RectInTheSweptQuadrant()
    {
        var arc = new SwingArc(Vec2.Zero, 1.0, new Vec2(1, 0), new Vec2(0, 1));

        Assert.True(arc.Intersects(new FootprintRect(new Vec2(0.4, 0.4), 0.4, 0.4)));   // sits in the sweep
        Assert.False(arc.Intersects(new FootprintRect(new Vec2(-0.5, 0.5), 0.4, 0.4))); // behind the hinge
        Assert.False(arc.Intersects(new FootprintRect(new Vec2(2.0, 2.0), 0.4, 0.4)));  // beyond the radius
    }

    [Fact]
    public void SwingArc_BoundsEncloseTheSweep()
    {
        var arc = new SwingArc(new Vec2(3, 1), 0.9, new Vec2(1, 0), new Vec2(0, 1));
        var (minX, minY, maxX, maxY) = arc.Bounds();

        // Every point the arc contains must lie within its bounds.
        Assert.True(minX <= 3 && minY <= 1 && maxX >= 3.9 && maxY >= 1.9);
        Assert.True(arc.Contains(new Vec2(3.5, 1.4)));
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

    [Fact]
    public void Footprint_Intersects_OverlappingAxisAlignedRects()
    {
        var a = new FootprintRect(new Vec2(0, 0), 2.0, 2.0);
        var b = new FootprintRect(new Vec2(1, 1), 2.0, 2.0); // overlaps a
        Assert.True(a.Intersects(b));
        Assert.True(b.Intersects(a)); // symmetry
    }

    [Fact]
    public void Footprint_Intersects_SeparatedAxisAlignedRects()
    {
        var a = new FootprintRect(new Vec2(0, 0), 2.0, 2.0);
        var b = new FootprintRect(new Vec2(3, 0), 2.0, 2.0); // gap of 1 m between
        Assert.False(a.Intersects(b));
        Assert.False(b.Intersects(a));
    }

    [Fact]
    public void Footprint_Intersects_TouchingRects_NotConsideredOverlap()
    {
        // Right edge of a (x=1) exactly meets left edge of b (x=1).
        var a = new FootprintRect(new Vec2(0, 0), 2.0, 2.0);
        var b = new FootprintRect(new Vec2(2, 0), 2.0, 2.0);
        Assert.False(a.Intersects(b));
    }

    [Fact]
    public void Footprint_Intersects_RotatedOverlap()
    {
        // Two 2×0.5 rects at the same center, one at 0° and one at 90°, overlap.
        var a = new FootprintRect(Vec2.Zero, 2.0, 0.5, 0);
        var b = new FootprintRect(Vec2.Zero, 2.0, 0.5, Math.PI / 2);
        Assert.True(a.Intersects(b));
    }

    [Fact]
    public void Footprint_Intersects_RotatedNoOverlap()
    {
        // A long thin rect at the origin pointing along X; another 3 m away pointing along Y.
        var a = new FootprintRect(new Vec2(0, 0), 1.0, 0.2, 0);
        var b = new FootprintRect(new Vec2(3, 0), 1.0, 0.2, Math.PI / 2);
        Assert.False(a.Intersects(b));
    }
}
