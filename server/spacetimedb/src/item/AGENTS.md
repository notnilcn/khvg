# item/ AGENTS.md

Catalog tables + seeds for items and enchantments. See `server/AGENTS.md` for the module
map and the SpacetimeDB Rust rules — they all apply here. This file is the focused recipe
for **adding abilities** (the most common task touching this module).

## The recipe

**Reusing an existing `AbilityEffect`** (`Heal`, `Buff`, `DeleteBullets`, `SplitBullets`,
`AttractBullets`, `DeleteBulletsInRect`, the status effects — `MarkBleed`, `MarkStore`,
`StoreIncoming`, `Invulnerable`, `DamageShield`, `ReduceDamage`, `RedirectToCaster`,
`RegenAura` — the P2 enemy-side effects — `Curse`, `Bash`, `Slash`, `Wall` — the P3
ground-zone effect — `CastZone(ZoneParams)` — and the P4 movement effects —
`Teleport(TeleportParams)`, `Grapple` — and the P5
`Spell(SpellParams)` shape-damage spells): seed-only.

1. `seeds.rs::seed_world_items` — new `Item::seed` entry: `equip_slot: EquipSlot::Ability`,
   one `ItemBehavior::Ability(AbilityBehavior { effect, potency, duration,
   cooldown_seconds, max_charges })` (`max_charges: 0` = unlimited), and the class gate:
   `stat_requirements` is one of the `REQ_*` consts at the top of `seeds.rs` (P6 —
   primary stat ≥ 15, secondary ≥ 12 per the doc's class headers; `NO_REQUIREMENTS` for
   generic gear). Single-effect ability seeds go through the `ability_item` helper,
   which takes the gate as its last parameter.
2. `main/seeds.rs::seed_default_textures` — a `TextureEntry` if the `texture_id` is new
   (missing entry = blank icon, no error).
3. `cargo check` → `server/build.sh` (regens bindings, publishes, wipes + reseeds the DB).

**New effect type** — additionally:

1. `tables.rs` — add the `AbilityEffect` variant. **Unit or single-field variants only**;
   ≥2 payload values must be wrapped in a struct (e.g. `RectParams`). Multi-field tuple
   variants fail with "must be a unit variant or a newtype variant" plus a misleading
   `Serialize`/`Deserialize` cascade.
2. `player/reducers.rs` — arm in `scale_ability_effect` (multiply every scalable dim by
   the position multiplier) and in `apply_ability_effect` (the shared dispatch both
   `activate_ability` and the P4 charge release call). Self effects delegate to
   `apply_consumable_effect`; bullet-control effects append a `BulletControlEvent`
   (`enemy/instance_tables.rs`, already subscribed by every client). If the effect is
   charge-capable (hold-to-charge), decide that in `ability_is_charge_capable` — the
   client mirrors it (`LocalPlayerInventoryComponent.IsChargeCapable`).
3. `enemy/instance_tables.rs` — new `BulletControlKind` variant if bullet-control. Prefer
   encoding geometry into the existing event fields (DeleteRect/SplitRect use x/y = origin,
   target_x/y = far end, radius = width; Attract's `duration` is the black-hole linger
   time, 0 = one-shot) over new columns; document the encoding in the table's doc comment.
4. Client mirror (not this directory): query/visual in
   `Components/Bullets/BulletControllerComponent.cs`, echo arm in its binder handler,
   optimistic arm in `LocalPlayerInventoryComponent.TryActivateAbility`, display line in
   `ItemSidebarComponent.FormatAbility`. Server-side fallbacks (zero-length direction,
   range clamp) must match the client's optimistic apply exactly.
5. Same build/publish flow.

The full contributor-facing tutorial is
`docs/Technical Design Docs/Workflows/Adding an Ability (Ability Authoring Workflow).md`.

## Rules that bite here

- **Capability is behavior, not slot.** Look up with `ability_behavior(&item)` /
  `consumable_behavior(&item)` in `player/methods.rs`, never by inspecting `equip_slot`.
- **Slot indices are load-bearing on both sides:** 0 weapon, 1–4 hotbar, 5 accessory,
  6 armor, 7–30 general, 31 bag, 32–37 ability (`ABILITY_SLOT_START`/`COUNT`). Ability
  items span `slot_cost` contiguous cells; the head holds the item, followers get
  `occupied_by`. Backpack stores ability items loose in one cell.
- **`ABILITY_POSITION_MULTIPLIERS` is mirrored client-side**
  (`LocalPlayerInventoryComponent.AbilityPositionMultipliers`) — change both together.
- **Items are not consumed** — runtime state (cooldown/charges) lives on the
  `PlayerInventorySlot` row, never the `Item` row. The catalog is shared/static.
- **Content = seeds.** Every publish wipes the DB; anything not seeded does not exist.
  `Item::seed`/`Enchantment::seed` upsert by primary key, so reseeding is idempotent.
- Pre-equip for testing via `place_span` in `player/methods.rs::try_scaffold_profile`
  (see the Skull/Tome entries) rather than inventing new machinery.

## Commands

```bash
cargo check --manifest-path server/spacetimedb/Cargo.toml   # compile gate
server/build.sh                                             # bindings + publish (DB wipe + reseed)
spacetime sql bullethell "SELECT item_id, equip_slot, slot_cost FROM item"   # inspect the catalog
spacetime call bullethell give_item <username> <profile_name> "Delete Orb"   # hand an item out (admin)
```
