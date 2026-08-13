#nullable enable

/// <summary>
/// Client mirror of the server's StatKind (server/spacetimedb/src/item/tables.rs). The
/// generated bindings also define SpacetimeDB.Types.StatKind — qualify with the full
/// namespace when you need that one. The first six are the allocatable stats (players
/// get skill points per level — see allocate_stat); Hp and Defense are modifier-only
/// (granted by gear/buffs, resolved into PlayerData.max_hp / PlayerData.defense).
/// </summary>
public enum StatKind
{
    Strength,
    Wisdom,
    Dexterity,
    DamageDealer,
    Supporter,
    Artisan,
    Hp,
    Defense,
}
