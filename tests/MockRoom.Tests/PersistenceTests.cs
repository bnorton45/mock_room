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
        room.AddOpening(new WallOpening(OpeningKind.Door, WallSide.South,
            Length.FromMeters(3), Length.FromMeters(0.9), Length.FromMeters(2.0)));
        room.AddOpening(new WallOpening(OpeningKind.Window, WallSide.East,
            Length.FromMeters(4), Length.FromMeters(1.2), Length.FromMeters(1.2), Length.FromMeters(0.9))
        {
            FrameTop = Length.FromMeters(0.1),
            FrameBottom = Length.FromMeters(0.2),
            FrameLeft = Length.FromMeters(0.05),
            FrameRight = Length.FromMeters(0.15),
        });
        return room;
    }

    [Fact]
    public async Task RoundTrip_PreservesRoomItemsAndOpenings()
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

        Assert.Equal(2, loaded.Openings.Count);
        var door = Assert.Single(loaded.Openings, o => o.Kind == OpeningKind.Door);
        Assert.Equal(WallSide.South, door.Wall);
        Assert.Equal(0.9, door.Width.Meters, 6);
        Assert.Equal(2.0, door.Height.Meters, 6);

        var window = Assert.Single(loaded.Openings, o => o.Kind == OpeningKind.Window);
        Assert.Equal(WallSide.East, window.Wall);
        Assert.Equal(0.9, window.SillHeight.Meters, 6);
        Assert.Equal(0.1, window.FrameTop.Meters, 6);
        Assert.Equal(0.2, window.FrameBottom.Meters, 6);
        Assert.Equal(0.05, window.FrameLeft.Meters, 6);
        Assert.Equal(0.15, window.FrameRight.Meters, 6);
    }

    [Fact]
    public async Task Load_UpgradesLegacyVersion1Doors()
    {
        // A version-1 document stored openings under "doors"; they must load as door openings.
        const string legacyJson = """
            {
              "version": 1,
              "widthMeters": 6,
              "lengthMeters": 8,
              "heightMeters": 2.5,
              "preferredUnits": "Metric",
              "items": [],
              "doors": [
                { "id": "11111111-1111-1111-1111-111111111111", "wall": "South",
                  "offsetMeters": 3, "widthMeters": 0.9, "heightMeters": 2.0, "swingClearanceMeters": 0.9 }
              ]
            }
            """;
        var repository = new JsonRoomRepository();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson));

        var loaded = await repository.LoadAsync(stream);

        var opening = Assert.Single(loaded.Openings);
        Assert.Equal(OpeningKind.Door, opening.Kind);
        Assert.Equal(WallSide.South, opening.Wall);
        Assert.Equal(0.9, opening.Width.Meters, 6);
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
