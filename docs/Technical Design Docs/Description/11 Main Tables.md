# 11 Main

The composition root and operator toolbox: publish-time seeding, connect/disconnect lifecycle, the admin reducers, the background-agent switch, the position-debug mirror, and the shared constants/clock every module builds on. Main owns only three tables — everything else it does, it does *to other modules' tables*.

Maintainer refs (jump-off points, not authority — code wins): [[AGENTS.md|Description AGENTS.md]], [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]].

## Assumed knowledge

- [[00 Table Map]] — the per-table spine; every table section below transcludes its entry.
- [[01 Roadmap]] — reading order, conventions, the `main` vs `main-module` flow-name collision.
- [[03 Connection, Subscriptions & Views]] — reducers (server functions clients invoke by name; transactional, returning no data — clients learn only through tables), views (parameterized server queries clients subscribe to), subscription waves (`BaseTables`/`LobbyTables`/`GameTables`), and binders (one `TableBinderComponent` child per consumed table, re-exposing row events as editor-wireable signals). Unfamiliar terms below assume that doc.

## The 30-second version

Main owns three tables — `AgentConfig` (single-row master switch for background ticks), `PlayerPositionDebug` (per-player position mirror while debugging), and `PlayerPositionDebugSchedule` (the job row arming its 1 Hz tick) — and all three are server-only: no view, no wave, no binder, no pixels. The module's real weight is codeless-of-tables: `init` seeds the whole world in a fixed order, `client_connected`/`client_disconnected` move identities between the lobby and world rows, admin-gated reducers mutate every other module's tables through the archetype helpers, and `global.rs`/`time.rs` hold the constants and clock the rest of the server reads.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I become admin (and why only one)? | — (writes `LoggedInPlayer`/`LoggedOutPlayer` flags) | [[#admin.rs]] |
| How do I spawn / despawn enemies as an operator? | `Enemy` in `[[00 Table Map#^table-enemy\|^table-enemy]]` | [[#admin.rs]] |
| What runs at publish, in what order? | seeds + schedules | [[#Cross-table flows]] |
| Why did connecting bounce me back to the lobby? | `LoggedInPlayer` / `LoggedOutPlayer` | [[#Known gaps / stubs]] |
| How do I watch live player positions from the server? | [[#PlayerPositionDebug]] | [[#PlayerPositionDebug]] |
| Does the on-screen debug overlay read that table? | [[#PlayerPositionDebug]] (no — purely local HUD) | [[#PlayerPositionDebug]] |
| Where do background ticks get gated on/off? | [[#AgentConfig]] | [[#AgentConfig]] |
| What arms / disarms the debug tick? | [[#PlayerPositionDebugSchedule]] | [[#PlayerPositionDebugSchedule]] |
| Where do tuning constants live? | — | [[#global.rs]] |
| Where is "seconds since timestamp" computed? | — | [[#time.rs]] |
| Which lobby rows change on connect/disconnect? | `LoggedInPlayer` / `LoggedOutPlayer` in `[[00 Table Map#^table-logged-in-player\|^table-logged-in-player]]` | [[#lifecycle.rs]] |

## Flowcharts

- [[flowcharts/main-main-module.canvas]] — the composed Main-module flow (flow name `main-module`; the global aggregate keeps `main` — see [[01 Roadmap]] phase-0 note (a)).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/main_subfolder.canvas]] — module aggregate.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/lifecycle_codefile/lifecycle_codefile.canvas]] — init / connect / disconnect deep dive.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/admin_codefile/admin_codefile.canvas]] — admin reducer deep dive.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/debug_codefile/debug_codefile.canvas]] — debug tick / toggle deep dive.

## AgentConfig

```sync
![[00 Table Map#^table-agent-config{seamless:true,title:false,marker:01.}]]
```

### Shape

One row keyed by fixed id `0` carrying a single `agents_enabled` bool — the master switch for every background agent pass. Fixed id (not auto-inc) *because* the read path is one PK lookup in [[server/spacetimedb/src/main/agents.rs#should_run|should_run]]. Server-only (no `public`), so no client can subscribe no matter the wave.

### Changers

Insert in [[server/spacetimedb/src/main/agents.rs#init_agents|init_agents]] (called from `init` after seeds, idempotent — skips the insert when id `0` already exists); insert-or-update in admin-only [[server/spacetimedb/src/main/agents.rs#set_agents_enabled|set_agents_enabled]]; re-asserted by admin-only [[server/spacetimedb/src/main/agents.rs#update_agent_timers|update_agent_timers]] (periods are compile-time consts, so it just re-runs `init_agents` and logs). Never deleted.

### Readers

- ***Client*** — None. Server-only table: never subscribed, no visible end.
- ***Server*** — `should_run` gates the three per-concern passes `tick_regen` / `tick_zones` / `tick_cleanup`, and it fails closed (missing row reads as disabled). The passes themselves are dispatch points — real regen/zone math lives in `status::methods::tick`, trade/chat/upkeep/strike sweeps in their own modules — so flipping the switch pauses background sim without deleting any timer row.

## PlayerPositionDebug

```sync
![[00 Table Map#^table-player-position-debug{seamless:true,title:false,marker:02.}]]
```

### Shape

One mirror row per live `PlayerPosition`, keyed by `player_id` (not `profile_id` — one row per connection even if profiles multiply), carrying x/y plus `screen_rotation` (joined from the rotation row, defaulting to `0.0`) and the `chunk_index`/`hex_q`/`hex_r` the tick derives via `world_to_hex`. Exists only while debug mode is armed; gameplay never writes it. Server-only (no `public`).

### Changers

Only [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug|tick_player_position_debug]] writes: per position row it upserts the mirror (update when the `player_id` row exists, insert otherwise), then deletes mirror rows whose player vanished — and `toggle_debug`'s disable path deletes *all* rows when disarming. Never written by gameplay, never updated by any other reducer.

### Readers

- ***Client*** — None, and this is the point most worth belaboring: there is no view, no wave, no binder — inspect via `spacetime sql`. The client's [[client/Scripts/Game/DebugOverlay.cs#DebugOverlay|DebugOverlay]] (declared inline in [[client/Scenes/game.tscn|game.tscn]], not instanced from a scene) is a purely local perf HUD — FPS, frame time, memory, object/node/draw-call counts, `GameManager.EnemyCount`, connection state, local position/hex/chunk/rotation, camera yaw/pitch — and never touches this table (its only table read is a one-shot `MapConfig.Iter()` for the chunk label). `client/AGENTS.md`'s "paired with server `main/debug.rs`" line is stale (see [[#Known gaps / stubs]]).
- ***Server*** — The tick's own reads only: full `player_position` scan, per-row `player_rotation` lookup, `MapConfig::load` for the hex radius. No reducer guards on it.

## PlayerPositionDebugSchedule

```sync
![[00 Table Map#^table-player-position-debug-schedule{seamless:true,title:false,marker:03.}]]
```

### Shape

A scheduled-job row (`scheduled_id` auto-inc PK + `scheduled_at`) whose presence arms the 1-second `tick_player_position_debug`. Standard schedule-table shape: the row *is* the timer. Server-only (not `public`).

### Changers

Arm/disarm only in admin-only [[server/spacetimedb/src/main/debug.rs#toggle_debug|toggle_debug]] (gated by [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]]): enable inserts the interval row, disable deletes the schedule rows *and* all `PlayerPositionDebug` rows. Interval rows persist until deleted — whoever arms it is its only changer; never updated.

### Readers

- ***Client*** — None. Server-only schedule table: never subscribed, no visible end.
- ***Server*** — The scheduler fires the tick; nothing else reads the row (the enable check is `iter().next().is_some()` inside `toggle_debug` itself).

## Files — pointer deep dive

In this folder's `[[server/spacetimedb/src/main/pointers.md|pointers]]` order. The pointers say *what* each file uses; this section says *how*.

### admin.rs

Every reducer here is admin-gated via [[server/spacetimedb/src/player/methods.rs#is_admin|is_admin]] (the single global admin slot — see below) and every one mutates *other modules'* tables; admin owns no table of its own. No non-generated client code calls these reducers today (only the generated bindings under `client/Scripts/module_bindings/`; the `DebugOverlay` has no admin buttons) — they are invoked from the `spacetime` CLI.

- Admin slot: [[server/spacetimedb/src/main/admin.rs#claim_admin|claim_admin]] refuses when *any* `LoggedInPlayer`/`LoggedOutPlayer` row already has `is_admin` (it scans both tables because an admin may sit in either), and re-claiming while already admin returns `Ok` idempotently; [[server/spacetimedb/src/main/admin.rs#release_admin|release_admin]] is the only way to free the slot. Both preserve the row via `..p` spread updates.
- Player targeting: [[server/spacetimedb/src/main/admin.rs#find_player_by_username|find_player_by_username]] resolves a username, full identity hex, or identity-hex prefix across both login tables (case-insensitive); [[server/spacetimedb/src/main/admin.rs#change_stats|change_stats]] writes the *allocation input* (`PlayerStatAllocation`, negatives clamped to zero) rather than the `PlayerStats` output — because `recompute_stats` rebuilds output from allocation + gear on every inventory touch and would wipe a direct write — then calls `recompute_stats` itself.
- Enemy authoring: [[server/spacetimedb/src/main/admin.rs#spawn_enemy|spawn_enemy]] wraps the position onto the torus, stamps `chunk_index` via `spiral_chunk_index`, and routes through `spawn_enemy_archetype` so no behavior-tree row is orphaned; [[server/spacetimedb/src/main/admin.rs#despawn_enemy|despawn_enemy]] and [[server/spacetimedb/src/main/admin.rs#despawn_all_enemies|despawn_all_enemies]] route through `despawn_enemy_archetype` *plus* `on_enemy_killed`, so admin despawns leave no status rows referencing the dead enemy (despawn-all grants no XP, unlike the combat kill path). The def upserts ([[server/spacetimedb/src/main/admin.rs#upsert_movement_def|upsert_movement_def]], [[server/spacetimedb/src/main/admin.rs#upsert_single_step_def|upsert_single_step_def]], [[server/spacetimedb/src/main/admin.rs#upsert_repeat_step_def|upsert_repeat_step_def]], [[server/spacetimedb/src/main/admin.rs#upsert_multi_step_def|upsert_multi_step_def]], [[server/spacetimedb/src/main/admin.rs#upsert_enemy_template|upsert_enemy_template]]) take `def_id: 0` as insert and an existing id as update. [[server/spacetimedb/src/main/admin.rs#toggle_enemy_spawning|toggle_enemy_spawning]] arms/disarms the `EnemySpawnSchedule` job row (spawning starts disarmed — `init` never inserts it); admin spawns and the behavior tick are unaffected.
- World authoring: [[server/spacetimedb/src/main/admin.rs#internal_add_chunks|internal_add_chunks]] (also called directly by `init`, hence non-gated and non-reducer) validates grid shape, stamps every `BuildingTile` plus the `MapConfig` row; [[server/spacetimedb/src/main/admin.rs#add_chunks|add_chunks]] is its gated wrapper; [[server/spacetimedb/src/main/admin.rs#clear_chunks|clear_chunks]] wipes tiles *and* the building records that would otherwise dangle (footprints, states, sites). [[server/spacetimedb/src/main/admin.rs#generate_world_proc|generate_world_proc]] / [[server/spacetimedb/src/main/admin.rs#generate_world_manual|generate_world_manual]] are gated wrappers over the same `internal_generate_world_*` calls `init` uses; the `upsert_world_def` / `upsert_biome_def` / `upsert_biome_region_def` trio edits the world-gen inputs those calls read.

### agents.rs

The BitCraft-style agent framework's master switch plus per-concern dispatch. Defines the `AgentConfig` table; `init_agents` is called from `init` after seeds and gap-fills the `ConsumableEffectSchedule` row (the lifecycle `init` inserts it first, but a deleted timer row must not silently kill the 1 Hz tick). `should_run` fails closed. `tick_regen` / `tick_zones` / `tick_cleanup` each early-return on `should_run` and then delegate — regen/zones into `status::methods`, cleanup into trade timeouts, chat retention, claims upkeep, and the anticheat strike sweep — so future work can hang each concern off its own schedule row (periods already live apart in `global.rs`) without touching call sites. `set_agents_enabled` (upsert) and `update_agent_timers` (re-assert + log) are the admin controls.

### debug.rs

Defines `PlayerPositionDebug`, its `PlayerPositionDebugSchedule` job table, the `tick_player_position_debug` mirror sweep, and admin-only `toggle_debug`. Call direction is one-way: the tick reads `player_position` / `player_rotation` / `MapConfig` and writes only the mirror; nothing reads the mirror back server-side and no client subscribes. Toggle-off purges both the schedule rows and every mirror row, so disabling leaves no residue.

### global.rs

The single home for every tunable constant, grouped by subsystem (world gen, elevation, AOI/camera, stats, inventory slot layout, enemy sim, timers, status, validation, agents, strikes, channels, survival, trade, claims, chat). Other modules read these at reducer time; the client mirrors a subset in `World/ServerConstants.cs` (ability multipliers, charge factors, elevation/fall/bullet-band constants) — both copies must change together. Notable load-bearing values: `BASE_SPEED` (movement clamp root), `MOVE_SLACK` (lag-tolerant clamp, not reject), `INTERACT_RADIUS` (pickup/chest range), `LOOT_DROP_EXPIRY` (10 s, enforced by the 1 Hz tick), `AGENTS_ENABLED_DEFAULT` (seeded switch state).

### lifecycle.rs

The three lifecycle hooks — the only reducers here, and the only writers of lobby rows in this module (direct row writes; the player helpers it calls are the runtime-row purges, not the lobby writes).

- [[server/spacetimedb/src/main/lifecycle.rs#init|init]] arms `EnemyBehaviorSchedule` (100 ms) and `ConsumableEffectSchedule` (1 Hz), deliberately *not* `EnemySpawnSchedule` (biome spawning starts disarmed for debugging), then runs the full seed chain, `internal_add_chunks`, `internal_generate_world_proc("Earth", 0)`, and `init_agents` — full order in [[#Cross-table flows]].
- [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] inserts a `LoggedOutPlayer` row plus a scaffold `Knight` `PlayerProfile` for first-time identities and ignores known lobby identities; the already-in-world branch mishandles (see [[#Known gaps / stubs]]) — it purges statuses/zones via the player helpers, deletes the `LoggedInPlayer` row, and reinserts a `LoggedOutPlayer` row instead of rejecting.
- [[server/spacetimedb/src/main/lifecycle.rs#client_disconnected|client_disconnected]] purges the profile's statuses and zones (no status may tick a logged-out player; a caster's zones die with them), moves the `LoggedInPlayer` row back to `LoggedOutPlayer`, and preserves username/admin flags across the move.

### mod.rs

Module declarations only (`lifecycle`, `admin`, `agents`, `debug`, `seeds`, `global`, `time`). No logic.

### seeds.rs

The `Seed` trait (upsert-by-natural-key: update when the key row exists, insert otherwise — so republishes converge instead of duplicating) plus its impls for `MapConfig`, `TextureEntry`, `AnimationEntry`, `EnemyTemplate`, `WorldDef`, `BiomeDef`, `BiomeRegionDef`, and `Enchantment` (`Item`'s impl lives in the item module's seeds; enemy row-building helpers live in `enemy/defs/`). The `seed_*` functions are the content: `seed_default_textures` / `seed_default_animations` (the 86-clip UAL library, `animation_id` namespaced per library so identically named clips never collide), `seed_layering_rules` / `seed_adjacency_rules` / `seed_decor_ground_rules` (clear-and-reinsert, since rule pairs have no natural key to upsert against), `seed_default_enemies` (`Enemy`, `Archer`, and the `TestBoss` every-pattern showcase), and `seed_world_defs` (regions, three biomes, the `Earth` world). All run from `init`, in the order listed in [[#Cross-table flows]].

### time.rs

One function: [[server/spacetimedb/src/main/time.rs#seconds_since|seconds_since]] (wall-clock seconds between a stored `Timestamp` and `ctx.timestamp`, via unix-epoch micros). Lives here rather than in any domain module *because* its callers span enemy, player/status, and chest code — a clock is not enemy-domain. Pure helper: reads no table, writes nothing.

## Cross-table flows

- **Init seed order** (all inside [[server/spacetimedb/src/main/lifecycle.rs#init|init]]): behavior + status schedules → `seed_default_textures` → `seed_default_animations` → `seed_layering_rules` → `seed_adjacency_rules` → `seed_decor_ground_rules` → `seed_default_enemies` → gorgon boss seed → `seed_world_items` → `seed_world_enchantments` → `seed_world_defs` → building seeds → `internal_add_chunks` → `internal_generate_world_proc("Earth", 0)` → `init_agents`. Order matters only where later steps read earlier rows (world-gen reads the defs; `init_agents` gap-fills the schedule `init` just inserted).
- **Publish wipes data.** [[server/build.sh|build.sh]] publishes with `--delete-data`: every publish is a hard cut — no migrations, no dual-write — and `init` re-runs the whole chain above from scratch, so schema changes land by editing seeds, not by migrating rows.

## Known gaps / stubs

- `client_connected` mishandles the already-in-world case: it purges the player's statuses/zones, deletes the `LoggedInPlayer` row, and inserts a `LoggedOutPlayer` row (bouncing the player back to the lobby) instead of rejecting — flagged as a bug only in a `log::error!` message, not a comment.
- `PlayerPositionDebug` has no client reader at all (server-only table; inspect via `spacetime sql`). `client/AGENTS.md`'s claim that `DebugOverlay.cs` is "paired with server `main/debug.rs`" is stale: the overlay is a purely local perf HUD (FPS / memory / enemy count) and never touches the table.
- No non-generated client code calls the admin reducers today — `DebugOverlay` carries no admin buttons, so operator actions go through the `spacetime` CLI against the generated bindings. (Not a bug; recorded so nobody hunts for the missing UI.)

## Where to go next

Read [[03 Connection, Subscriptions & Views]] for the subscription machinery these tables deliberately bypass, then the Player doc for the lobby/profile rows lifecycle touches and the World and Enemy docs for the tables the admin and seed paths write.

**Phase 2 proposal:** delete `flowcharts/main-admin.canvas` if a compose leaves it behind — the module flow is `main-module` now (its canvas is `[[flowcharts/main-main-module.canvas]]`), and a stale `main-<name>.canvas` is never auto-cleaned. (No such file exists on disk today; all composed mains are pending the next "Regenerate all flowcharts" run.)
