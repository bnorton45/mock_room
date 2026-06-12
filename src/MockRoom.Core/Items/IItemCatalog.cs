using MockRoom.Core.Geometry;

namespace MockRoom.Core.Items;

/// <summary>
/// Registry of available furniture presets. The UI lists <see cref="Templates"/>
/// and calls <see cref="Create"/> to drop a new item into the room.
/// </summary>
public interface IItemCatalog
{
    IReadOnlyList<ItemTemplate> Templates { get; }

    bool TryGet(string templateId, out ItemTemplate template);

    /// <summary>Instantiates the template with the given id at <paramref name="position"/>.</summary>
    RoomItem Create(string templateId, Vec2 position);
}
