using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Items;

/// <summary>
/// Default <see cref="IItemCatalog"/> seeded with the standard furniture presets.
/// Extra templates can be supplied to the constructor to extend the catalog
/// without modifying this class.
/// </summary>
public sealed class ItemCatalog : IItemCatalog
{
    private readonly Dictionary<string, ItemTemplate> _byId;

    public ItemCatalog(IEnumerable<ItemTemplate>? additionalTemplates = null)
    {
        var templates = new List<ItemTemplate>(DefaultTemplates());
        if (additionalTemplates is not null)
            templates.AddRange(additionalTemplates);

        Templates = templates;
        _byId = templates.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ItemTemplate> Templates { get; }

    public bool TryGet(string templateId, out ItemTemplate template)
        => _byId.TryGetValue(templateId, out template!);

    public RoomItem Create(string templateId, Vec2 position)
    {
        if (!_byId.TryGetValue(templateId, out var template))
            throw new KeyNotFoundException($"No item template with id '{templateId}'.");
        return template.CreateItem(position);
    }

    private static ItemTemplate Meters(
        string id, string name, ItemCategory category, double w, double d, double h, string color)
        => new(id, name, category,
            Length.FromMeters(w), Length.FromMeters(d), Length.FromMeters(h), color);

    private static IEnumerable<ItemTemplate> DefaultTemplates() =>
    [
        Meters("table", "Table", ItemCategory.Table, 1.2, 0.8, 0.75, "#B5835A"),
        Meters("chair", "Chair", ItemCategory.Chair, 0.5, 0.5, 0.9, "#6D8B74"),
        Meters("couch", "Couch", ItemCategory.Couch, 2.0, 0.9, 0.85, "#5C6B8A"),
        Meters("tv-stand", "TV Stand", ItemCategory.TvStand, 1.5, 0.4, 0.5, "#4A4A4A"),
        Meters("coffee-table", "Coffee Table", ItemCategory.CoffeeTable, 1.1, 0.6, 0.45, "#9C6B3F"),
        Meters("bed", "Bed", ItemCategory.Bed, 2.0, 1.5, 0.5, "#8A6FA8"),
        Meters("dresser", "Dresser", ItemCategory.Dresser, 1.0, 0.5, 0.8, "#7A5C45"),
        Meters("chest", "Chest", ItemCategory.Chest, 0.9, 0.45, 0.6, "#8C7355"),
    ];
}
