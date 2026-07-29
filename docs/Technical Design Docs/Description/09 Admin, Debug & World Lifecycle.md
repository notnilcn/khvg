# 09 Admin, Debug & World Lifecycle

## Assumed knowledge

[[01 Architecture & Sync Model|01]] — this doc assumes the login-state tables (`LoggedInPlayer`/`LoggedOutPlayer`), the boot sequence (`init`, scheduled tables, seeding), and the subscription/push model without re-explaining them. [[02 Entity & Component Framework|02]] — the archetype-helper pattern (`spawn_enemy_archetype`/`despawn_enemy_archetype`) is the yardstick this doc measures `admin.rs`'s `spawn_enemy`/`despawn_enemy` bugs against. [[03 World & Hex Grid|03]] — chunk grid, `BuildingTile`, and terrain generation are `add_chunks`/`clear_chunks`/`generate_world_proc`/`generate_world_manual`'s subject matter; this doc covers only the admin-gated wrapper reducers, not the generation math itself. [[05 Item, Equipment & Enchantment System|05]] — `recompute_stats` is `change_stats`'s foil. [[06 Enemy AI & Bullet Patterns|06]] — the automatic spawn/despawn path this doc's admin reducers duplicate (badly, in `despawn_enemy`'s case).

## The 30-second version

There is exactly one admin identity for the whole server at any time — not a role list, a single slot any connected identity can claim if nobody else already holds it — and every reducer in `main/admin.rs` and `main/debug.rs` gates on that one `is_admin` flag. Nothing in the client calls any of them: no button, no menu, no keybind anywhere in the C# codebase reaches `claim_admin`, `spawn_enemy`, `generate_world_proc`, or any other reducer this doc covers. The only way any of this runs today is the SpacetimeDB CLI, invoked out-of-band from a running client — `spacetime call bullethell <reducer> <args>`. Once claimed, admin reducers mostly reuse machinery the rest of the game already has: catalog upserts share their update-if-exists-else-insert shape with `init`'s own one-time seeding, and `add_chunks`/`generate_world_proc` are thin gated wrappers around the exact same internal functions `init` calls once at boot. Two of them — `spawn_enemy` and `despawn_enemy` — are exceptions: both skip the archetype-helper pattern the automatic enemy-spawn path uses and leak or misroute the enemy's behavior tree as a result. Separately, `main/debug.rs` runs one narrow diagnostic (a scheduled mirror of every player's position into hex coordinates) that nothing in the client currently reads, and the client's own `DebugOverlay` is a third, unrelated thing entirely: a local FPS/memory HUD any player can toggle, with no admin gate and no tie to the server at all.

## System flowchart

```sync
![[99 End-to-End Timeline Flowchart#^admin-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-6{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-7{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-8{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-9{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-10{seamless:true,title:false,marker:10.}]]
```

## Main body

### One admin, not a role list

`claim_admin`/`release_admin` (`admin-1`) don't manage a set of privileged accounts — they manage a single boolean living on whichever login-state row (`LoggedInPlayer` or `LoggedOutPlayer`) the current admin happens to occupy. This is the same pair of tables [[01 Architecture & Sync Model|01]] introduces for ordinary login/logout tracking; admin status is just one more field riding along on rows that already exist for every player, not a separate permissions table. The practical consequence is that admin-ness moves with a player between lobby and in-world exactly like any other per-identity flag would — `claim_admin` and `is_admin` (`admin-2`) both check `LoggedInPlayer` first and fall back to `LoggedOutPlayer`, so an admin who joins the world or leaves it doesn't lose the flag, it's just read from a different row afterward.

Every reducer this doc covers — all of `admin.rs`, all of `debug.rs` — shares that one `is_admin` check rather than each reimplementing its own. There's no tiered permission system (no "moderator" below "admin"), no audit log of who claimed or released the slot beyond the plain `log::info!` lines in `claim_admin`/`release_admin` themselves, and no reducer-level rate limiting — the only thing standing between an arbitrary connected identity and every admin reducer in the game is whether they currently hold the one global flag.

### Reaching any of this: the CLI, not the game

None of `admin.rs`'s or `debug.rs`'s reducers have a client-side caller anywhere in `client/Scripts/` — confirmed by grepping the whole client tree for every reducer name this doc covers (`ClaimAdmin`, `ReleaseAdmin`, `ChangeStats`, `SpawnEnemy`, `DespawnEnemy`, `AddChunks`, `ClearChunks`, `GenerateWorldProc`, `GenerateWorldManual`, the four `Upsert*` reducers, `ToggleDebug`) and finding zero matches; the only near-hits are `BulletManager`/`Enemy`'s own `SpawnEnemyBullet` method, an unrelated client-only function that happens to share a substring. `DebugOverlay` (`admin-10`) is the sole piece of "debug" tooling with any client UI at all, and it isn't wired to any of this — see below. Concretely, today's only path to any of this doc's mechanisms is `spacetime call bullethell <reducer_name> <args...>` from a terminal with the SpacetimeDB CLI installed, run independently of any Godot client instance.

### Editing content live: the same shape as boot seeding, admin-callable at any time

`upsert_texture_entry`, `upsert_enemy_template`, `upsert_world_def`, `upsert_biome_def`, `upsert_biome_region_def` (`admin-4`) all follow one pattern: look the row up by its primary key, `.update(...)` if it's there, `.insert(...)` if it isn't. That's the identical shape `main/seeds.rs`'s `Seed` trait impls use for `init`'s one-time boot seeding ([[01 Architecture & Sync Model|01]]'s `boot-2`) — the only difference is *when* each runs. Seeding fires exactly once, the moment the module is first published; these fire whenever an admin calls them, against a server that's already live with connected players. Because `TextureEntry`, `EnemyTemplate`, `WorldDef`, `BiomeDef`, and `BiomeRegionDef` are all `public` tables, an edit lands on every currently-subscribed client's cache the instant the reducer commits — no republish, no client reconnect, the same row-push mechanism that makes ordinary gameplay state (health, position, inventory) update live applies equally here. An admin can retexture an item, rebalance an enemy template's phases, or redefine a biome's ground-texture weights against a running server and watch it take effect immediately for everyone connected.

