#nullable enable

/// <summary>
/// Client mirror of the server's StatKind (server/spacetimedb/src/item/tables.rs). The
/// generated bindings also define SpacetimeDB.Types.StatKind — qualify with the full
/// namespace when you need that one. Hp exists for parity with item stat modifiers; live
/// hp/max_hp values are mirrored by HealthComponent's shared Stat.
/// </summary>
public enum StatKind
{
    Strength,
    Wisdom,
    Dexterity,
    Defense,
    Vitality,
    Speed,
    Hp,
}
