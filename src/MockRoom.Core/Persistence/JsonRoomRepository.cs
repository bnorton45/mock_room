using System.Text.Json;
using MockRoom.Core.Rooms;

namespace MockRoom.Core.Persistence;

/// <summary>
/// <see cref="IRoomRepository"/> backed by JSON via the source-generated
/// <see cref="RoomJsonContext"/> (NativeAOT- and trim-safe — no reflection-based
/// serialization).
/// </summary>
public sealed class JsonRoomRepository : IRoomRepository
{
    public Task SaveAsync(Room room, Stream destination, CancellationToken cancellationToken = default)
    {
        var document = RoomMapper.ToDocument(room);
        return JsonSerializer.SerializeAsync(destination, document, RoomJsonContext.Default.RoomDocument, cancellationToken);
    }

    public async Task<Room> LoadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var document = await JsonSerializer.DeserializeAsync(
            source, RoomJsonContext.Default.RoomDocument, cancellationToken);
        if (document is null)
            throw new InvalidDataException("The file did not contain a room layout.");

        return RoomMapper.FromDocument(document);
    }
}