### `change_stats`: an edit that doesn't survive the next recompute

`change_stats` (`admin-3`) looks its target up two hops deep — `find_player_by_username` (`admin-8`) resolves a username or identity-hex (full or prefix) to an `Identity`, then `find_profile_by_name` narrows to one of that player's profiles — and writes all six base stats straight onto `PlayerStats`. This is the one place in the admin surface that collides with an existing system rather than reusing it cleanly: `recompute_stats` ([[05 Item, Equipment & Enchantment System|05]]'s `equip-3`/`equip-4`) never reads the current `PlayerStats` row as an input — it always rebuilds all six stats from `compute_base_stats(level)` plus whatever's currently equipped/enchanted/buffed, and overwrites the row wholesale. Since equipping, unequipping, picking up, dropping, enchanting, un-enchanting, or even a consumable buff expiring all trigger a recompute, an admin's `change_stats` call is only as durable as the time until the next one of those happens to that profile — there's no flag anywhere marking a stat as "admin-set, don't recompute." In practice this makes `change_stats` useful for a quick, disposable test (buff a profile, watch a specific formula react) but not for a persistent balance override.

### The two `spawn_enemy`/`despawn_enemy` bugs

Both of these (`admin-5`) exist to let an admin place a specific enemy template at a specific point without waiting for `06`'s automatic biome-region spawning to roll it — but neither goes through `spawn_enemy_archetype`/`despawn_enemy_archetype`, the archetype-helper pair [[02 Entity & Component Framework|02]] and [[06 Enemy AI & Bullet Patterns|06]] both point to as the correct way to create or destroy an enemy's whole row set atomically.

`spawn_enemy` calls `build_enemy_behavior` directly, which on its own already inserts a complete `EnemyBehavior` row plus every `EnemyPhase`/`EnemyAttack`/`EnemySequenceStep` (and any `RepeatStepInstance`) the template's phase data describes, all pointed at that one `behavior_id`. `spawn_enemy` then wraps the *already-inserted* returned struct in a second, redundant `ctx.db.enemy_behavior().insert(...)` call — something `spawn_enemy_archetype` never does, since it only calls `build_enemy_behavior` once and uses its result directly. The final `Enemy` row's `behavior_id` points at whatever the second insert produces, not necessarily the row the freshly-built phase tree is actually attached to, so an admin-spawned enemy's link to its own attack sequences is unreliable.

`despawn_enemy` is a cleaner miss: it's a one-line `ctx.db.enemy().enemy_id().delete(&enemy_id)` and nothing else — no call to `despawn_enemy_archetype`, no call to `delete_enemy_behavior`. Every row that enemy's behavior tree ever created (`EnemyBehavior`, its phases, attacks, sequence steps, repeat instances) stays in the database forever, orphaned the instant the `Enemy` row referencing them is gone. Neither bug touches the automatic spawn/despawn path — `spawn_from_biome_regions` and `deal_damage_to_enemy`'s kill branch ([[06 Enemy AI & Bullet Patterns|06]]) both go through the archetype helpers correctly; these two bugs are specific to the admin-only manual path.

### World lifecycle: chunks vs. terrain are two different resets

`add_chunks` (`admin-6`) is a thin wrapper — admin gate, then a direct call to `internal_add_chunks`, the identical function `init` calls once at `boot-3` — so re-laying the chunk grid (different radius, columns, rows, or hex size) against a live server is one reducer call away. `clear_chunks`, though, only deletes `BuildingTile` rows: the per-hex grid `place_building`/`remove_building` ([[03 World & Hex Grid|03]]) write player-placed building state into. It does not touch `TriangleTile`, `HexDecor`, `BiomeRegion`, or `MapConfig` — none of the rows that actually define what a player sees as terrain. Calling `clear_chunks` alone doesn't blank the visible world; it only wipes where buildings are legally allowed to sit.

