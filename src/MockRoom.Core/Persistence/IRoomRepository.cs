using MockRoom.Core.Rooms;

namespace MockRoom.Core.Persistence;

/// <summary>
/// Reads and writes <see cref="Room"/> layouts. Stream-based so callers (file
/// picker, tests, future cloud sync) choose where the bytes live, and so the
/// abstraction stays UI- and platform-agnostic.
/// </summary>
public interface IRoomRepository
{
    Task SaveAsync(Room room, Stream destination, CancellationToken cancellationToken = default);

    Task<Room> LoadAsync(Stream source, CancellationToken cancellationToken = default);
}
