namespace MockRoom.Core.Items;

/// <summary>
/// The kind of furniture an item represents. Drives default dimensions and
/// display color in the catalog. <see cref="Custom"/> is for user-defined shapes
/// that don't match a preset.
/// </summary>
public enum ItemCategory
{
    Table,
    Chair,
    Recliner,
    Couch,
    TvStand,
    CoffeeTable,
    Bed,
    Dresser,
    Chest,
    Custom,
}
