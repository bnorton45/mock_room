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

    // Helper: part defined in item-local meters (center X, center Y, bottom Y, W, D, H, optional color).
    private static FurniturePart P(
        double lx, double ly, double botY, double w, double d, double h, string? col = null)
        => new(lx, ly, botY, w, d, h, col);

    private static ItemTemplate Meters(
        string id, string name, ItemCategory category, double w, double d, double h, string color,
        IReadOnlyList<FurniturePart>? parts = null)
        => new(id, name, category,
            Length.FromMeters(w), Length.FromMeters(d), Length.FromMeters(h), color, parts);

    private static IEnumerable<ItemTemplate> DefaultTemplates() =>
    [
        // Table: four legs + tabletop. Legs drawn first (covered by top in 2D), visible in 3D.
        Meters("table", "Table", ItemCategory.Table, 1.2, 0.8, 0.75, "#B5835A", [
            P(-0.55, -0.35, 0,    0.05, 0.05, 0.72),   // front-left leg
            P( 0.55, -0.35, 0,    0.05, 0.05, 0.72),   // front-right leg
            P(-0.55,  0.35, 0,    0.05, 0.05, 0.72),   // back-left leg
            P( 0.55,  0.35, 0,    0.05, 0.05, 0.72),   // back-right leg
            P( 0,     0,    0.72, 1.20, 0.80, 0.03),   // tabletop
        ]),

        // Chair: seat + backrest strip visible from above.
        Meters("chair", "Chair", ItemCategory.Chair, 0.5, 0.5, 0.9, "#6D8B74", [
            P(0,  0.04, 0, 0.50, 0.40, 0.45),  // seat
            P(0, -0.22, 0, 0.50, 0.06, 0.90),  // backrest
        ]),

        // Recliner (La-Z-Boy style): wide padded arms, thick back with raised headrest, deep seat.
        // Footrest is stowed internally in seated position so it is not shown in the top-down plan.
        Meters("recliner", "Recliner", ItemCategory.Recliner, 0.85, 0.90, 1.05, "#A67C52", [
            P(-0.35,  0,     0,    0.13, 0.90, 0.62),  // left arm pad
            P( 0.35,  0,     0,    0.13, 0.90, 0.62),  // right arm pad
            P( 0,    -0.30,  0,    0.59, 0.30, 1.05),  // back cushion (full height)
            P( 0,     0.10,  0,    0.59, 0.54, 0.50),  // seat cushion
            P( 0,    -0.32,  0.80, 0.42, 0.24, 0.25),  // headrest (raised above back cushion)
        ]),

        // Couch: seat, backrest, and two side arms — gives an unmistakable sofa silhouette.
        Meters("couch", "Couch", ItemCategory.Couch, 2.0, 0.9, 0.85, "#5C6B8A", [
            P(-0.95, 0,     0, 0.10, 0.90, 0.65),  // left arm
            P( 0.95, 0,     0, 0.10, 0.90, 0.65),  // right arm
            P( 0,   -0.32,  0, 1.80, 0.26, 0.85),  // backrest
            P( 0,    0.10,  0, 1.80, 0.60, 0.45),  // seat
        ]),

        // L-shaped sectional couch (2.6 m × 1.8 m).
        // Main section runs left-right (backrest at Y=-0.9), chaise extends forward-left (Y: 0→+0.9, X: -1.3→-0.4).
        // Left outer wall spans the full depth and doubles as the chaise back. Right arm caps the main section end.
        Meters("sectional-couch", "L-Shaped Couch", ItemCategory.SectionalCouch, 2.6, 1.8, 0.85, "#7A6B9A", [
            P(-1.25,  0,     0, 0.10, 1.80, 0.65),  // left outer wall: arm (main) + chaise back
            P( 0,    -0.77,  0, 2.40, 0.26, 0.85),  // main backrest
            P( 0,    -0.32,  0, 2.40, 0.64, 0.45),  // main seat
            P( 1.25, -0.45,  0, 0.10, 0.90, 0.65),  // right arm (end of main section)
            P(-0.80,  0.40,  0, 0.80, 0.80, 0.45),  // chaise seat
            P(-0.80,  0.85,  0, 0.80, 0.10, 0.65),  // chaise front arm
        ]),

        // TV Stand: simple box (no visible silhouette improvement from parts).
        Meters("tv-stand", "TV Stand", ItemCategory.TvStand, 1.5, 0.4, 0.5, "#4A4A4A"),

        // Coffee table: four legs + tabletop (same pattern as dining table, smaller scale).
        Meters("coffee-table", "Coffee Table", ItemCategory.CoffeeTable, 1.1, 0.6, 0.45, "#9C6B3F", [
            P(-0.50, -0.26, 0,    0.06, 0.06, 0.42),  // front-left leg
            P( 0.50, -0.26, 0,    0.06, 0.06, 0.42),  // front-right leg
            P(-0.50,  0.26, 0,    0.06, 0.06, 0.42),  // back-left leg
            P( 0.50,  0.26, 0,    0.06, 0.06, 0.42),  // back-right leg
            P( 0,     0,    0.42, 1.10, 0.60, 0.03),  // tabletop
        ]),

        // Bed: base frame, mattress on top, headboard, and shorter footboard.
        Meters("bed", "Bed", ItemCategory.Bed, 2.0, 1.5, 0.5, "#8A6FA8", [
            P(0,  0,     0,    2.00, 1.50, 0.20),  // base frame
            P(0,  0.08,  0.20, 1.88, 1.26, 0.30),  // mattress
            P(0, -0.68,  0,    1.88, 0.12, 1.00),  // headboard (tall, at head end)
            P(0,  0.68,  0,    1.88, 0.08, 0.45),  // footboard (shorter, at foot end)
        ]),

        // Dresser: simple box.
        Meters("dresser", "Dresser", ItemCategory.Dresser, 1.0, 0.5, 0.8, "#7A5C45"),

        // Chest: simple box.
        Meters("chest", "Chest", ItemCategory.Chest, 0.9, 0.45, 0.6, "#8C7355"),
    ];
}
