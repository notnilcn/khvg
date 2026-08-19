# 12 Admin & Debug

## Assumed knowledge

- [[03 Boot & Connection]] — how the module is published, why every publish wipes the database, and what `init` seeds on boot.
- [[04 Lobby & Profiles]] — the `LoggedInPlayer`/`LoggedOutPlayer` split and what a profile is (admin reducers target players by *username + profile name*).
- [[07 Terrain & World Streaming]] — chunks, hexes, `MapConfig`, and what world generation produces (admin reducers re-run or replace it).
- [[08 Enemies & AI]] — enemy templates, phases, and the archetype helpers (admin spawn/despawn routes through them).
- [[10 Inventory, Items & Enchantments]] — the item/enchantment catalog and inventory slots (admin item tooling writes both).

## The 30-second version

Admin is a **single global slot**: exactly one player identity at a time may hold the `is_admin` flag, claimed and released through two reducers, and every privileged reducer re-checks that flag before acting. The privileged surface is 18 reducers in `main/admin.rs` (stat edits, catalog upserts, enemy spawn/despawn, chunk and world management), four more in `item/reducers.rs` (give/remove items, item/enchantment upserts), and one opt-in debug feed in `main/debug.rs` that mirrors player positions into a server-side table once a second. None of it has client UI — everything is driven from the `spacetime` CLI. The client side of "debug" is just the `DebugOverlay` HUD (FPS/memory/enemy count) declared inline in `game.tscn` and toggled with a key. Operationally, the whole loop is publish-driven: `build.sh` regenerates bindings and republishes with `--delete-data`, so the seed functions double as the content pipeline that admin upserts modify at runtime.

## Flowcharts

