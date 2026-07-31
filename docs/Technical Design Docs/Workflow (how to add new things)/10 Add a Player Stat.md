# 10 Add a Player Stat

The six stats (`Strength`, `Wisdom`, `Dexterity`, `Defense`, `Vitality`, `Speed` — plus the pseudo-stat `Hp`) are wired through **positional arrays and tuples**, which is why adding a seventh is the most invasive content change in the codebase: the compiler will not find all the touch points for you. This is the full checklist. Background: [[04 Player System|04]], [[05 Item, Equipment & Enchantment System|05]].

## Server checklist

1. **The enum** — add the variant to `StatKind` in `item/tables.rs` ([[item/tables.rs##pub enum StatKind|StatKind]]). Keep `Hp` last (see step 3).

2. **The index mapping** — add an arm to `stat_index` in `player/methods.rs` ([[player/methods.rs##fn stat_index|stat_index]]). The index is the stat's position in the `[f32; 7]` accumulator arrays.

3. **Widen the accumulators** — in `recompute_stats` ([[player/methods.rs##pub fn recompute_stats|recompute_stats]]), change `let mut flat = [0f32; 7]; let mut mult = [0f32; 7];` to `[0f32; 8]`, and add a `resolve(N, base_newstat)` field to the `PlayerStats` construction. `Hp` currently resolves separately into `PlayerData.max_hp` via index 6 — if your stat isn't HP-like, insert it before `Hp`'s index and bump `Hp` to 7, or append it at 7 and leave `Hp` at 6; either way, keep `stat_index`, the array width, and the resolve calls in agreement. This positional fragility is exactly why this doc exists.

4. **The table row** — add the column to `PlayerStats` in `player/tables.rs` ([[player/tables.rs##pub struct PlayerStats|PlayerStats]]), and mirror the field order with the `resolve(...)` calls.

5. **Base values** — `compute_base_stats` returns a 6-tuple ([[player/methods.rs##pub fn compute_base_stats|compute_base_stats]]): widen the tuple and the destructure in `recompute_stats`. Decide the per-level scaling (`10 + (level-1)` is the current flat rule for all six).

6. **Admin override** — `change_stats` takes the six stats as positional args ([[main/admin.rs##pub fn change_stats|change_stats]]): add a parameter and include it in the `PlayerStats { ... }` update.

7. **(If it's buffable)** — add a `ConsumableBuffEffect` variant and its `buff_to_modifier` arm ([[06 Add a Consumable or Ability|06]], Path B).

8. **(If it affects combat)** — the combat formulas only read `strength` (outgoing) and `defense` (incoming) in `combat/mod.rs` ([[combat/mod.rs##pub fn compute_player_damage|compute_player_damage]]). A stat that *does* something (e.g. `Luck` affecting XP, `Wisdom` doing anything at all — several stats currently have no mechanical effect) needs its consumer written: find the formula/system that should read it and add it there. `PlayerStats` values are otherwise inert numbers.

## Client checklist

9. **Regenerate bindings** — `bash build.sh` produces the new `SpacetimeDB.Types.StatKind` variant and the new `PlayerStats` field.

10. **Mirror enum** — add the variant to the client-side `StatKind` in `client/Scripts/Resources/Stats/StatKind.cs` ([[StatKind.cs##public enum StatKind|StatKind.cs]]), which `StatsComponent` uses to key its `Stat` resources.

11. **Push the value** — `LocalPlayer.ApplyStats` ([[LocalPlayer.cs##private void ApplyStats|ApplyStats]]) copies each `PlayerStats` field into `StatsComponent.SetFromServer`; add yours.

12. **Display it** — `StatsSidebarComponent` lists the six stats explicitly; add the new one or it stays invisible (it will still *work* — the sidebar is display-only).

## Verify

- `cargo check` in `server/spacetimedb` — catches the enum's exhaustive matches (`stat_index`, `buff_to_modifier`) but **not** the positional arrays: re-read step 3 with fresh eyes, it's the place this change goes wrong.
- `dotnet build client/khvg.csproj`.
- In game: level up (base scaling), equip an item with a modifier on the new stat (seed one — [[04 Add an Item|04]]), and socket an enchantment targeting it; confirm each source folds in and un-folds correctly, and that `change_stats` followed by any inventory change reverts (admin overrides never survive `recompute_stats` — [[09 Admin, Debug & World Lifecycle|09]]).

## Gotchas

- **`Hp` is not a real stat column** — it resolves into `PlayerData.max_hp` with current-hp clamping. Don't model a new stat on `Hp` unless you want that exact special-casing.
- **Modifier stacking is shared** — once the stat exists, items, enchantments, and consumable buffs all fold through the same `fold_modifier` closure with no per-source special-casing; you get all three for free from steps 1-3.
- **Old profiles:** publishing with `--delete-data` (the `build.sh` default) wipes them anyway, so no migration is needed for schema changes in local dev.
- Consider whether you need a stat at all: several existing stats already have no mechanical consumer, so a new inert number is cheap to add but easy to over-use — step 8 is where the real design work is.
