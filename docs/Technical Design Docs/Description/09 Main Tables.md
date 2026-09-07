# 09 Main Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the archetype helpers lifecycle calls, [[03 Connection, Subscriptions & Views]] for why unsubscribed tables stay invisible. This module owns two tables (`PlayerPositionDebug` + its schedule); its larger role is the composition root — lifecycle hooks, the admin surface, seeds, tunables, and the shared clock — which write *other* modules' tables and are cited on those entries.

## The 30-second version

`main/` is the module with no domain of its own: `init` arms the two scheduled ticks and seeds the world (textures, animations, rules, enemies incl. the gorgon boss, items, enchantments, world defs, chunks, proc-generated "Earth"); `client_connected` / `client_disconnected` move identities between the lobby and world login rows; the admin reducers expose runtime world-authoring (enemy/template/step-def upserts, spawn/despawn, chunk and world-gen management, the single global admin slot); `global.rs` is the single home for every tunable (mirrored client-side by `ServerConstants.cs`); `time.rs` is the shared wall clock. Its only tables are the opt-in `PlayerPositionDebug` mirror and the schedule row that arms it.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| What runs at publish / first connect? | — | [[#lifecycle.rs]] — `init` seeds + arms ticks |
| How do I become admin? | — | [[#admin.rs]] — `claim_admin` (single global slot), `release_admin` frees it |
| How do I spawn / wipe enemies by hand? | — | [[#admin.rs]] — `spawn_enemy` / `despawn_enemy` / `despawn_all_enemies`, `toggle_enemy_spawning` |
| Where do tunable constants live? | — | [[#global.rs]] — grouped by subsystem, client mirrors named |
| What is the position-debug mirror? | `PlayerPositionDebug` | [[#PlayerPositionDebug]] — admin-armed 1 Hz mirror, `spacetime sql` only |
| Why doesn't DebugOverlay show positions? | `PlayerPositionDebug` | [[#PlayerPositionDebug]] — purely local perf HUD; the old pairing claim is stale |
| What time source do reducers share? | — | [[#time.rs]] — `seconds_since` |

## Flowcharts

- [[flowcharts/main-main.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/main_subfolder.canvas]]

> **Phase 2 proposal:** the `main` flow slug for this doc collides with the global `"main"` aggregate flow (both would compose to `flowcharts/main.canvas`). Rename this doc's flow before composing — suggested `main-admin` (output `flowcharts/main-main-admin.canvas`) or fold this doc into the global flow. The section above assumes `main-main` only as a placeholder.

## Table changes

```sync
![[00 Table Map#^table-player-position-debug{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-player-position-debug-schedule{seamless:true,title:false,marker:02.}]]
```

## PlayerPositionDebug

### Shape

One row per connected player in [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebug|debug.rs]]: `player_id` primary key plus a flattened copy of the live position row (`x`/`y`, `screen_rotation` joined from `PlayerRotation`, `chunk_index`) with the derived `hex_q`/`hex_r`. A debug mirror, not state — nothing in the game reads it.

### Changers

The scheduled [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]] (1 Hz while armed) upserts one row per `PlayerPosition` row and prunes rows for vanished players; admin [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] arms/disarms the schedule row and clears the table on disarm (see entries above).

### Readers

- ***Client*** — none. Server-only table (not `public`): inspect via `spacetime sql`. `DebugOverlay.cs` (declared inline in [[client/Scenes/game.tscn|game.tscn]]) is a purely local perf HUD — FPS, memory, enemy count, animation rows, plus the runtime 3dpixelart tweak panel — and never touches this table; the old "paired with server main/debug.rs" claim is stale (see [[#Known gaps / stubs]]).
- ***Server*** — none (debug mirror only).

## PlayerPositionDebugSchedule

### Shape

Scheduled-job row driving `tick_player_position_debug` at a 1 s interval. Its only changer is `toggle_debug` inserting/deleting it (entry 02 above).

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — the scheduler fires the debug tick per row.

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/main/pointers.md|main pointers]].

### admin.rs

The admin surface — every reducer here (except the read-only `find_player_by_username`, which matches username, full identity hex, or hex prefix and is reused by `item/reducers.rs`) is gated by `is_admin`, and the admin slot itself is a single global: [[server/spacetimedb/src/main/admin.rs#claim_admin|claim_admin]] refuses when ANY `LoggedInPlayer`/`LoggedOutPlayer` row already holds `is_admin`, and `release_admin` is the only way to free it. By area: `change_stats` (writes `PlayerStatAllocation`, then `recompute_stats`); `upsert_texture_entry` (+ `_fields` variant) and `upsert_enemy_template` (insert-or-update by key); the four runtime enemy-authoring upserts (`upsert_movement_def` / `upsert_single_step_def` / `upsert_repeat_step_def` / `upsert_multi_step_def` — `def_id: 0` inserts, existing id updates); `spawn_enemy` / `despawn_enemy` / `despawn_all_enemies` (all through the archetype helpers + `on_enemy_killed`, so no orphans and no dangling status rows — mass wipes grant no XP, unlike combat kills); `toggle_enemy_spawning` (arms/disarms the 2 s `EnemySpawnSchedule` row; behavior tick unaffected); `add_chunks` / `clear_chunks` over `internal_add_chunks` (the non-gated helper `init` calls directly — validates cols-divide-rows and positive radius, inserts one `BuildingTile` per hex plus the singleton `MapConfig` with derived lap vectors, or rewrites it); `upsert_world_def` / `upsert_biome_def` / `upsert_biome_region_def`; `generate_world_proc` / `generate_world_manual` (admin-gated wrappers over the same `internal_generate_world_*` calls `init` uses to build the starting world).

### debug.rs

The two debug tables plus their two reducers (see entries above). Consumes `is_admin` (toggle gate), `MapConfig` (outer radius), and the position/rotation rows (mirror source). No vault code outside the folder uses it — by design, since nothing reads the mirror.

### global.rs

The single home for every tunable constant, grouped by subsystem: world gen/map grid, elevation (`ELEVATION_LEVELS` = 8, walk deltas, fall damage/timing, the bullet `BULLET_ELEVATION_CLIMB`/`DROP` pair that is exported-constants-only — enforced client-side), AOI/camera (`DEFAULT_AOI_CHUNK_RADIUS` / terrain variant, `CAMERA_VIEW_RADIUS` derived from viewport × zoom, `SIMULATION_CHUNK_RINGS`), player stats/leveling (`BASE_MAX_HP` 100, `HP_PER_LEVEL` 5, `BASE_STAT` 10, `SKILL_POINTS_PER_LEVEL` 2, `MAX_ABILITY_TARGET_RANGE` 600, `SPELL_VISUAL_SECONDS` 0.5, `BASE_SPEED` 100), inventory (`ABILITY_SLOT_START` 32 / `COUNT` 6, position multipliers), enemy sim (tick dt, catch-up/backlog/budget caps, live/spawn caps, the full context-steering set), world timers (`LOOT_DROP_EXPIRY` 10 s, 1 Hz consumable tick), status thresholds (mark-pick radius, bleed purges, `NO_HIT_WITNESS_THRESHOLD` 2, bash hold, stagger decay, zone refresh, charge timing), and server validation (move slack, hit-rate caps, interact radius, grapple allowance, sweep TTLs, rotation epsilon, `MAX_LEVEL`). Each constant names its subsystem; the client mirrors its subset in `World/ServerConstants.cs` — both sides must change together.

### lifecycle.rs

The three lifecycle hooks: [[server/spacetimedb/src/main/lifecycle.rs#init|init]] arms `EnemyBehaviorSchedule` (100 ms) and `ConsumableEffectSchedule` (1 Hz), deliberately NOT `EnemySpawnSchedule` (biome spawning starts disarmed), then seeds everything (textures, animations, layering/adjacency/decor rules, default enemies + gorgon boss, items, enchantments, world defs), adds default chunks, and proc-generates `"Earth"`. [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] inserts a fresh `LoggedOutPlayer` (+ starter "Knight" profile) for unknown identities, and bounces already-in-world identities back to the lobby with the same status/zone purge as `leave_world` (recovery path — see the lobby-tables doc for the known gap it carries). `client_disconnected` runs the same purge and lobby move. Publishing wipes the DB every time, so all of this re-runs from scratch — schema changes are hard cuts, no migrations.

### mod.rs

Module declarations only.

### seeds.rs

The `Seed` trait (upsert-by-key `seed`, so reseeds are idempotent) with impls for `MapConfig`, `TextureEntry`, `AnimationEntry`, `EnemyTemplate`, `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `Enchantment` (`Item`'s impl lives in `item/seeds.rs`), plus the seed fns: `seed_default_textures` / `seed_default_animations`, `seed_default_enemies` (incl. `TestBoss`, the every-movement/every-pattern showcase), `seed_world_defs` (worlds + region defs via the private `seed_region_def`), and the rule seeders. `TestBoss` stays as the second content example alongside `gorgon_boss.rs`.

### time.rs

[[server/spacetimedb/src/main/time.rs#seconds_since|seconds_since]] — seconds between a stored timestamp and `ctx.timestamp`, saturating. Lives here (not in `enemy/methods.rs`, where it used to sit) because callers span player, chest, status, and enemy: a clock is not enemy-domain. Used by the hit-flag/attestation/loot sweeps, charge timing, bleed purges, and chest cooldowns.

## Known gaps / stubs

- **`PlayerPositionDebug` has no client reader at all** (server-only table, inspect via `spacetime sql`), and `client/AGENTS.md`'s claim that `DebugOverlay.cs` is "paired with server main/debug.rs" is stale: `DebugOverlay` is a purely local perf HUD (FPS/memory/enemy count) and never touches the table.

## Where to go next

[[10 Player Tables]] covers the lobby/login rows lifecycle hooks move around. For world generation inputs and outputs, read `12 World Tables`.
