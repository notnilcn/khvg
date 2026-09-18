# item/ AGENTS.md

Catalog tables + seeds for items and enchantments. See `server/AGENTS.md` for the module
map and the SpacetimeDB Rust rules — they all apply here. File index (generated —
regenerate, never hand-edit): [[server/spacetimedb/src/item/pointers.md|pointers]]. This file is the focused recipe
for **adding abilities** (the most common task touching this module).

## The recipe

**Reusing an existing `AbilityEffect`** — self/ally effects (`Heal`, `Buff`), bullet-control
(`DeleteBullets`, `SplitBullets`, `AttractBullets`, `DeleteBulletsInRect`), status effects
(`MarkBleed`, `MarkStore`, `StoreIncoming`, `Invulnerable`, `DamageShield`, `ReduceDamage`,
`RedirectToCaster`, `RegenAura`), enemy-side effects (`Curse`, `Bash`, `Slash`, `Wall`),
ground-zone effects (`CastZone(ZoneParams)`), movement (`Teleport(TeleportParams)`,
`Grapple`), and shape-damage spells (`Spell(SpellParams)`): seed-only.

1. `seeds.rs::seed_world_items` — new `Item::seed` entry: `equip_slot: EquipSlot::Ability`,
   one `ItemBehavior::Ability(AbilityBehavior { effect, potency, duration,
   cooldown_seconds, max_charges })` (`max_charges: 0` = unlimited), and the class gate:
   `stat_requirements` is one of the `REQ_*` consts at the top of `seeds.rs` (primary
   stat ≥ 15, secondary ≥ 12 per the doc's class headers; `NO_REQUIREMENTS` for generic
   gear). Single-effect ability seeds go through the `ability_item` helper,
   which takes the gate as its last parameter.
2. `main/seeds.rs::seed_default_textures` — a `TextureEntry` if the `texture_id` is new
   (missing entry = blank icon, no error).
3. `cargo check`, then the build/publish flow in `server/AGENTS.md` (Commands).

**New effect type** — additionally:

1. `tables.rs` — add the `AbilityEffect` variant. **Unit or single-field variants only**;
   ≥2 payload values must be wrapped in a struct (e.g. `RectParams`). Multi-field tuple
   variants fail with "must be a unit variant or a newtype variant" plus a misleading
   `Serialize`/`Deserialize` cascade.
2. `player/reducers.rs` — arm in `scale_ability_effect` (multiply every scalable dim by
   the position multiplier) and in `apply_ability_effect` (the shared dispatch both
   `activate_ability` and the charge release call). Self effects delegate to
   `apply_consumable_effect`; bullet-control effects append a `BulletControlEvent`
   (`enemy/instance_tables.rs`). If the effect is charge-capable (hold-to-charge),
   decide that in `ability_is_charge_capable` (client mirror noted in `server/AGENTS.md`).
3. `enemy/instance_tables.rs` — new `BulletControlKind` variant if bullet-control. Prefer
   encoding geometry into the existing event fields (encodings listed in `enemy/AGENTS.md`)
   over new columns; document the encoding in the table's doc comment.
4. Client mirror (not this directory): query/visual in
   `Components/Bullets/BulletControllerComponent.cs`, echo arm in its binder handler,
   optimistic arm in `LocalPlayerInventoryComponent.TryActivateAbility`, display line in
   `ItemSidebarComponent.FormatAbility`. Server-side fallbacks (zero-length direction,
   range clamp) must match the client's optimistic apply exactly.

The full contributor-facing tutorial is
`docs/Technical Design Docs/Workflows/Adding an Ability (Ability Authoring Workflow).md`.

## Rules that bite here

- Seeds upsert by primary key (`Item::seed`/`Enchantment::seed`), so reseeding is idempotent.
- Pre-equip for testing via `place_span` in `player/methods.rs::try_scaffold_profile`
  (see the Skull/Tome entries) rather than inventing new machinery.

## Commands

```bash
spacetime sql bullethell "SELECT item_id, equip_slot, slot_cost FROM item"   # inspect the catalog
spacetime call bullethell give_item <username> <profile_name> "Delete Orb"   # hand an item out (admin)
```
