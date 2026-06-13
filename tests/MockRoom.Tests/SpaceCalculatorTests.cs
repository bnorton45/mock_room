using System;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Rooms;
using MockRoom.Core.Spatial;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class SpaceCalculatorTests
{
    [Fact]
    public void EmptyRoom_IsFullyFree()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        var report = new OccupancyGridSpaceCalculator().Compute(room);

        Assert.Equal(48, report.Total.SquareMeters, 6);
        Assert.Equal(0, report.Used.SquareMeters, 6);
        Assert.Equal(48, report.Free.SquareMeters, 6);
        Assert.Equal(1.0, report.FreeFraction, 6);
    }

    [Fact]
    public void HeadlineExample_BedLeaves45SquareMeters()
    {
        // 6 m × 8 m room (48 m²) with a 2 m × 1.5 m bed (3 m²) → 45 m² free.
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        var bed = new BoxItem("Bed", ItemCategory.Bed,
            Length.FromMeters(2), Length.FromMeters(1.5), Length.FromMeters(0.5))
        {
            Position = new Vec2(1.0, 0.75), // grid-aligned: footprint covers x∈[0,2], y∈[0,1.5]
        };
        room.AddItem(bed);

        var report = new OccupancyGridSpaceCalculator().Compute(room);

        Assert.Equal(3.0, report.Used.SquareMeters, 2);
        Assert.Equal(45.0, report.Free.SquareMeters, 2);
    }

    [Fact]
    public void OverlappingItems_CountedOnce()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        // Two identical 2×1.5 footprints at the same spot should still use ~3 m².
        for (var i = 0; i < 2; i++)
        {
            room.AddItem(new BoxItem($"Bed{i}", ItemCategory.Bed,
                Length.FromMeters(2), Length.FromMeters(1.5), Length.FromMeters(0.5))
            {
                Position = new Vec2(1.0, 0.75),
            });
        }

        var report = new OccupancyGridSpaceCalculator().Compute(room);
        Assert.Equal(3.0, report.Used.SquareMeters, 2);
    }

    [Fact]
    public void DoorSwing_ConsumesQuarterCircleWhenEnabled()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        room.AddOpening(new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0)));

        var withSwing = new OccupancyGridSpaceCalculator { IncludeDoorSwing = true }.Compute(room);
        var withoutSwing = new OccupancyGridSpaceCalculator { IncludeDoorSwing = false }.Compute(room);

        // A 0.9 m leaf sweeps a quarter circle: π·0.9²/4 ≈ 0.636 m².
        Assert.Equal(Math.PI * 0.9 * 0.9 / 4, withSwing.Used.SquareMeters, 1);
        Assert.Equal(0, withoutSwing.Used.SquareMeters, 6);
    }

    [Fact]
    public void Window_ConsumesNoFloor()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        room.AddOpening(new WallOpening(OpeningKind.Window, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.2), Length.FromMeters(1.2), Length.FromMeters(0.9)));

        var report = new OccupancyGridSpaceCalculator().Compute(room);
        Assert.Equal(0, report.Used.SquareMeters, 6);
    }

    [Fact]
    public void ClosetDoor_ConsumesLessFloorThanASingleDoorOfSameWidth()
    {
        // Two W/2 leaves sweep 2·π·(W/2)²/4 = half the area of one W leaf's quarter circle.
        var doorRoom = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        doorRoom.AddOpening(new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.5), Length.FromMeters(2.0)));
        var closetRoom = new Room(RoomDimensions.FromMeters(6, 8, 2.5));
        closetRoom.AddOpening(new WallOpening(OpeningKind.ClosetDoor, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(1.5), Length.FromMeters(2.0)));

        var door = new OccupancyGridSpaceCalculator().Compute(doorRoom);
        var closet = new OccupancyGridSpaceCalculator().Compute(closetRoom);

        Assert.True(closet.Used.SquareMeters < door.Used.SquareMeters);
    }

    [Fact]
    public void UsedArea_NeverExceedsTotal()
    {
        var room = new Room(RoomDimensions.FromMeters(2, 2, 2.5)); // 4 m²
        // A huge item bigger than the room.
        room.AddItem(new BoxItem("Slab", ItemCategory.Custom,
            Length.FromMeters(5), Length.FromMeters(5), Length.FromMeters(0.1))
        {
            Position = new Vec2(1, 1),
        });

        var report = new OccupancyGridSpaceCalculator().Compute(room);
        Assert.Equal(4, report.Used.SquareMeters, 6);
        Assert.Equal(0, report.Free.SquareMeters, 6);
    }

    [Fact]
    public void FreeRuns_CoverEveryFreeCellExactlyOnce()
    {
        var room = new Room(RoomDimensions.FromMeters(2, 2, 2.5));
        room.AddItem(new BoxItem("Box", ItemCategory.Custom,
            Length.FromMeters(0.5), Length.FromMeters(0.5), Length.FromMeters(0.5))
        {
            Position = new Vec2(1, 1),
        });

        var grid = new OccupancyGridSpaceCalculator().Compute(room).Grid;
        var runs = grid.FreeRuns();

        // Runs are within bounds, non-empty, and only over free cells.
        var cellsInRuns = 0;
        foreach (var (row, colStart, colEnd) in runs)
        {
            Assert.InRange(row, 0, grid.Rows - 1);
            Assert.True(colEnd > colStart);
            for (var col = colStart; col < colEnd; col++)
                Assert.False(grid.IsOccupied(col, row));
            cellsInRuns += colEnd - colStart;
        }

        // Every free cell is accounted for, none double-counted.
        Assert.Equal(grid.FreeCellCount, cellsInRuns);
    }

    [Fact]
    public void FreeRuns_EmptyWhenFullyOccupied()
    {
        var room = new Room(RoomDimensions.FromMeters(2, 2, 2.5));
        room.AddItem(new BoxItem("Slab", ItemCategory.Custom,
            Length.FromMeters(5), Length.FromMeters(5), Length.FromMeters(0.1))
        {
            Position = new Vec2(1, 1),
        });

        var grid = new OccupancyGridSpaceCalculator().Compute(room).Grid;
        Assert.Empty(grid.FreeRuns());
    }
}