Regenerating the terrain itself is `generate_world_proc`/`generate_world_manual`'s job (`admin-7`) — admin-gated wrappers around the exact `internal_generate_world_proc`/`internal_generate_world_manual` entry points `init` itself calls once at `boot-4` to build the starting world. An admin can re-roll the whole map (a new procedural seed) or hand-author a specific biome layout (`Vec<ManualBiomeInput>`) against a running server at any time, without a republish — the generation pipeline itself, including whatever it does with pre-existing `TriangleTile`/`HexDecor` rows before writing new ones, is [[03 World & Hex Grid|03]]'s concern, not repeated here.

### The position-debug mirror nobody reads

`main/debug.rs` is a single-purpose diagnostic, separate from everything above: `toggle_debug` (`admin-9`, still `is_admin`-gated) flips a `PlayerPositionDebugSchedule` row on or off — a scheduled table shaped like the three `boot-1` describes, here ticking at a fixed 1-second interval only while active — and its paired reducer, `tick_player_position_debug`, mirrors every live `PlayerPosition` row into a `PlayerPositionDebug` row carrying the same `x`/`y` plus the equivalent `(hex_q, hex_r)` hex coordinate, deleting any mirrored row whose player no longer has a live position. It exists, presumably, to let someone eyeball players' positions in hex-space directly against the database. But nothing in the client subscribes to it — a full-tree grep of `client/Scripts/` for `PlayerPositionDebug` returns nothing — so as the code stands today, the only way to actually see this table's contents is a direct database query (`spacetime sql`), not any in-game view. The mechanism runs correctly; there's simply no consumer wired up to it yet.

### `DebugOverlay`: a different "debug" entirely

`DebugOverlay` (`admin-10`) is easy to conflate with the server-side debug tooling above by name alone, but it's unrelated in every way that matters: it's instanced directly under `Main` in `main.tscn`, present and ticking from the moment the scene loads regardless of connection or admin state (the same always-present pattern [[08 Client Rendering & Camera|08]]'s `camera-1` describes for the camera rig), toggled by the `P` key with no admin check whatsoever, and every value it displays — FPS, frame time, static memory, object/node counts, draw calls, plus `GameManager.EnemyCount` — comes from Godot's own `Performance` monitor API or a local client-side count, never from a server row. It updates its one `Label` every quarter second (`UpdateInterval`) while visible and does nothing else. It shares a folder (`Scripts/Game/`) and a name pattern with the server's admin/debug surface, and nothing more.

## Known gaps / stubs

- **No client UI reaches any reducer this doc covers.** `claim_admin`, `release_admin`, `change_stats`, all five catalog upserts, `spawn_enemy`, `despawn_enemy`, `add_chunks`, `clear_chunks`, `generate_world_proc`, `generate_world_manual`, and `toggle_debug` are all only callable via the SpacetimeDB CLI today — there's no in-game admin panel, console, or debug menu of any kind.
- **`spawn_enemy` double-inserts the behavior row**, potentially leaving the spawned `Enemy`'s `behavior_id` pointed at a row that doesn't carry the phase tree `build_enemy_behavior` actually built. See "The two `spawn_enemy`/`despawn_enemy` bugs" above.
- **`despawn_enemy` leaks the entire behavior tree** — it deletes only the `Enemy` row, never the `EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` rows built for it.
- **`change_stats` is not a persistent override** — `recompute_stats` overwrites it the next time anything in [[05 Item, Equipment & Enchantment System|05]]'s equip/inventory pipeline runs for that profile.
- **`PlayerPositionDebug` has no client subscriber.** The scheduled mirror runs correctly server-side once toggled on, but nothing in `client/Scripts/` reads it.
- **`clear_chunks` doesn't clear terrain.** It only deletes `BuildingTile` rows (the building-placement grid), not `TriangleTile`/`HexDecor`/`BiomeRegion` — an admin expecting it to blank the visible map will be surprised it doesn't.
- **No audit trail beyond log lines.** `claim_admin`/`release_admin` log via `log::info!` only; there's no persisted history of who has held the admin slot.

## Where to go next

This closes the per-system docs (01–09). [[00 Roadmap|00]]'s status table tracks what's left; the final session builds `99 End-to-End Flowchart.canvas`, which arranges every `^prefix-N` anchor from `99` — including this doc's `^admin-N` steps — spatially, plus runs a final cross-doc verification pass ([[00 Roadmap|00]]'s reading-order table, row 10). Re-reading [[01 Architecture & Sync Model|01]] alongside this doc is worthwhile: nearly every mechanism here (login-state rows, scheduled tables, public-table push, the `Seed`-shaped upsert pattern) is a variation on something `01` already introduced for the ordinary player path.