- [[flowcharts/main-admin.canvas]] — this system's composed flow (composed later from `flows.json`; link intentionally unresolved until then).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/admin_codefile/admin_codefile.canvas]] — the 18 admin reducers + `find_player_by_username`, one symbol node each.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/debug_codefile/debug_codefile.canvas]] — the position-debug feed: `toggle_debug`, `tick_player_position_debug`, and the two tables.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Game_subfolder/DebugOverlay_codefile/DebugOverlay_codefile.canvas]] — the client debug HUD.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^admin-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-7{seamless:true,title:false,marker:07.}]]
```

## Main body

### The admin model: one flag, one holder, one gate

```sync
![[00 End-to-End Timeline Flowchart#^admin-1{seamless:true,title:false,marker:01.}]]
```

The flag itself is a plain `is_admin: bool` column on both login-state tables in [[server/spacetimedb/src/player/tables.rs#is_admin#1|player/tables.rs]] — remember from [[04 Lobby & Profiles]] that a connected client always has exactly one of a `LoggedInPlayer` (in the world) or `LoggedOutPlayer` (in the lobby) row, keyed by its SpacetimeDB `Identity`. Admin state therefore survives moving between lobby and world, because [[server/spacetimedb/src/main/admin.rs#claim_admin#1|claim_admin]] sets the flag on whichever row the caller currently has, and [[server/spacetimedb/src/player/reducers.rs#join_world#1|join_world]]/[[server/spacetimedb/src/player/reducers.rs#leave_world#1|leave_world]] carry it across: each deletes the old login-state row and inserts the other with `is_admin` copied verbatim from the row it replaces.

The single-slot rule is enforced inside `claim_admin`, not by a table constraint: before granting, it scans **both** tables (`ctx.db.logged_in_player().iter().any(|p| p.is_admin) || ctx.db.logged_out_player().iter().any(...)`) and refuses with "An admin already exists" if anyone holds the flag. Two subtleties follow. First, calling `claim_admin` when you already *are* the admin is a no-op success (the early `if p.is_admin { return Ok(()); }`), so the CLI command is idempotent. Second, because the scan covers logged-out rows too, an admin who disconnects still occupies the slot — a second operator can't sneak in while the first is offline; [[server/spacetimedb/src/main/admin.rs#release_admin#1|release_admin]] is the only exit, and it errors unless the caller actually holds the flag. There is no timeout or revoke-by-identity path; if the admin's credentials are lost, the slot is stuck until the next publish wipes the database (and every publish does — see the ops section below).

Every privileged reducer then gates on the same helper: [[server/spacetimedb/src/player/methods.rs#is_admin#1|is_admin]] looks up the caller's `LoggedInPlayer` row, falls back to the `LoggedOutPlayer` row, and defaults to `false` when neither exists. That means the gate works from the lobby *and* from inside the world, and a caller with no player row at all is never admin. The one place admin status is more than an on/off gate is [[server/spacetimedb/src/world/reducers.rs#remove_building#1|remove_building]]: it checks `tile.owner_id != Some(ctx.sender()) && !is_admin(ctx)`, so the admin can demolish any player's building — the only admin power reachable through a normal gameplay reducer.

### The 18 reducers of `main/admin.rs`

```sync
![[00 End-to-End Timeline Flowchart#^admin-2{seamless:true,title:false,marker:02.}]]
```

They cluster into five groups. All of them open with the same `if !is_admin(ctx) { return Err("Admin only.".to_string()); }` line (except `claim_admin`/`release_admin`, which manage the flag itself), and all of them return `Result<(), String>` — reducers return nothing to the caller, so an `Err` string is the entire feedback channel and shows up in `spacetime logs`.

**Player targeting.** [[server/spacetimedb/src/main/admin.rs#change_stats#1|change_stats]] overwrites a profile's six core stats (`strength`…`artisan`) in the `PlayerStats` table. Admin commands address players by name, not by raw identity, so the module needs a resolver: [[server/spacetimedb/src/main/admin.rs#find_player_by_username#1|find_player_by_username]] chains both login tables into one iterator and accepts three forms — an exact (case-insensitive) username, a full identity hex string, or an unambiguous hex *prefix* (`id_hex.starts_with(hex)`), with an optional `0x` stripped first. It returns the first match, so a prefix that collides resolves arbitrarily — fine for a two-person dev server, worth knowing before trusting it anywhere bigger. `change_stats` then resolves the profile with [[server/spacetimedb/src/player/methods.rs#find_profile_by_name#1|find_profile_by_name]] and updates the row in place, preserving the fields it wasn't passed via the `..stats` spread.

**Catalog upserts.** `upsert_texture_entry`, `upsert_enemy_template`, `upsert_world_def`, `upsert_biome_def`, and `upsert_biome_region_def` all share one shape: take a whole row struct as the reducer argument, update it if the primary key exists, insert otherwise. SpacetimeDB reducer arguments are just deserialized rows, so "authoring content" is `spacetime call bullethell upsert_biome_def '{...}'`. These are the runtime counterparts of the seed functions — they write the same def tables `init` populates on publish (see the seeding section below).

**Runtime enemy authoring.** The four step/movement upserts — `upsert_movement_def`, `upsert_single_step_def`, `upsert_repeat_step_def`, `upsert_multi_step_def` — write the def tables that enemy attack sequences are built from (the phase/sequence model is [[08 Enemies & AI]]'s topic). Their `def_id` columns are auto-increment, which forces a convention the code states in a comment: pass `def_id: 0` to insert (the reducer rewrites the struct with `def_id: 0` so the database assigns the real id, and you discover it afterwards via `spacetime sql`), or pass an existing id to update. This is how you prototype a new bullet pattern without a republish: upsert the step defs, upsert a template referencing them, then `spawn_enemy`.

**Enemy spawn/despawn.** [[server/spacetimedb/src/main/admin.rs#spawn_enemy#1|spawn_enemy]] looks up the template, loads the world geometry with [[server/spacetimedb/src/world/tables.rs#load#1|MapConfig::load]] (the `(1, 1)` fallback arguments only matter if no `MapConfig` row exists yet), wraps the requested coordinates onto the torus with [[server/spacetimedb/src/world/wrap.rs#wrap_world_pos#1|wrap_world_pos]], converts to a chunk with [[server/spacetimedb/src/world/hex.rs#world_to_chunk#1|world_to_chunk]] + [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]], and hands off to [[server/spacetimedb/src/enemy/methods.rs#spawn_enemy_archetype#1|spawn_enemy_archetype]]. `despawn_enemy` similarly routes through [[server/spacetimedb/src/enemy/methods.rs#despawn_enemy_archetype#1|despawn_enemy_archetype]]. Routing through the archetype helpers is the invariant that matters: an enemy is a *bundle* of rows across several tables (instance, behavior, schedule — the server-side mirror of the client component model), and only the helpers insert/delete the whole bundle. Admin spawns therefore behave exactly like natural spawns, and admin despawns can't orphan behavior rows — which is also why `immortal` and `is_elite` are explicit arguments: they're archetype fields the natural spawner computes, so the admin path has to supply them by hand.

**World management.** `add_chunks` is the admin-gated wrapper around [[server/spacetimedb/src/main/admin.rs#internal_add_chunks#1|internal_add_chunks]], the same function `init` calls at publish time: it validates the geometry (`chunk_cols` must divide `chunk_rows`, `hex_outer_radius` must be positive), inserts every `BuildingTile` row for the chunk grid (hex coordinates from `hex_area`/`hex_shift`/`inv_hexmod`/`chunk_center_hex` in [[server/spacetimedb/src/world/hex.rs#hex_area#1|world/hex.rs]]), computes the torus lap vectors, and upserts the single `MapConfig` row. `clear_chunks` deletes every `BuildingTile` row. `generate_world_proc`/`generate_world_manual` are the gated wrappers around [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc#1|internal_generate_world_proc]] and `internal_generate_world_manual` — the very calls `init` makes in boot — so an admin can regenerate the terrain of a live server (with a new seed, or a hand-authored biome layout) without republishing. The generation internals are [[07 Terrain & World Streaming]]'s subject; the point here is that `main/admin.rs` owns the *authorization boundary* while `world/terrain` owns the mechanism.

### Item administration

```sync
![[00 End-to-End Timeline Flowchart#^admin-3{seamless:true,title:false,marker:03.}]]
```

The four item reducers live in `item/reducers.rs` rather than `main/admin.rs` because they reuse the item module's slot machinery. [[server/spacetimedb/src/item/reducers.rs#give_item#1|give_item]] resolves the target the same way `change_stats` does (username → identity via `find_player_by_username`, then profile via `find_profile_by_name`), finds the first empty `General`-role slot, and writes the item id into it — it errors with "No empty general slots available" rather than dropping or stacking. [[server/spacetimedb/src/item/reducers.rs#remove_item#1|remove_item]] removes *every* slot holding that item id, and it's span-aware: ability items occupy `slot_cost` consecutive slots (the extra slots are marked via `occupied_by`), so removing one calls the slot helper `clear_span` to free the whole run instead of leaving phantom occupied slots behind. [[server/spacetimedb/src/item/reducers.rs#upsert_item#1|upsert_item]] and [[server/spacetimedb/src/item/reducers.rs#upsert_enchantment#1|upsert_enchantment]] are the same key-based upsert shape as the world-def upserts. Because the client's `CatalogComponent` caches the catalog tables from its base subscription, any of these edits reaches every connected client on the next table event — no restart, no republish.

### Seeding as the admin content pipeline

The seed functions are not a separate system from admin tooling — they are its baseline. [[server/spacetimedb/src/main/lifecycle.rs#init#1|init]] calls, in a fixed order: the texture catalog ([[server/spacetimedb/src/main/seeds.rs#seed_default_textures#1|seed_default_textures]]), the terrain rule sets ([[server/spacetimedb/src/main/seeds.rs#seed_layering_rules#1|seed_layering_rules]], `seed_adjacency_rules`, `seed_decor_ground_rules` — each clear-and-reinsert so republishes can't leave stale rules), the enemy roster ([[server/spacetimedb/src/main/seeds.rs#seed_default_enemies#1|seed_default_enemies]] plus the `seed_test_boss_p2`…`p6` variants, built with the `make_phase`/`make_sequence`/`seq_single`/`seq_repeat`/`seq_multi` helper DSL that inserts the movement/step def rows and returns the ids), the item and enchantment catalogs ([[server/spacetimedb/src/item/seeds.rs#seed_world_items#1|seed_world_items]] / [[server/spacetimedb/src/item/seeds.rs#seed_world_enchantments#1|seed_world_enchantments]]), the world defs ([[server/spacetimedb/src/main/seeds.rs#seed_world_defs#1|seed_world_defs]]), and finally `internal_add_chunks` + `internal_generate_world_proc` — the two `internal_*` functions the admin world reducers wrap.

Every seed goes through the same idempotent pattern as the admin upserts, factored into a tiny [[server/spacetimedb/src/main/seeds.rs#Seed#1|Seed]] trait (`fn seed(ctx, data)`) with one impl per table: find by primary key, update if present, insert otherwise. That's what makes the publish loop safe to repeat — and since `build.sh` publishes with `--delete-data`, "present" only ever means "seeded twice in one boot," which the layering/adjacency rule seeds additionally guard against by clearing first. The practical workflow: edit a seed file for permanent content changes (survive republish), use the admin upsert reducers for live experiments (lost on next publish). Biomes-as-gameplay, guild territory, and base-building beyond `place_building`/`remove_building` are aspirational design-doc systems and deliberately out of scope here — the seeds' biome/world defs are terrain paint and spawn pools, nothing more.

### The client debug HUD

```sync
![[00 End-to-End Timeline Flowchart#^admin-4{seamless:true,title:false,marker:04.}]]
```

Wiring detail the timeline step compresses: the node is declared inline in [[client/Scenes/game.tscn|game.tscn]] as a `CanvasLayer` named `DebugOverlay` with `layer = 100` (so it draws above every gameplay layer) and `visible = false`, with a single child `Label` that has a 4px black outline so the text reads over any background. The script, [[client/Scripts/Game/DebugOverlay.cs#_Ready#1|DebugOverlay]], grabs that label in `_Ready`. Two Godot concepts carry the whole thing: `_Input` runs on every unhandled input event, and the check `Input.IsActionJustPressed("toggle_debug_overlay")` ([[client/Scripts/Game/DebugOverlay.cs#_Input#1|_Input]]) refers to an input *action* — a named key binding declared in `client/project.godot`, here bound to the physical P key — so `Visible = !Visible` flips the HUD on P. `_Process` runs every frame; the early `if (!Visible) return;` plus a 0.25 s accumulator means the expensive part — reading six counters from Godot's `Performance` singleton and formatting the string — happens four times a second and only while shown ([[client/Scripts/Game/DebugOverlay.cs#_Process#1|_Process]]). The enemy count comes from the `GameManager` facade ([[client/Scripts/Game/GameManager.cs#EnemyCount#1|GameManager.EnemyCount]]), which just forwards `EntitySpawnerComponent`'s live dictionary size ([[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#EnemyCount#1|EnemyCount]]) — so the HUD reads client-side mirrors only and never touches the network. Note this overlay is *not* a component and has no `TableBinderComponent`; it's a plain script node, the one debug tool that works with no server connection at all.

### The server-side debug feed

```sync
![[00 End-to-End Timeline Flowchart#^admin-5{seamless:true,title:false,marker:05.}]]
```

Mechanically this is the same scheduled-table pattern `init` uses for enemy ticks (boot-1): a row in [[server/spacetimedb/src/main/debug.rs#PlayerPositionDebugSchedule#1|PlayerPositionDebugSchedule]] declares a 1-second interval and names the reducer it re-fires. [[server/spacetimedb/src/main/debug.rs#toggle_debug#1|toggle_debug]] is the on/off switch — if a schedule row exists it deletes the row *and* wipes the whole `PlayerPositionDebug` table (so "off" also means "clean"), otherwise it inserts one. While scheduled, [[server/spacetimedb/src/main/debug.rs#tick_player_position_debug#1|tick_player_position_debug]] iterates every `PlayerPosition` row, enriches it with the player's `PlayerRotation` (defaulting to 0.0 when absent) and the hex coordinates from [[server/spacetimedb/src/world/hex.rs#world_to_hex#1|world_to_hex]], upserts the mirror row keyed by `player_id`, and finally deletes mirror rows whose positions disappeared — so the table always mirrors *current* positions, including the logged-out "ghost" position rows that [[13 Disconnect & Teardown]] documents. The intended consumer is a human running `spacetime sql 'SELECT * FROM player_position_debug'` or watching `spacetime logs`; no client subscribes to it.

### Scattered debug prints

```sync
![[00 End-to-End Timeline Flowchart#^admin-6{seamless:true,title:false,marker:06.}]]
```

### The operational loop

```sync
![[00 End-to-End Timeline Flowchart#^admin-7{seamless:true,title:false,marker:07.}]]
```

Concretely, [[server/build.sh|server/build.sh]] is two commands: `spacetime generate --lang csharp --out-dir ../client/Scripts/module_bindings --module-path ./spacetimedb -y` (regenerates the client bindings so the C# reducer/table stubs match the module — never hand-edit that output) and `spacetime publish bullethell --delete-data -y`. The `--delete-data` flag is why there are no migrations anywhere in this codebase: schema changes are hard cuts, `init` rebuilds schedules and seeds from scratch, and any runtime state — including admin upserts and the admin flag itself — is gone. The typical admin session is therefore: publish → `spacetime call bullethell claim_admin` from a connected client identity → experiment via the upsert/spawn reducers → `spacetime logs bullethell` to read the `log::info!`/`log::error!` trail every admin reducer leaves → codify whatever worked into `main/seeds.rs` or `item/seeds.rs` so the next publish keeps it.

## Known gaps / stubs

- **No client UI for any admin reducer.** Nothing in `client/Scripts` calls `claim_admin`, `release_admin`, `change_stats`, the upserts, `spawn_enemy`, `give_item`, or `toggle_debug` (grep finds callers only in the generated `module_bindings`). Every admin action is a `spacetime call` CLI command. If an in-game admin panel is ever built, `claim_admin` and the item/catalog upserts are the entry points.
- **`toggle_debug`/`PlayerPositionDebug` have no consumer.** The position-debug feed is server-side only; no client subscribes to the table and no tooling renders it beyond `spacetime sql`.
- **`Scenes/UI/debug_overlay.tscn` is an unreferenced duplicate.** The live `DebugOverlay` is declared inline in `game.tscn`; the standalone scene is one of seven stale component scenes kept only as drift hazards — the full list and its consequences live in [[02 The Component Framework]]. Never wire new code against the duplicate.
- **The admin slot has no recovery path.** If the admin identity's credentials are lost while it holds the flag (even logged out), no reducer can free the slot short of a `--delete-data` republish.
- **`find_player_by_username` hex prefixes can collide.** A prefix matching multiple identities returns the first iteration hit; fine on a dev server, not a resolver to build player-facing features on.

## Where to go next

You've now seen every runtime system; [[13 Disconnect & Teardown]] closes the loop — what `client_disconnected`, `leave_world`, and `teardown_profile` clean up, and the ghost rows they deliberately don't. If you landed here out of order, [[00 End-to-End Timeline Flowchart]] re-sequences this doc's steps against the whole timeline, and [[01 Roadmap]] has the full doc map.
