# server/AGENTS.md

Per-module map of the SpacetimeDB module (`server/spacetimedb/`, crate `BulletHell`, `spacetimedb 2.2`), followed by the Rust/SpacetimeDB rules that apply to every edit here. See the root `AGENTS.md` for the project-wide domain map and the publish workflow.

# Module Map

`server/spacetimedb/src/` is split into nine modules: `enemy/`, `player/`, `world/`, `item/`, `main/`, `combat/`, `status/`, `anticheat/`, `chest/`. The baseline layout is `mod.rs` / `tables.rs` / `methods.rs` (pure helpers/math) / `reducers.rs` / `views.rs`, with two departures: `world/` splits its `methods.rs` into focused files (`prng`/`noise`/`heights`/`hex`/`hex_region`/`masks`/`pathing`/`wrap`/`aoi`) plus a composable `terrain/` pipeline; and `combat/` splits into `mod.rs` (docs + policy types + re-exports) / `calc.rs` (pure damage math) / `apply.rs` (the sinks). `main/` adds a shared `time.rs` clock helper. Nothing here is stubbed — no `todo!`/`unimplemented!` anywhere in the module. `player/views.rs` hosts the shared `nearby_indices`/`nearby_indices_from_chunk` AOI helpers the other modules' views build on.

Composition mapping (mirrors the client's entity/component structure): one table per concern = a component, and ids like `profile_id`/`enemy_id` act as entity ids joining a logical entity's rows across tables. Archetype helpers bundle the rows of one entity: `player/methods.rs::try_scaffold_profile`/`teardown_profile` and `enemy/methods.rs::spawn_enemy_archetype`/`despawn_enemy_archetype`. An enemy keeps position/hp/phase flat on the `Enemy` row itself.

### `enemy/`
**`enemy/AGENTS.md` is the canonical doc for this module** — file map, tick/phase/sequence semantics (repeat/loop/catch-up/stagger rules), event-table field encodings, and the add-an-enemy recipe. Read it before touching anything here. Map-level notes only:
- `def_tables.rs` + `defs/` = static content (seeds; difficulty is compile-time), `instance_tables.rs` = runtime rows + event tables, `methods.rs`/`reducers.rs` = the 100 ms simulation, `emitters.rs` = the `Emitter` authoring vocabulary, `views.rs` = client exposure (`nearby_enemies`, `enemy_templates`).
- No `EnemyZone` table — spawn regions are `BiomeRegion`, a `world/` table (`center_hex_q/r`, `spawn_radius`, `max_enemies`, `enemy_template_ids`, `region_id`).
- **Wrap-aware pathfinding** for AggroTarget goals in another chunk lives in `enemy/methods.rs` (`resolve_aggro_goal` + `path_decision`/`advance_path_next`), backed by `world/pathing.rs`; the path scratch fields (`path`/`path_next`/`path_goal_q/r`/`path_tick`) live on `EnemyBehavior`. See `enemy/AGENTS.md`.

### `player/`
- `tables.rs` — `LoggedInPlayer`/`LoggedOutPlayer` (login state), `PlayerProfile`, `PlayerData` (level/xp/hp/max_hp + `defense` + `base_speed` — resolved gear/buff defence and move-speed cap live here, not in PlayerStats, same pattern as max_hp), `PlayerStats` (exactly the **six allocatable stats**: strength/wisdom/dexterity + the damage_dealer/supporter/artisan temperaments), `PlayerStatAllocation` (the allocation *input*: per-stat assigned points + `unspent_points`; PlayerStats is the recomputed output), `PlayerInventorySlot`, `PlayerPosition` (x/y + movement as an angle + scalar: `movement_direction`/`movement_speed` — speed is the player's current speed, 0 when idle, clamped server-side to the resolved `base_speed`; clients reconstruct velocity from the pair for dead reckoning), `PlayerRotation` (`screen_rotation` — camera/facing angle, split out of PlayerPosition so it streams on its own faster cadence; `chunk_index` mirrors the position row so AOI views filter identically), `PlayerChunk`, `PlayerExploredChunk` (per-profile explored-map snapshots, one row per revealed chunk with its denormalized `hexes` payload — see `explored.rs`), `ActiveConsumableEffect` + `ConsumableEffectSchedule`, `LootDrop`. `PlayerInventorySlot` is **one row per slot** (auto-inc `slot_id` PK, btree on `profile_id` + composite `(profile_id, slot_index)`), keyed by `SlotRole` (Weapon/Hotbar/Accessory/Armor/General/Bag/Ability) — a slot's acceptance set is derived from its role by `slot_allowed` in `methods.rs` (General accepts **everything**: the backpack stores ability items loose in one cell — `slot_cost` only applies inside the ability region). Slot indices stay load-bearing on both sides: `0` weapon, `1–4` hotbar consumables, `5` accessory, `6` armor, `7–30` general (backpack), `31` bag, `32–37` ability (`ABILITY_SLOT_START`/`ABILITY_SLOT_COUNT`). Ability items span `Item.slot_cost` contiguous ability slots: the head row holds the item, follower rows are marked `occupied_by = Some(head_index)`. The row also carries per-slot runtime state: `cooldown_until`, `charges` (`u32::MAX` = unlimited), `active_toggle`. `LootDrop` carries `expires_at` (rejected at pickup, swept on the 1 Hz tick) plus the dropped slot's `cooldown_until`/`charges` — ability runtime state survives drop → pickup, and whoever picks it up inherits the residual cooldown. Three server-only tables (not `public`) back the validation layer: `PlayerMovementState` (last movement-report timestamp behind the displacement clamp; restamped by `teleport_player`), `PlayerDisplacementAllowance` (one-shot Grapple window — `report_movement`'s clamp accepts a single oversized report up to `max_distance` while the row lives), `PlayerHitRate` (1 s fixed-window token bucket with separate `outgoing`/`incoming` counters for `report_enemy_hit`/`report_hit`).
- `explored.rs` — `reveal_aoi` (PLAN §4.2): snapshots the destination AOI chunk disk (`surrounding_chunk_coords` at `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`) into per-chunk `PlayerExploredChunk` rows, set-diffed against the profile's own rows (≤19 today — a 2-ring disk). Called from `try_scaffold_profile` (spawn disk), a real `PlayerChunk` crossing in `report_movement`, and `teleport_player`. Payload = `HexTile` fields + `BuildingTile.height`, sorted by `(hex_q, hex_r)` so inserts are scan-order independent; world generation is static after `internal_generate_world`, so snapshots never go stale. `delete_profile` deletes the rows explicitly — they are deliberately NOT in `teardown_profile` (death must not erase the map).
- **`PlayerChunk` exists so views don't key off `PlayerPosition`.** It is written only when the player's chunk actually changes, so AOI views recompute on real chunk crossings instead of on every 10 Hz `report_movement`. Don't "simplify" an AOI view to read `PlayerPosition`.
- `reducers.rs` — `set_username`, `create_profile`, `delete_profile` (also deletes the profile's `PlayerExploredChunk` rows — they survive death by design), `join_world`, `leave_world`, `report_movement` (x/y + `movement_direction`/`movement_speed`, speed clamped to the resolved `base_speed`, further × the minimum `SelfSlow` factor while a charge is held; position is displacement-clamped: wrap-aware distance vs the stored row, capped at `base_speed` × dt × `MOVE_SLACK` (floor `MIN_REPORT_DISPLACEMENT`) by clamping along the reported vector — clamp, not reject, so legit lag doesn't rubber-band; one oversized report is accepted while a Grapple-stamped `PlayerDisplacementAllowance` lives; `teleport_player` is exempt and restamps the `PlayerMovementState` row; elevation (levels derived from per-hex `BuildingTile.height` via `world::reducers::height_to_level`): deltas within ±`MAX_WALKABLE_*_DELTA` walk free, rising more than `MAX_WALKABLE_UP_DELTA` rejects the position claim, dropping more than `MAX_WALKABLE_DOWN_DELTA` is a fall — `falling_until` lockout on `PlayerMovementState`, displacement-budget exemption up to `MAX_FALL_DISPLACEMENT`, and `FALL_DAMAGE_PER_LEVEL` × drop damage only past `FALL_SAFE_DROP`; a live grapple allowance exempts the report from elevation rules entirely; unchanged positions skip the write; a real chunk crossing also calls `explored::reveal_aoi` for the destination AOI disk), `report_screen_rotation` (updates `PlayerRotation`, mirroring `chunk_index` from the position row; sub-~0.5° changes skip the write), `report_hit` (rate-limited via `PlayerHitRate`'s `incoming` counter at `INCOMING_HIT_RATE_CAP` ≈ 30/s), `pickup_drop` (wrap-aware `INTERACT_RADIUS` range check and `expires_at` rejection up front; restores the drop's carried `cooldown_until`/`charges`; ability items auto-equip into the first free span **if stat requirements pass**, else fall back to loose backpack storage), `drop_item` (dropping a follower drops the whole span; stamps the slot's `cooldown_until`/`charges` onto the `LootDrop` row), `swap_slots` (span handling is **region-based**, not item-based: it only triggers when source or target is an Ability-role slot; a span move needs `slot_cost` contiguous free cells at the target, source cells count as freed, runtime state travels with the item in both directions (entering the region from the backpack initializes charges from the item only when the source carried no state); ability items swapped between non-ability slots are plain single-cell swaps where runtime state (cooldown/charges/toggle) also travels with the item; **stat requirements gate any item entering an equipped role from loose storage**), `use_item` (Hotbar-role slots only, matching the client UI; rejects items with an `Ability` behavior — consuming a span head would orphan its followers), `activate_ability` (takes a cursor `target_x/y`, clamped to `MAX_ABILITY_TARGET_RANGE` around the server position row; cooldown + charges, potency scaled by `ABILITY_POSITION_MULTIPLIERS`; Heal/Buff reuse `apply_consumable_effect`, bullet-control effects append a `BulletControlEvent` directly with the behavior's radius; fires equipped `OnAbilityUseBuff` enchantment triggers; does **not** consume the item; rejects charge-capable items — those route through `status/reducers.rs`; its validation/dispatch/spend are the shared `resolve_ability_slot`/`apply_ability_effect`/`spend_ability` helpers the charge reducers reuse; `Teleport` moves the position row via `teleport_player`, `Grapple` validates only, `Spell` deals shape damage through `apply_damage_to_enemy` (per-pellet for volleys) and appends an `AbilityVisualEvent`), `set_slot_toggle` (weapon slot only; validates against `weapon_toggle_options` count), `allocate_stat` (moves unspent skill points into one of the six allocatable stats; rejects Hp/Defense/BaseSpeed), `apply_enchantment`, `remove_enchantment`, `tick_status_effects` (scheduled, wired to `ConsumableEffectSchedule`; the single 1 Hz status tick the status module also hangs off)
- `methods.rs` — guards (`require_logged_in`, `require_in_world`, `require_in_lobby`, `is_admin`), stat/level math (`xp_for_level`, `compute_level`, `compute_base_max_hp`, `recompute_stats`; base stats are just the flat `BASE_STAT` constant — growth comes from allocation), `teleport_player` (position/chunk write identical to `report_movement`'s, plus the chunk-crossing AOI reveal), `internal_gain_xp` (grants `SKILL_POINTS_PER_LEVEL` unspent points per level), `apply_consumable_effect` (heals delegate to `combat::heal_player`), archetype helpers `try_scaffold_profile`/`teardown_profile` (one union cleanup list used by BOTH `kill_player` death cleanup and `delete_profile`, incl. the allocation and rotation rows; `try_scaffold_profile` also reveals the spawn AOI disk), `kill_player` (the single death-cleanup entry combat calls — teardown + lobby move), `players_near_point` (reducer-side AOI circle query, logged-in players only — for AoE ally targeting and zone membership), slot helpers (`slot_allowed`, `get_slot`, `update_slot`, `ability_position_multiplier`, `find_free_span`, `place_span`, `clear_span`), `check_stat_requirements`, `find_profile_by_name`, and `transfer_item_to_inventory` (the single acquire path: `store_item_in_inventory` + `recompute_stats` — outside callers must not pair them by hand).
- `views.rs` — per-player subscription views (`local_player`, `local_lobby_player`, `local_player_profiles`, `local_player_active_profile`, `local_player_data`, `local_player_stats`, `local_player_stat_allocation`, `local_player_inventory`, `local_player_position`, `local_player_explored` (the caller's explored-map rows, `PlayerExploredChunk`), `nearby_remote_players` (excludes the caller's own row), `nearby_remote_player_rotations`, `nearby_remote_players_profiles` (AOI-filtered through the same chunk set as `nearby_remote_players`, not all of `LoggedInPlayer`), `nearby_loot_drops`) plus the shared AOI helpers named above

**`recompute_stats` is the one place stats resolve.** It folds allocated skill points, every `StatModifier` from equipped items, their socketed **and innate** enchantments, and active consumable buffs into per-`StatKind` `(flat, mult)` accumulators, then resolves `(base + flat) * (1 + mult)`. Design rules that live here:
- **Mults are additive, not compounding** — two `+10%` sources give `+20%`, not `+21%`.
- **"Equipped" is an explicit `SlotRole` check** — only Weapon/Accessory/Armor heads contribute stats at scale 1.0; Ability heads contribute with their stat modifiers scaled by `ABILITY_POSITION_MULTIPLIERS[head_index - ABILITY_SLOT_START]` (first ability stronger). Span followers (`occupied_by.is_some()`) never contribute. Enchantment *behaviors* (as opposed to stat modifiers) are never position-scaled.
- `StatKind::Hp`, `StatKind::Defense` and `StatKind::BaseSpeed` are modifier-only: no `PlayerStats` field, not allocatable. They resolve into `PlayerData.max_hp` (over `compute_base_max_hp`, with `hp` clamped down), `PlayerData.defense` and `PlayerData.base_speed` (over `BASE_SPEED`) respectively.

### `world/`
- `tables.rs` — `MapConfig`, `BuildingTile` (per-hex world-unit `height`, unclamped — the addon's `height_map` value; gameplay derives the elevation level as `floor(height / WORLD_HEIGHT_PER_LEVEL)`), `TextureEntry` (a texture's `res_path2d` sprite/icon path + `res_path3d` model path — empty string = no 3D representation, e.g. the 2D shadow decals), `LayeringRule`, `BaseAdjacencyRule`, `OverlayAdjacencyRule`, `DecorGroundRule` (+ the `TextureKind` enum). Read config through **`MapConfig::load(ctx, fallback_chunk_cols, fallback_chunk_rows)`** rather than re-deriving defaults; the fallback differs by caller — simulation paths pass `0, 0`, `main/admin.rs` passes `1, 1`. `player/views.rs` is the one place that reads `map_config` directly (a `ViewContext` can't call the helper), defaulting both to `1`.
- `def_tables.rs` — world-gen definition inputs: `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `GroundTextureConfig`/`OverlayTextureConfig`/`DecorConfig`, weight structs, `ManualBiomeInput`/`ManualRegionInput`
- `instance_tables.rs` — generated world rows: `BiomeRegion`, `HexTile` (one row per hex + the per-edge `river_mask`/`road_mask`/`wall_mask` bitmasks), `HexDecor` (the payload that populates the client's tile scenes)
- `prng.rs` — deterministic PRNG: `splitmix64`/`hash_to_unit`/`next_unit` + weighted picks
- `noise.rs` — `value_noise2d` + seed offsets
- `hex_region.rs` — `HexMapCellId` + plane-bounded iterators (`HexPlanes`, `HexMapIterAxial`/`Radial`/`Cube`) ported from `godot-hex-map`, with wrap-aware `wrap_cell`/`WrappedHexIter` built on `wrap_hex_coords` (`y` is never wrapped). Lockstep sibling `client/Scripts/World/HexRegion.cs`; the source's test assertions live in this file's `#[cfg(test)]` module. Available, not yet adopted by server gameplay code.
- `masks.rs` — the per-edge mask vocabulary for `HexTile.river_mask`/`road_mask`/`wall_mask`: `has_edge`/`set_edge`/`clear_edge`/`opposite_direction`/`direction_index`, one bit per edge in `HEX_NEIGHBOR_DELTAS` order. Generation always writes both hexes of an edge (mirrored bit `(d + 3) % 6`), so a half-set edge never exists; `wall_mask` is reserved and always 0 today.
- `pathing.rs` — wrap-aware deterministic Dijkstra (`find_path`/`path_cost`) + the table-free `HexTerrain`/`StepCostConfig` cost model (`terrain_step_cost`: cliffs block both ways, a river edge costs the bridge surcharge, an existing road earns a discount, walled edges are impassable). Callers build one `HexTerrainSnapshot` per tick. `PathDecision` + `path_decision`/`advance_path_next` drive enemy path following — see `enemy/AGENTS.md`.
- `hex.rs` — hex/chunk math, mostly thin wrappers over the **`hexx` crate** (`to_higher_res`/`to_lower_res`/`from_hexmod_coordinates`): `world_to_hex`, `hex_to_world`, `hex_area`, `chunk_center_hex`, `world_to_chunk`, `hex_to_chunk` (the `to_lower_res` chunk-owner helper; lockstep with client `HexMath.ToLowerRes`, the addon's `MarchingSquaresHexRingsGrid.to_lower_res`, and their tests), `inv_hexmod`, `HexCell`, `enumerate_world_hexes`, hex neighbor helper (`hex_neighbors` over `HEX_NEIGHBOR_DELTAS`), plus `spiral_chunk_index` — a hand-rolled bijective hex-spiral chunk id whose lockstep client sibling is **`HexMath.SpiralIndex`** (`client/Scripts/World/HexMath.cs`, consumed by both hex-grid overlay components); both copies must change together. Background on the coordinate schemes: [redblobgames hexagons](https://www.redblobgames.com/grids/hexagons/), [hexagon tiling of a hexagonal grid](https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid), [hexmod representation](https://observablehq.com/@sanderevers/hexmod-representation).
- `wrap.rs` — torus wrap: `wrap_world_pos`/`wrap_chunk_coords` + wrapped distance (the continuous world is a torus; the hex grid is not), plus **`wrap_hex_coords`** (canonical hex wrap — chunk coords wrap, local offset preserved) and **`wrapped_hex_neighbors`** (the 6 canonical-lap neighbors). Lockstep with client `HexMath.WrapHex` + the addon's `MarchingSquaresHexRingsGrid.wrap_cell` (the hex-rings MST mode canonicalizes seam lookups through it) — all copies change together. `#[cfg(test)]` covers idempotence, neighborhood preservation, and lap-displacement.
- `aoi.rs` — `surrounding_chunk_coords` (wrapped chunk disk as `(q, r, index)` triples, deterministic ring order, deduped on a small torus — the explored-map reveal needs the coords), `surrounding_chunk_indices` (its index projection), `chunk_indices_covering` (chunk-index superset of a world-space circle, for reducer-side AOI)
- `terrain/` — terrain generation as composable passes: the elevation pass (`value_noise2d` × `ELEVATION_LEVELS` × `WORLD_HEIGHT_PER_LEVEL` → each `BuildingTile.height`, unclamped, with the derived level collected for the mask passes), `voronoi.rs` (biome/region seed distribution), `rules.rs` (`LayeringRule`/adjacency/decor conflict checks, `NeighborTagMap`, **wrap-aware `decided_neighbor_tags`** so seam hexes consult their true across-the-seam neighbors), `ground.rs` + `overlay.rs` (`choose_ground`/`choose_overlay`, called per hex by `write_hex_tile` in `mod.rs`), `decor.rs` (`place_decor`), `rivers.rs` (`generate_rivers` — weighted downhill walks into `HexTile.river_mask`, `RIVER_SEGMENT_PERCENT` budget, single-edge stubs rolled back), `roads.rs` (`generate_roads` — Region centers connected in `region_id` order via `pathing::find_path`, bridge surcharge on river edges, road bits written on both sides; **a road never shares an edge with a river**), `mod.rs` (orchestrator — `internal_generate_world_proc`/`internal_generate_world_manual` are two front-ends over one parameterized `run_generation`; mask passes run after all hex rows exist and persist only changed rows in `enumerate_world_hexes` order; deterministic draw order preserved)
- `reducers.rs` — `place_building` (admin-only world authoring — no client UI calls it, same category as `add_chest`), `remove_building`
- `views.rs` — `all_textures`, `nearby_terrain_tiles`, `nearby_hex_decor` (AOI-filtered via `nearby_indices_from_chunk` with `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`)

### `item/`
- **`src/item/AGENTS.md` has the ability-authoring recipe** (seed-only vs new effect type) — read it before adding abilities.
- `tables.rs` — single unified catalog table `Item` (`item_id`, `display_name`, `description`, `texture_id`, `equip_slot`, `slot_cost`, `stat_modifiers`, `behaviors`, `max_enchantments`, `innate_enchantment_ids`, `stat_requirements`); `Enchantment` (`enchantment_id`, …, `stat_modifiers`, `behaviors`, `allowed_slots`); `EquipSlot` enum (Weapon/Armor/Accessory/Bag/Consumable/Ability — **artifacts are 1-cost abilities**, there is no Artifact variant); `StatKind` (the six allocatable stats **Strength/Wisdom/Dexterity/DamageDealer/Supporter/Artisan** — temperaments are mechanically identical to attributes — plus modifier-only `Hp`/`Defense`/`BaseSpeed`)/`StatMode`/`StatModifier`; `StatRequirements` (flat per-stat minimums, enforced when an item enters an equipped slot; the class gates are the `REQ_*` consts in `seeds.rs`); `ItemBehavior` (`Weapon(WeaponBehavior)`, `Consumable(ConsumableBehavior)`, `Ability(AbilityBehavior)`) plus `WeaponPattern`/`ConsumableEffect`/`ConsumableBuffEffect`. The full `AbilityEffect` and `EnchantmentBehavior` variant lists live in `item/AGENTS.md`. `slot_cost` is 1 for everything except multi-slot ability items (tome 3, spell 2, shield 6); `AbilityBehavior` carries `cooldown_seconds`/`max_charges` (0 = unlimited) on top of its `AbilityEffect`. Weapon `damage` is **per trigger pull, divided among the pattern's bullets** (doc 02 defence model). `innate_enchantment_ids` are always applied while equipped, don't eat sockets, can't be removed. An item's shot-pattern **toggle options** = its own `Weapon` behaviors + `WeaponToggle` grants from equipped enchantments, indexed by the slot's `active_toggle`.
- `methods.rs` — item behavior lookups and weapon composition: `consumable_behavior`/`ability_behavior`, `ability_is_charge_capable` (the one place charge-capability is decided; the client mirrors it), `equipped_head_slots`, `equipped_enchantment_behaviors` (deterministic fold order — slot index ascending, innate before socketed; the client's `EffectiveWeaponResolver` mirrors it exactly), `weapon_toggle_options`, `resolve_effective_weapon` → `EffectiveWeapon` (toggle-selected `WeaponBehavior` with effective `shot_count`, plus `flat_bullet_mod`/`true_flat`/`true_pct`).
- `reducers.rs` — admin catalog reducers: `give_item`, `remove_item`, `upsert_item`, `upsert_enchantment`
- `views.rs` — `all_items` (the public view the client's `CatalogComponent` caches), `all_enchantments`
- `seeds.rs` — `seed_world_items` (incl. the two-pattern `Bow`, the innate-toggle `Fan Bow`, the class-gated bullet-master toggle weapons `Marksman Bow`/`Warbow`, and the `Null Sigil`/`Delete Orb` bullet-delete abilities), `seed_world_enchantments` (incl. `splitting` shot-doubler, `echoing` on-ability buff, `point_blank`/`scatter_fan` innate-only toggles), `impl Seed for Item` (`impl Seed for Enchantment` lives in `main/seeds.rs`). The class gates are the `REQ_*` `StatRequirements` consts at the top of the file (thresholds + the Paladin/Trickster deviations documented there); the `ability_item` helper takes them as a parameter.

**Slot and behavior are deliberately independent axes** — `equip_slot` says only *where an item goes*, `stat_modifiers` + `behaviors` say *what it does*. That is what makes "an accessory that fires projectiles" expressible (`equip_slot: Accessory, behaviors: [Weapon(..)]`) and is the prerequisite for procedural item generation. Consequences worth knowing before changing this:
- There are **no per-type side tables** (no `Weapon`/`Armor`/`Artifact`/`Consumable` rows to join) and no `ArtifactEffect` — an artifact "effect" is just a `StatModifier` with `mode: Flat`. Capability lookups are behavior-based, never `equip_slot`-based (see `item/AGENTS.md`).
- `use_item` guards on **having a `Consumable` behavior**, not on `equip_slot == Consumable`. It additionally requires the `Hotbar` slot role (matching the client UI, which only exposes UseItem on the hotbar keys) and rejects items carrying an `Ability` behavior, so a span head can never be consumed out from under its follower cells.
- The same `Vec<enum>`-on-one-row shape is used by `EnemyTemplate.phases`.
- Not built: the procedural item generator; `Bag` capacity as a behavior. Rationale for this schema: `docs/Technical Design Docs/(implemented) Item system - split stats from behavior.md`.

### `combat/`
- `mod.rs` (docs + mitigation policy types + re-exports), `calc.rs` (pure damage math, no row writes), `apply.rs` (the sinks) — shared combat pipeline, the only place damage is computed and applied: damage formulas (`compute_player_damage` outgoing — returns pre-defence `base` + post-mitigation true-damage bonus, split at the mitigation seam; `compute_incoming_damage` mitigation), `deal_damage_to_player` (public wrapper over `deal_damage_to_player_opts`, whose `IncomingDamageOptions{skip_conversion, respect_floor}` gates the status-module consult points: intercept before mitigation, HpFloor clamp after; mitigate → subtract → on zero delegates death cleanup to `player::methods::kill_player` — combat never calls `teardown_profile` or touches the lobby rows itself), **`apply_damage_to_enemy`** (the one enemy damage sink — weapon hits, spells, DoT ticks, zone damage, store-damage releases, one `DamageRequest` policy: `NORMAL` subtracts flat defence min 1 and consults the target's enemy statuses — ArmorBreak zeroes defence, Vulnerability multiplies the final damage; `DOT_TICK` skips the outgoing consults; `FLAT_RELEASE` skips mitigation for already-mitigated releases; kill → `despawn_enemy_archetype` + `internal_gain_xp` for `xp_credit`; survive → phase recompute + re-aggro onto `aggro_from`, skipped when `None`), `deal_damage_to_enemy` (weapon-path wrapper: `compute_player_damage` → delegate `(Normal, Some(profile_id), Some(ctx.sender()))`; `_bonus` variant merges a pre-mitigation flat bonus — the armed StoreIncomingBash pool — into the same hit), `heal_player`. **Damage model (doc 02):** weapon `damage` is per trigger pull — each reported bullet hit deals `damage × (1 + str×0.002) / effective_shot_count`, minus the target's **flat defence** (`EnemyTemplate.defense` for enemies, `PlayerData.defense` for players), minimum 1; true-damage enchantment bonuses apply after defence. `report_enemy_hit` resolves base damage and delegates; `report_hit(event_id)` attests the claim against the server-only `BulletFireAttestation` row stamped at fire time (pellet-pool decrement, TTL sweep) and only then delegates with the stamped damage.

### `status/`
Player status effects with **runtime state** (pools, counters, marks) or **non-stat effects** (reduction %, invuln, floors) — plain pre-baked stat buffs stay on `ActiveConsumableEffect` — plus the enemy-side debuffs (DoT/Slow/Vulnerability/ArmorBreak/Stunned), the stagger bar (`Enemy.stagger`, decays on the tick, bashes fill it, full = Stunned per the template's `stun_behavior`), the ground zones (`GroundZone` — the witch doctor's circles and the wizard's spire, ticked at 1 Hz with live membership), and the ability charge (`AbilityCharge` — one held charge per player). **There is exactly one 1 Hz schedule**: `player/tables.rs`'s `ConsumableEffectSchedule` drives `player/reducers.rs::tick_status_effects`, which runs the consumable expiry pass and then `status::methods::tick` — hang any new per-second status work off `tick`, never add a second schedule.
- `tables.rs` — `ActiveStatusEffect` (one row per (profile, kind); the mark kinds `DamageToBleed`/`StoreDamage` share ONE slot per player — recasting any mark replaces it: bleed pools dump instantly, store pools forfeit), `StatusEffectKind`, `StatusParams` (flat struct, one field per concept, unused = zero; field dual-uses by kind documented on the table), `PurgeKind`/`WagerKind`/`ReleaseKind`, plus the enemy side: `ActiveEnemyStatusEffect` (one row per (enemy_id, kind) — DoT/Slow/Vulnerability/ArmorBreak/Stunned) and `EnemyStatusKind`, plus the ground zones: `GroundZone` (owner/kind/center/radius/magnitude/remaining) and `ZoneKind` (11 kinds), plus `AbilityCharge` (profile_id PK, slot_index, started_at).
- `methods.rs` — the shared entry points every targeting variant (self / allies-in-radius / zone) calls: `apply_status_effect`, `ally_targets`, `intercept_incoming` (fixed order: Invulnerable → DamageShield → RedirectToCaster (loop-guarded) → DamageShare → DamageReductionPct → DamageToBleed; **every hit — even fully absorbed — stamps counter/last_event_at**; DamageShare is LIVE zone membership, splitting the hit across the zone's occupants via the flagged damage path — shared portions never re-intercept), outgoing consults (`outgoing_damage_mult`, `withhold_outgoing`, `convert_outgoing_to_bleed` — the warrior's outgoing bleed mark turns a hit on the mark into an enemy DoT instead of instant damage, `note_damage_dealt`), the enemy-side apply/consults (`apply_enemy_status`, `enemy_defense`, `enemy_vulnerability_mult`, `enemy_movement_mult`, `stunned_magnitude`), `on_enemy_killed` (purges statuses referencing a dead enemy AND that enemy's own debuff rows; wager pools forfeit), `purge_profile_statuses` + `purge_profile_zones` (death/`leave_world`/disconnect — **no status may kill or tick a logged-out player; a caster's zones die with them**; a held charge vanishes too, no cooldown spent), and `tick` (1 Hz, called from `tick_status_effects`: bleed-pool drains via the flagged `deal_damage_to_player_opts` path — never direct HP writes —, Regen, purge evaluation, expiry sweep, charge timeout auto-release (at `MAX_CHARGE_SECONDS`, target = the player's own position), enemy-DoT ticks through `apply_damage_to_enemy_opts` with `DOT_TICK_OPTIONS`, enemy-status expiry/orphan sweep, stagger decay, and the zone pass: ally zones re-apply statuses with `ZONE_STATUS_REFRESH_SECONDS` (live membership each tick, ~1.5s shed), enemy zones refresh debuffs the same way, bursts fire once at expiry, expiry sweep).
- `reducers.rs` — `begin_ability_charge` / `release_ability_charge` (hold-to-charge; validation/dispatch/spend shared with `activate_ability` via `resolve_ability_slot`/`apply_ability_effect`/`spend_ability` in `player/reducers.rs`; begin applies the `SelfSlow` status — enforced by `report_movement`'s speed clamp — release removes it). `internal_release_charge` is the shared release path the tick's timeout auto-release also calls.

### `chest/`
- `mod.rs` (single file) — loot containers at fixed world positions. Tables: `Chest` (x/y + `chunk_index`, set from the wrapped coordinate like `spawn_enemy`) and `ChestItem` (one row per item + the parent's mirrored `chunk_index`; `regenerating` rows are never consumed by `take_from_chest` — the surviving row IS the next copy), plus server-only `ChestTakeCooldown` ((profile_id, chest_id) → last_taken). Reducers: `add_chest`/`add_item_to_chest`/`add_all_items_to_chest` (admin-only world-authoring; bulk fill is its own reducer, not a magic item_id), `take_from_chest` (any in-world player within wrap-aware `INTERACT_RADIUS`; regenerating chests enforce a per-player `CHEST_TAKE_COOLDOWN_SECONDS` cooldown via `ChestTakeCooldown`; placement goes through the single `transfer_item_to_inventory` entry in `player/methods.rs`, also used by `pickup_drop`). Views: `nearby_chests` and `nearby_chest_items` (both the `nearby_loot_drops` AOI chunk-index OR-chain). Client side: `Scenes/chest.tscn` (`Items/Chest.cs`).

### `anticheat/`
- `tables.rs` — `PlayerHitFlag` (server-only, not `public`): a witness flag that some client saw `hit_player` get hit by a bullet (`reported_by`, `reported_at`; no bullet identity). Used to corroborate the victim's self-reported `report_hit`.
- `reducers.rs` — `flag_player_hit` (requires in-world; self-reports silently ignored). Flag rows older than `HIT_FLAG_MAX_AGE_SECONDS` (60 s) are swept in the 1 Hz `tick_status_effects` pass.

### `main/`
- `global.rs` — single home for tunable constants, grouped by subsystem: world gen/map grid (`DEFAULT_CHUNK_HEX_RADIUS`, `DEFAULT_CHUNK_COLS`/`ROWS`, `DEFAULT_HEX_OUTER_RADIUS`, `BIOME_VORONOI_SEED_BUDGET`) and river/road masks (`RIVER_SEGMENT_PERCENT`, `RIVER_MOISTURE_NOISE_SCALE`, `ROAD_BASE_STEP_COST`, `ROAD_ELEVATION_STEP_COST`, `ROAD_RIVER_CROSS_COST`, `ROAD_EXISTING_DISCOUNT`), AoI/camera (`DEFAULT_AOI_CHUNK_RADIUS`, `DEFAULT_TERRAIN_AOI_CHUNK_RADIUS`, `CLIENT_VIEWPORT_HEIGHT_PX`, `CLIENT_CAMERA_ZOOM`, `CAMERA_VIEW_RADIUS`, `SIMULATION_CHUNK_RINGS`, `SPAWN_LOOKAHEAD_CHUNK_RINGS`), player stats (`BASE_MAX_HP`, `HP_PER_LEVEL`, `BASE_STAT`, `SKILL_POINTS_PER_LEVEL`, `MAX_ABILITY_TARGET_RANGE`, `SPELL_VISUAL_SECONDS`), inventory (`ABILITY_SLOT_START`, `ABILITY_SLOT_COUNT`, `ABILITY_POSITION_MULTIPLIERS`), enemy sim (`BEHAVIOR_TICK_DT`, `MAX_CATCHUP_SHOTS`, `BACKLOG_CAP`, the context-steering set `STEERING_DIRECTIONS`/`STEERING_PROBE_DISTANCE`/`SEPARATION_RADIUS`/`SEPARATION_ANGLE_OFFSET`/`ORBIT_RANGE_JITTER`/`MEANDER_MAX_TURN_RATE`/`MEANDER_NOISE_SEED_OFFSET`, plus pathing `ENEMY_PATH_RECOMPUTE_TICKS`/`ENEMY_PATH_WAYPOINT_RADIUS`), world timers (`LOOT_DROP_EXPIRY`, `CONSUMABLE_EFFECT_TICK_SECONDS`), elevation/heights (`ELEVATION_LEVELS`, `WORLD_HEIGHT_PER_LEVEL` — lockstep with client `ElevationMapComponent.WorldHeightPerLevel`, `ELEVATION_NOISE_SCALE`, `MAX_WALKABLE_UP_DELTA`/`MAX_WALKABLE_DOWN_DELTA`, `FALL_SAFE_DROP`, `FALL_DAMAGE_PER_LEVEL`, `FALL_SECONDS_PER_LEVEL`, `MAX_FALL_DISPLACEMENT`, `BULLET_ELEVATION_CLIMB`/`BULLET_ELEVATION_DROP` — the bullet pair is exported constants only, enforced client-side), server validation (`MOVE_SLACK`, `MIN_REPORT_DISPLACEMENT`, `HIT_RATE_SLACK`, `INCOMING_HIT_RATE_CAP`, `HIT_VALIDATION_RADIUS`, `INTERACT_RADIUS`, `GRAPPLE_ALLOWANCE_SECONDS`, `CHEST_TAKE_COOLDOWN_SECONDS`, `HIT_FLAG_MAX_AGE_SECONDS`)
- `lifecycle.rs` — `init`, `client_connected`, `client_disconnected`
- `admin.rs` — admin-only reducers (single global admin slot — `claim_admin` refuses if any `LoggedInPlayer`/`LoggedOutPlayer` already has `is_admin`; `release_admin` is the only way to free it): `claim_admin`/`release_admin`, `change_stats`, `upsert_texture_entry`/`upsert_enemy_template`, `upsert_movement_def`/`upsert_single_step_def`/`upsert_repeat_step_def`/`upsert_multi_step_def` (runtime enemy-authoring upserts — pass `def_id: 0` to insert, an existing id to update), `spawn_enemy`/`despawn_enemy` (both route through the archetype helpers, so no orphan or double-inserted behavior-tree rows; `despawn_enemy` also calls `status::methods::on_enemy_killed`, so admin despawns leave no status rows referencing the dead enemy), `add_chunks`/`clear_chunks`, `upsert_world_def`/`upsert_biome_def`/`upsert_biome_region_def`, `generate_world_proc`/`generate_world_manual` (admin-gated wrappers around `world::terrain::internal_generate_world_*`, the same calls `init` uses to build the starting world), `internal_add_chunks` (the non-gated chunk-creation helper `init` calls directly), `find_player_by_username` (matches username, full identity hex, or identity-hex prefix)
- `seeds.rs` — the `Seed` trait (+ impls for `MapConfig`/`TextureEntry`/`EnemyTemplate`/`WorldDef`/`BiomeDef`/`BiomeRegionDef`/`Enchantment`), `seed_default_textures`/`seed_default_enemies` (incl. `TestBoss`, the every-movement/every-pattern showcase), world-gen seeds (`seed_world_defs`, `seed_region_def`, `seed_layering_rules`, `seed_adjacency_rules`, `seed_decor_ground_rules`). Enemy seed-building helpers live in `enemy/defs/mod.rs`; boss seeds in `enemy/defs/gorgon_boss.rs`.
- `debug.rs` — `PlayerPositionDebug` table + its `PlayerPositionDebugSchedule` scheduled table + `tick_player_position_debug`, `toggle_debug`

---

# SpacetimeDB Rust Rules

## Core concepts

1. **Reducers are transactional** and return no data to callers — clients read data through tables/subscriptions.
2. **Reducers must be deterministic** — no filesystem, network, timers, or external RNG. Use `ctx.rng` and `ctx.timestamp`.
3. **`ctx.sender()` is the authenticated principal** — never trust an identity passed as an argument. It is a **method call**, not a field, in every context; `ctx.timestamp` *is* a plain field.
4. **Auto-increment IDs are not sequential** — gaps are normal; never use them for ordering.
5. **Server modules use the `spacetimedb` crate**, not `spacetimedb_sdk` (that is the Rust *client* SDK; this project's client is C#).

## Mistakes that actually happen

| Wrong | Right | Symptom |
|-------|-------|---------|
| `#[derive(SpacetimeType)]` on a `#[table]` struct | Drop it — the `#[table]` macro handles it | Conflicting derive macros |
| `#[spacetimedb::table]` + `#[primary_key]` C#-style | `#[table(accessor = user, public)]` on the struct | Wrong attribute path |
| `#[table(accessor = "my_table")]` | `#[table(accessor = my_table)]` | String literals not allowed |
| Missing `public` on a table clients subscribe to | Add the `public` flag | Clients can't subscribe |
| `ctx.db.player` / `ctx.db().player()` / `ctx.Db.player` | `ctx.db.player()` — `db` is a field, tables are methods | "no field `player` on type" |
| `ctx.db.player().find(id)` | `ctx.db.player().id().find(&id)` — always via an index | Must access via index |
| Missing `use spacetimedb::Table;` | Import the `Table` trait | "no method named `insert`" |
| `&mut ReducerContext` | `&ReducerContext` | Wrong context type |
| Reducer returning data (`-> Option<User>`) | `-> Result<(), String>` or nothing | — |
| Panicking on an expected error | Return `Err(...)` | WASM instance destroyed |
| Network/filesystem in a reducer | Use a procedure | Sandbox violation |
| `..Default::default()` in an update | Spread the existing row: `..user` | Silently nulls every other field |
| `ctx.sender` | `ctx.sender()` — a method, everywhere | "no field `sender`" |
| `scheduled(...)` written as `schedule(...)`, or spelled with `reducer =`/`column =` args | `#[table(accessor = tick_timer, scheduled(tick_enemy_spawn))]` — just the reducer name | "not a valid table type" |
| `ScheduleAt::At(t)` | `ScheduleAt::Time(t)` | No such variant |
| `let id: String = identity.to_hex();` | `identity.to_hex().to_string()` | `HexString<32>` type mismatch |
| `ctx.timestamp.to_duration_since_unix_epoch().as_micros()` | `.unwrap_or_default().as_micros()` — it returns a `Result` | Type mismatch |
| Moving a value into a struct then reading it | Compute the check before the move, or `clone()` | "value moved" |

## Canonical shapes

```rust
use spacetimedb::{table, reducer, Table, ReducerContext, Identity, Timestamp, ScheduleAt};

// Table — `public` means clients can subscribe; add `event` for append-only event tables
// (see BulletPatternEvent). Add explicit indexes for non-unique query columns.
#[table(accessor = task, public, index(name = by_owner, btree(columns = [owner_id])))]
pub struct Task {
    #[primary_key] #[auto_inc] pub id: u64,   // 0 is the auto-inc placeholder
    #[unique] pub slug: String,
    #[index(btree)] pub owner_id: Identity,
    pub title: String,
    pub created_at: Timestamp,
}

// Custom structs/enums used as FIELDS or PARAMETERS do derive SpacetimeType.
#[derive(SpacetimeType, Clone, PartialEq)]
pub enum Status { Idle, Busy(u32) }

#[reducer]
pub fn rename(ctx: &ReducerContext, id: u64, title: String) -> Result<(), String> {
    if title.is_empty() { return Err("title cannot be empty".into()); }
    let task = ctx.db.task().id().find(&id).ok_or("no such task")?;
    if task.owner_id != ctx.sender() { return Err("not yours".into()); }   // sender() — with parens
    ctx.db.task().id().update(Task { title, ..task });   // spread, never Default
    log::info!("renamed {id}");
    Ok(())
}

// Insert returns the ROW (read the assigned id off it), delete goes through an index.
let new_id = ctx.db.task().insert(Task { id: 0, /* … */ }).id;
ctx.db.task().id().delete(&new_id);

// Reads: .find() on primary key/unique (Option<Row>), .filter() on a btree index
// (iterator), .iter() for a full scan when there is no index.
for t in ctx.db.task().owner_id().filter(&ctx.sender()) { /* … */ }

// Scheduled tables: the job row is auto-deleted after the reducer runs; cancel by deleting it.
#[table(accessor = tick_timer, scheduled(tick))]
pub struct TickTimer {
    #[primary_key] #[auto_inc] scheduled_id: u64,
    scheduled_at: ScheduleAt,
}
```

`ctx` members: `ctx.sender()` (method), `ctx.timestamp`, `ctx.db`, `ctx.rng`. Lifecycle hooks are `#[reducer(init)]`, `#[reducer(client_connected)]`, `#[reducer(client_disconnected)]` — all three live in `main/lifecycle.rs`. Logging is `log::{trace,debug,info,warn,error}!`.

## Views — how the client subscribes

Every `views.rs` here uses 2.x **parameterized views**. The client never writes SQL: it subscribes to view accessors by name (see `TableSubscriber`'s `BaseTables`/`LobbyTables`/`GameTables`), so *adding a view is how you expose data*.

```rust
use spacetimedb::{ViewContext, Query, AnonymousViewContext};

// Per-caller view: ViewContext gives both ctx.db (imperative lookups) and
// ctx.from (the query builder). Note r#where — `where` is a Rust keyword.
#[spacetimedb::view(accessor = local_player_data, public)]
fn local_player_data(ctx: &ViewContext) -> impl Query<PlayerData> {
    let profile_id = ctx.db.logged_in_player().player_id().find(ctx.sender())
        .map(|p| p.profile_id)
        .unwrap_or(u64::MAX);                     // sentinel ⇒ matches nothing
    ctx.from.player_data().r#where(move |d| d.profile_id.eq(profile_id)).build()
}

// Unfiltered/global view: AnonymousViewContext, no per-caller state.
#[spacetimedb::view(accessor = enemy_templates, public)]
fn enemy_templates(ctx: &AnonymousViewContext) -> impl Query<EnemyTemplate> {
    ctx.from.enemy_template().build()
}
```

Two idioms every AOI view in this module follows, because the builder has no `IN` and a view must always return a query:

- **`IN` is an OR-chain.** Seed with the first index, then fold: `let mut cond = e.chunk_index.eq(idx[0]); for &i in &idx[1..] { cond = cond.or(e.chunk_index.eq(i)); }`
- **Empty result needs a sentinel query, not an early `return`** of nothing. Build a deliberately-unmatchable query up front (`r#where(|e| e.enemy_id.eq(u64::MAX))`) and return that when the caller has no chunk yet. Same trick as the `unwrap_or(u64::MAX)` above.

Predicates are closures over *column accessors* (`d.profile_id.eq(v)`, `.or(..)`), not plain Rust comparisons, and anything captured must be `move`d in.

**Procedures** (`#[procedure] fn f(ctx: &mut ProcedureContext, …) -> T`) are the escape hatch for HTTP/filesystem side effects and *can* return values, but need an explicit `ctx.try_with_tx(|tx| …)` for database access. Beta; nothing in this module uses them.

**Indexes:** primary-key and `#[unique]` columns are indexed automatically. **Index names must be unique across the whole module, not just per table.** Query code references indexes by name, so renaming or removing one means updating every call site.

## Commands

```bash
server/build.sh                 # regenerate C# bindings + publish `bullethell` to the project server (--delete-data)
spacetime logs bullethell       # module logs — first place to look
cargo check --manifest-path server/spacetimedb/Cargo.toml   # fast compile check, no publish
cargo check --manifest-path server/spacetimedb/Cargo.toml --tests  # typecheck the #[cfg(test)] helpers
```

Unit tests (`#[cfg(test)]` in `wrap.rs`/`aoi.rs`/`masks.rs`/`pathing.rs`/`hex_region.rs`/`explored.rs`/`enemy/methods.rs`) typecheck via `--tests` but **cannot be linked or run in place** — the wasm host symbols are unresolved for a native test binary (the `godot-hex-map` port ran its copies from a standalone crate); use `--tests` as the in-repo gate.

Server URL, publish semantics (wipes data, seeds re-run), and the `module_bindings/` regeneration rule live in the root `AGENTS.md`; `spacetime` commands run from `server/`.

Calling reducers / reading tables by hand for testing (`spacetime call`, `spacetime sql`, the admin gate): see the `playtest` skill (`../.kimi-code/skills/playtest/`, Part 2).

Debug order when something doesn't work: server running → module published → bindings regenerated → module logs → **is the client actually calling the reducer?**

## Editing behavior

- Make the smallest change necessary; don't touch unrelated files, configs, or dependencies.
- Don't invent SpacetimeDB APIs — use only what exists in the docs or already appears in this module.
- Don't add restrictions the prompt didn't ask for: if "users can do X", implement X for all users.
