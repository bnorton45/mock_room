using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MockRoom.Core.Geometry;
using MockRoom.Core.Items;
using MockRoom.Core.Persistence;
using MockRoom.Core.Rooms;
using MockRoom.Core.Units;
using Xunit;

namespace MockRoom.Tests;

public class PersistenceTests
{
    private static Room SampleRoom()
    {
        var room = new Room(RoomDimensions.FromMeters(6, 8, 2.5), UnitSystem.Imperial);
        room.AddItem(new BoxItem("Bed", ItemCategory.Bed,
            Length.FromMeters(2), Length.FromMeters(1.5), Length.FromMeters(0.5))
        {
            Position = new Vec2(1.0, 0.75),
            Rotation = Math.PI / 6,
            ColorHex = "#3366CC",
        });
        room.AddDoor(new Door(WallSide.South, Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0)));
        return room;
    }

    [Fact]
    public async Task RoundTrip_PreservesRoomItemsAndDoors()
    {
        var repository = new JsonRoomRepository();
        var original = SampleRoom();

        using var stream = new MemoryStream();
        await repository.SaveAsync(original, stream);
        stream.Position = 0;
        var loaded = await repository.LoadAsync(stream);

        Assert.Equal(original.Dimensions.Width.Meters, loaded.Dimensions.Width.Meters, 6);
        Assert.Equal(original.Dimensions.Length.Meters, loaded.Dimensions.Length.Meters, 6);
        Assert.Equal(original.Dimensions.Height.Meters, loaded.Dimensions.Height.Meters, 6);
        Assert.Equal(UnitSystem.Imperial, loaded.PreferredUnits);

        Assert.Single(loaded.Items);
        var item = loaded.Items[0];
        var source = original.Items[0];
        Assert.Equal(source.Id, item.Id);
        Assert.Equal(source.Name, item.Name);
        Assert.Equal(ItemCategory.Bed, item.Category);
        Assert.Equal(source.Width.Meters, item.Width.Meters, 6);
        Assert.Equal(source.Depth.Meters, item.Depth.Meters, 6);
        Assert.Equal(source.Height.Meters, item.Height.Meters, 6);
        Assert.Equal(source.Position.X, item.Position.X, 6);
        Assert.Equal(source.Position.Y, item.Position.Y, 6);
        Assert.Equal(source.Rotation, item.Rotation, 6);
        Assert.Equal("#3366CC", item.ColorHex);

        Assert.Single(loaded.Doors);
        var door = loaded.Doors[0];
        Assert.Equal(WallSide.South, door.Wall);
        Assert.Equal(0.9, door.Width.Meters, 6);
        Assert.Equal(2.0, door.Height.Meters, 6);
    }

    [Fact]
    public async Task Save_WritesReadableEnumNames()
    {
        var repository = new JsonRoomRepository();
        using var stream = new MemoryStream();

        await repository.SaveAsync(SampleRoom(), stream);
        var json = Encoding.UTF8.GetString(stream.ToArray());

        // UseStringEnumConverter keeps the file human-readable rather than numeric.
        Assert.Contains("Imperial", json);
        Assert.Contains("South", json);
    }

    [Fact]
    public async Task Load_ThrowsOnEmptyJson()
    {
        var repository = new JsonRoomRepository();
        using var stream = new MemoryStream("null"u8.ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync(stream));
    }
}
