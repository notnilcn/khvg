# 08 Admin, Debug & World Lifecycle

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary, the boot sequence (`spacetime start`/`publish`/`generate`, `init` running once on a fresh publish), and the connection lifecycle (`client_connected`/`client_disconnected`) this doc picks up the admin/world-lifecycle half of — 01 covers what those two reducers do for a normal player's login state; this doc covers what they *don't* do for the admin flag riding along on the same rows.
- [[02 World & Hex Grid]] for the procedural world-gen algorithm (Voronoi biome/region assignment, per-wedge texture picks, decor placement) and the chunk grid (`add_chunks`, `BuildingTile`) — this doc covers only the admin-reducer wrapper layer around both, not the algorithm underneath.
- [[03 Player System]] for the `LoggedOutPlayer`/`LoggedInPlayer` login-state machine `is_admin` rides on, and for its explicit note that the single-admin-slot mechanics were left for this doc.
- [[05 Enemy AI & Bullet Patterns]] for `EnemyTemplate`/`build_enemy_behavior` — this doc's `spawn_enemy` bug is a misuse of that function, not a new mechanism.

## The 30-second version

`is_admin` is one bit living on whichever row (`LoggedInPlayer` or `LoggedOutPlayer`) a connected identity currently owns — there's no separate roles table and no hierarchy, just this flag, checked by a plain `is_admin(ctx)` helper. The whole server has exactly one admin slot at a time: `claim_admin` refuses if *any* `LoggedInPlayer`/`LoggedOutPlayer` row anywhere already has `is_admin: true`, and `release_admin` is the only reducer that ever clears it. Disconnecting while holding it doesn't free it — `client_disconnected` moves the identity to `LoggedOutPlayer` but copies `is_admin` across unchanged, so the slot stays locked until that same identity reconnects and calls `release_admin` itself. Every other reducer in `main/admin.rs` gates on that same flag: catalog edits (`change_stats`, `upsert_texture_entry`, `upsert_enemy_template`), world-gen re-triggers (`generate_world_proc`/`generate_world_manual`, `upsert_world_def`/`upsert_biome_def`/`upsert_biome_region_def`) that call the exact same internal functions `init` calls at boot, chunk management (`add_chunks`/`clear_chunks`), and enemy spawn/despawn (`spawn_enemy`/`despawn_enemy` — the latter's spawn path has a real bug, covered below). None of this has a client-side UI: no C# script anywhere calls any of these reducers, so today they're only reachable via the SpacetimeDB CLI or web dashboard. Separately, "debug" means two unrelated things: a server-side `PlayerPositionDebug` table an admin can toggle on for external inspection, and a client-only `DebugOverlay` perf HUD toggled by pressing P, with no server involvement at all.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the boot/connection steps this doc's admin mechanics ride on top of, then the whole of "07 Meanwhile, independently," which belongs to this doc alone.

```sync
![[99 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^end-2{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-1{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-2{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-3{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-4{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-5{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-6{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-7{seamless:true,title:false,marker:10.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^admin-8{seamless:true,title:false,marker:11.}]]
```

## The single-admin-slot mechanic

Steps 4–6 above are `claim_admin`/`release_admin` in full. There's no admin *table* — `is_admin: bool` is just another field on `LoggedInPlayer` and `LoggedOutPlayer`, the same two tables [[03 Player System]] documents for ordinary login state, and `set_username` already patches whichever one currently holds a given identity's row for the same reason: neither half of the split carries information the other lacks. `claim_admin` checks both tables' `is_admin` columns for *any* `true` row before granting one to the caller — this is a single global slot, not a permission you either have or don't independent of everyone else. If the caller already has it, `claim_admin` is a no-op success rather than an error; if someone else has it, the caller gets `Err("An admin already exists. Use release_admin to remove them first.")`. `release_admin` only clears the caller's own row and errors if the caller isn't currently admin. See [[admin.rs##pub fn claim_admin|claim_admin]] and [[admin.rs##pub fn release_admin|release_admin]].

Neither reducer is gated by `require_in_lobby`/`require_in_world` — a caller can claim or release admin whether they're logged into a profile or sitting in the lobby, since `is_admin` lives on the login-state row itself (`LoggedOutPlayer`/`LoggedInPlayer`), not on a `PlayerProfile`.

**The disconnect-while-admin edge case**, worth reading directly against [[lifecycle.rs##pub fn client_disconnected|client_disconnected]]: when a connection drops, `client_disconnected` moves a `LoggedInPlayer` row to `LoggedOutPlayer` by copying every field across — `username`, and `is_admin` right along with it — and does nothing at all if the identity was already `LoggedOutPlayer` (i.e. was in the lobby, not in-world, when it dropped). There is no branch anywhere in `client_disconnected` that clears `is_admin`. The practical consequence: an admin who closes their client (or loses connection) mid-session keeps `is_admin: true` sitting on their `LoggedOutPlayer` row indefinitely — `claim_admin`'s "does anyone already have it" check still finds that row and refuses every other identity, with no timeout and no way for anyone else to break the lock. The only way out is that exact identity reconnecting (a fresh anonymous `Identity` every launch today, per [[01 Architecture & Sync Model]]'s "Known gaps" — which itself means a disconnected admin may not even be able to reclaim their own slot without `AuthToken` persistence, since a relaunch gets a *different* identity that was never admin in the first place) and calling `release_admin` itself. This is a real, easy-to-hit lock, not a hypothetical: an admin testing something and simply closing the Godot window leaves the server with no way to grant admin to anyone else until that specific identity comes back.

## The admin reducer catalog

Everything below (`main/admin.rs`) opens with `if !is_admin(ctx) { return Err("Admin only.".to_string()); }` — a plain read of whichever login-state row the caller currently owns, no separate roles system. Grouped by what they actually do:

**Content/catalog edits** — one-off, ad-hoc row edits, no scheduling involved:
- `change_stats(target_username, profile_name, strength, wisdom, dexterity, defense, vitality, speed)` resolves a target player via `find_player_by_username` (below), then a specific profile of theirs by name via [[player/methods.rs##pub fn find_profile_by_name|find_profile_by_name]], and overwrites that profile's `PlayerStats` wholesale with the six values passed in — this bypasses `recompute_stats` entirely, so a subsequent equip/unequip will immediately fold the *real* level+item+buff formula back on top of whatever an admin set here, not preserve it.
- `upsert_texture_entry(entry: TextureEntry)` / `upsert_enemy_template(template: EnemyTemplate)` each take a whole row and insert-or-update by primary key (`texture_id`/`template_id`) — the same insert-or-update shape [[04 Item & Equipment System]]'s `upsert_item` uses for the item catalog, just for two different tables.

**World-gen triggers** — thin admin-gated wrappers around the exact same internal functions `init` calls once at boot:
- `generate_world_proc(world_id, seed)` calls `internal_generate_world_proc` — the same function [[02 World & Hex Grid]]'s "Procedural world generation" section covers in full (Voronoi biome/region assignment, per-wedge texture/overlay picks, decor placement) — meaning an admin can repaint the entire live map on demand, deterministically, from any `(world_id, seed)` pair, without restarting the server. `generate_world_manual(biomes: Vec<ManualBiomeInput>)` is the hand-authored counterpart 02 already names, taking explicit biome/region layouts instead of rolling from weight tables.
- `upsert_world_def(def: WorldDef)` / `upsert_biome_def(def: BiomeDef)` / `upsert_biome_region_def(def: BiomeRegionDef)` edit the weight-table inputs those generators read — same insert-or-update-by-primary-key shape as the catalog edits above, just feeding world-gen specifically.
- `add_chunks(chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius)` and `clear_chunks()` wrap `internal_add_chunks` — the same function `init` calls to lay out the starting `BuildingTile` grid and `MapConfig` row ([[02 World & Hex Grid]]'s "Chunks and the starting grid" section) — letting an admin resize or wipe the chunk grid on a live server. `clear_chunks` deletes every `BuildingTile` row without touching terrain or `MapConfig`.

**Enemy spawn/despawn:**
- `spawn_enemy(template_id, x, y, is_elite, immortal)` looks up an `EnemyTemplate`, wraps the given `(x, y)` through the same `wrap_world_pos`/`world_to_chunk`/`spiral_chunk_index` helpers [[02 World & Hex Grid]] documents for players and enemies, and is the *only* code path anywhere that can set `is_elite`/`immortal` to anything but `false` — [[05 Enemy AI & Bullet Patterns|05's]] finding that the normal spawn path (`spawn_from_biome_regions`) always hardcodes both to `false`. It has a bug — see "Known gaps" below.
- `despawn_enemy(enemy_id)` is a plain `ctx.db.enemy().enemy_id().delete(&enemy_id)` — note this does *not* cascade the `EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` rows the way `report_enemy_hit`'s death path does ([[05 Enemy AI & Bullet Patterns]]) — it only deletes the `Enemy` row itself, orphaning the behavior/phase/attack/step rows `build_enemy_behavior` created for it. Those orphaned rows are inert (nothing iterates `EnemyBehavior` independently of a live `Enemy` row referencing it) but they are never cleaned up either — a small, real leak on every admin despawn.

**`find_player_by_username`'s multi-match behavior**, used by `change_stats` above and by `item/reducers.rs`'s `give_item`/`remove_item` ([[04 Item & Equipment System]]): it takes one lookup string and tries it against three different things, in order — a case-insensitive match on `username`, an exact match against the full lowercase identity hex (stripping an optional `0x` prefix from the input first), or the input as a *prefix* of that hex — returning the first player, iterating `LoggedInPlayer` then `LoggedOutPlayer`, that matches any of the three. This is convenient for an admin poking at the CLI (paste a short hex prefix instead of a full 64-character identity, or just type a name), but it also means a short numeric-looking username could theoretically collide with an identity-hex prefix; nothing disambiguates the two cases beyond "username" being checked first. See [[admin.rs##pub fn find_player_by_username|find_player_by_username]].

## Seeding: where `init`'s starting catalog comes from

`main/seeds.rs` is what `init` (step 1 above) actually calls to populate every static table before any client can connect: `seed_default_textures`/`seed_layering_rules`/`seed_adjacency_rules`/`seed_decor_ground_rules` (texture catalog and the tag-based rules [[02 World & Hex Grid]] covers), `seed_default_enemies` plus `seed_test_boss_p2` through `p6` (the `EnemyTemplate` rows [[05 Enemy AI & Bullet Patterns]] covers in full, including the builder helpers `make_phase`/`make_sequence`/`seq_single`/`seq_repeat`/`seq_multi`), and `seed_world_defs` (the `WorldDef`/`BiomeDef` weight tables `generate_world_proc` reads). `item/seeds.rs`'s `seed_world_items` ([[04 Item & Equipment System]]) is called from here too, alongside the world-gen and chunk-layout calls covered above. None of the seed data itself is this doc's concern — it's fully covered by 02, 04, and 05 respectively — what belongs here is only that this file is `init`'s one-stop source for all of it, run exactly once per fresh publish.

## Debug: two unrelated things sharing a name

**Server-side position debug** (`main/debug.rs`): `toggle_debug` is admin-gated and flips a scheduled reducer on or off — if `PlayerPositionDebugSchedule` has no rows, it inserts one (a 1-second recurring interval); if one exists, it deletes every schedule row *and* clears the entire `PlayerPositionDebug` table. While active, `tick_player_position_debug` mirrors every `PlayerPosition` row into a parallel `PlayerPositionDebug` row, additionally computing each position's hex coordinates (`world_to_hex`) — and deletes any `PlayerPositionDebug` row whose player no longer has a live `PlayerPosition` (tracked via a `HashSet` of currently-active ids each tick). No client subscribes to this table anywhere in `client/Scripts/` — it exists purely for inspection via the SpacetimeDB CLI (`spacetime sql`) or web dashboard, not for anything the Godot client renders. See [[debug.rs##pub fn toggle_debug|toggle_debug]] and [[debug.rs##pub fn tick_player_position_debug|tick_player_position_debug]].

**Client-side perf overlay** (`Game/DebugOverlay.cs`): entirely unrelated to the above — a `CanvasLayer` added as a child of `GameManager` in its `_Ready`, starting hidden, toggled by pressing **P** (`_Input`, checked for `Key.P` on a non-echo key-down). While visible, `_Process` updates a label every 0.25s with FPS, frame time, static memory, object/node counts, draw calls (all from Godot's `Performance` singleton), and `GameManager.Instance.EnemyCount`. There's no admin gate, no server round-trip, and no connection to `PlayerPositionDebug` at all — it's a local performance HUD any player can open, not a privileged tool. See [[DebugOverlay.cs##public override void _Input|_Input]].

## Is there an admin UI?

No. A search of `client/Scripts/` for calls to `ClaimAdmin`, `ReleaseAdmin`, `ChangeStats`, `SpawnEnemy`, `DespawnEnemy`, `AddChunks`, `ClearChunks`, `GenerateWorldProc`/`GenerateWorldManual`, `UpsertWorldDef`/`UpsertBiomeDef`/`UpsertItem`/`UpsertTextureEntry`/`UpsertEnemyTemplate`, `ToggleDebug`, or `FindPlayerByUsername` turns up nothing — every admin reducer in `main/admin.rs` (and item's `give_item`/`remove_item`/`upsert_item`) is only reachable today via the SpacetimeDB CLI (`spacetime call <db> <reducer> <args>`) or the web dashboard, not from any in-game panel. This is a genuine gap, not a design decision described anywhere in the code — there's no `AdminPanel.cs` or similar, and no scene wires a button to any of these calls.

## Known gaps and stubs

- **`spawn_enemy` double-inserts the `EnemyBehavior` row — verified directly.** [[enemy/methods.rs##pub fn build_enemy_behavior|build_enemy_behavior]] already calls `ctx.db.enemy_behavior().insert(EnemyBehavior { .. })` internally and returns that already-inserted row, complete with its real, non-zero `behavior_id` (an `#[auto_inc] #[primary_key]` column — confirmed in `enemy/instance_tables.rs`). The normal spawn path (`spawn_from_biome_regions`, [[05 Enemy AI & Bullet Patterns]]) uses this correctly: `let inserted_behavior = build_enemy_behavior(ctx, &template);` — one insert. `spawn_enemy` instead writes `ctx.db.enemy_behavior().insert(build_enemy_behavior(ctx, &template))` — calling `.insert(..)` a *second* time on the row `build_enemy_behavior` already inserted, with the same primary-key value it was already assigned. Because `behavior_id` is a primary key, this is an attempt to insert a second row under a key that already exists in the table — in practice this is far more likely to abort the whole reducer with a duplicate-primary-key error (rolling back the entire `spawn_enemy` call, including the `Enemy` insert right after it) than to silently succeed with a harmless-looking duplicate. Either way, this is a real bug in the admin one-off spawn path specifically — `enemy/reducers.rs`'s own scheduled spawn path doesn't share it.
- **`despawn_enemy` doesn't cascade.** Unlike `report_enemy_hit`'s death path, `despawn_enemy` deletes only the `Enemy` row — the `EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance` rows `build_enemy_behavior` created for it are left behind, orphaned. They don't visibly affect anything (nothing iterates them independent of a live `Enemy` referencing them), but they're a real leak that accumulates with every admin despawn.
- **Disconnecting while admin permanently locks the slot**, detailed above under "The single-admin-slot mechanic" — worth restating here as a gap rather than a deliberate design point: there's no timeout, no server-side "admin went AFK" detection, and (per [[01 Architecture & Sync Model]]'s identity-persistence gap) a relaunched client gets a *new* anonymous identity that was never the admin in the first place, so recovering from this today may require directly editing the database via the CLI rather than anything a reducer call can fix.
- **No admin UI exists client-side** — see "Is there an admin UI?" above. Every admin/content reducer is CLI/dashboard-only today.
- **`change_stats` bypasses `recompute_stats`.** It writes `PlayerStats` directly rather than going through the fold [[04 Item & Equipment System]] documents, so any subsequent equip/unequip/buff-tick immediately overwrites an admin's manual stat edit with the real level+item+buff-derived value — `change_stats` is only a lasting change if nothing ever triggers a recompute afterward.
- **`server/CLAUDE.md`'s `main/` module breakdown checked out accurate.** Unlike the stale `views.rs`/reducer-name claims prior docs found elsewhere in that file, the `global.rs`/`lifecycle.rs`/`admin.rs`/`seeds.rs`/`debug.rs` per-file lists match the actual code in this module exactly — every reducer, table, and helper named there exists under the name given, with nothing missing and nothing extra. No correction needed here, unlike [[01 Architecture & Sync Model|01]]'s and [[04 Item & Equipment System|04]]'s findings for other modules.

## Where to go next

This is the last system doc in the series — [[99 End-to-End Timeline Flowchart]] is next, now fully filled in across all eight systems' forks and sections. See that file's "Session end" note for the deferred renumbering pass (letter-suffixed steps → plain integers) that's now ready to be picked up as its own task.
