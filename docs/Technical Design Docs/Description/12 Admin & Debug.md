# 12 Admin & Debug

## Assumed knowledge

- [[03 Boot & Connection]] — boot-4's `init` seeds and boot-5/boot-6's `internal_add_chunks`/`internal_generate_world_proc`, the publish-time machinery this doc's admin reducers re-expose at runtime.
- [[05 Joining the World]] — join-1's `LoggedOutPlayer`→`LoggedInPlayer` row move, which is what carries `is_admin` into the world.
- [[06 Movement & Position Sync]] — move-3's wrap/chunk canonicalization pipeline, reused by `spawn_enemy`.
- [[07 Terrain & World Streaming]] — terrain-1's two generation front-ends and terrain-6's AOI terrain views, the two halves of what a live regen touches.
- [[08 Enemies & AI]] — enemy-1's seeded templates and `seq_*` def helpers, enemy-3's archetype spawn/despawn helpers.
- [[09 Combat & Damage]] — the `immortal` early-return in `report_enemy_hit`, reachable only through this doc's `spawn_enemy`.
- [[10 Inventory, Items & Enchantments]] — `recompute_stats` and the equip/loot reducers `change_stats`/`give_item`/`remove_item` bypass.
- [[02 The Component Framework]] — the nine unreferenced duplicate component scenes (one of them is this doc's drift hazard).
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `admin` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

There is no admin UI anywhere — administration is a set of **reducers called from the SpacetimeDB CLI** (`spacetime call`), gated by a single `is_admin` flag that one player at a time claims with `claim_admin` and frees with `release_admin`. Once flagged, an admin can edit players (`change_stats`, `give_item`, `remove_item` — the latter two living in `item/reducers.rs`), patch catalog content live (texture/item/enchantment/enemy-template upserts), author enemy behavior defs at runtime, spawn and despawn arbitrary enemies (the only source of elite/immortal enemies and of the seeded test bosses), and reshape the world (`add_chunks`/`clear_chunks`, world/biome def upserts, `generate_world_proc`/`generate_world_manual`). On the debug side, the admin-gated `toggle_debug` arms a 1-second scheduled reducer that mirrors every `PlayerPosition` into a `PlayerPositionDebug` table with hex coordinates decoded, inspectable via `spacetime sql`; and the client ships a `DebugOverlay` CanvasLayer, declared inline in `main.tscn` and hidden by default, that the P key toggles to show local performance counters.

## Flowcharts

- [[flowcharts/main-admin.canvas]] — the composed admin flow (the client's `Game` scripts including `DebugOverlay`, `main.tscn`'s inline wiring, the server `main` module with `admin.rs`/`debug.rs`/`seeds.rs`, and the `item` module's admin reducers).
![[flowcharts/main-admin.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/admin_codefile/admin_codefile.canvas]] — deep dive: `admin.rs`, all 18 admin reducers plus `internal_add_chunks` and `find_player_by_username`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/debug_codefile/debug_codefile.canvas]] — deep dive: `debug.rs`, the `PlayerPositionDebug` mirror and its schedule.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Game_subfolder/DebugOverlay_codefile/DebugOverlay_codefile.canvas]] — deep dive: `DebugOverlay.cs`, the client perf overlay.

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

```sync
![[00 End-to-End Timeline Flowchart#^admin-8{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-9{seamless:true,title:false,marker:09.}]]
```

## Main body

### Becoming admin: one global slot

```sync
![[00 End-to-End Timeline Flowchart#^admin-1{seamless:true,title:false,marker:01.}]]
```

A **reducer** is SpacetimeDB's unit of server-side work — a function clients invoke by name over the connection, executed transactionally against the database — and admin reducers are invoked from the SpacetimeDB CLI rather than from game code, so "whoever runs the CLI against this database" is the real access boundary. The single-slot design means there is no role system to keep consistent: `is_admin` is one boolean on the same lobby/world row that tracks login state, it rides along on the join-1 row move for free, and the [[server/spacetimedb/src/player/methods.rs##pub fn is_admin|is_admin]] guard is a two-line lookup every gated reducer shares. The cost of that simplicity is operational: `claim_admin` refuses while *any* row in either table has the flag, and `release_admin` only works for the flag's owner — so if the admin disconnects without releasing, the slot is stuck until the database is reset or edited directly (see Known gaps).

Because `claim_admin` works on the `LoggedOutPlayer` row too, the intended flow is: connect (conn-2 plants the lobby row), claim admin from the lobby, then join the world with the flag already on.

### Targeting other players

```sync
![[00 End-to-End Timeline Flowchart#^admin-2{seamless:true,title:false,marker:02.}]]
```

The prefix-hex match in [[server/spacetimedb/src/main/admin.rs##pub fn find_player_by_username|find_player_by_username]] is a convenience for CLI use — identities are 32 hex characters, and typing a handful is enough — but it returns the *first* row in table-iteration order that matches, so a prefix short enough to collide picks an arbitrary victim. Full usernames are matched case-insensitively and only when non-empty (a fresh conn-2 row has an empty username, which can never be matched — deliberately, so "no username" isn't a targetable name). Note that targeting searches *both* player tables, so an admin can `change_stats` or `give_item` to a player sitting in the lobby as readily as one in the world.

### Editing players: stats and inventory

```sync
![[00 End-to-End Timeline Flowchart#^admin-3{seamless:true,title:false,marker:03.}]]
```

The asymmetry between the two edit paths is worth internalizing. `change_stats` writes the *derived* table directly, so it is overwritten by the stat pipeline's next run — recompute_stats treats `PlayerStats` as a pure function of level + equipment + effects, with no room for an admin override. `give_item`/`remove_item` write the *source* table (`PlayerInventory`) but skip the pipeline, so their stat effects don't materialize until an unrelated equip/enchantment/consumable change triggers a recompute. Neither reducer is wrong in isolation; they just disagree about which table is authoritative, and the practical recipe is "give the item, then change the stats last" — or accept that the next recompute reverts the stats.

### Live content editing: catalog and enemy authoring

```sync
![[00 End-to-End Timeline Flowchart#^admin-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^admin-5{seamless:true,title:false,marker:05.}]]
```

These reducers exist because boot-4's seeds only run at publish: without them, tuning a texture path, an item's modifiers, or a boss's bullet pattern would require republishing the module (which re-runs every seed and regenerates the world). The upsert shape mirrors the `Seed` trait in [[server/spacetimedb/src/main/seeds.rs##pub trait Seed|main/seeds.rs]] — look up by natural key, update-or-insert — so runtime edits and publish-time seeds can't create divergent duplicates of the same def. What they *don't* give you is rollback: an upsert overwrites the row in place, and the next republish's seeds overwrite it again, so runtime edits are always provisional relative to the seeded baseline.

The enemy-authoring upserts have one more subtlety beyond the def_id-0 footgun in admin-5: because enemy-3's spawn helper copies defs into per-enemy instance rows, editing a `SingleStepDef` mid-test only changes enemies spawned *after* the edit — a live boss keeps firing the old pattern, which is usually what you want when A/B-testing patterns but surprises anyone expecting a hot patch.

### Spawning and despawning enemies by hand

```sync
![[00 End-to-End Timeline Flowchart#^admin-6{seamless:true,title:false,marker:06.}]]
```

This is the only reducer pair that creates or destroys live gameplay state rather than editing defs, and it deliberately reuses the exact helpers the scheduled spawner uses — an admin-spawned enemy enters the same behavior tick, the same AOI view, and the same client spawner as a natural one, so nothing downstream can tell the difference (and no special-case teardown is possible either). The two flags are the point — or half of it: `immortal` arms the early-return in `report_enemy_hit`, an unkillable target dummy for testing patterns, while `is_elite` is written onto the `Enemy` row and then *never read anywhere* — a stub field only this reducer can set (see Known gaps). And because `seed_region_def` hardcodes every region's template list to `["Enemy", "Archer"]`, the entire seeded boss roster (`TestBoss`, `TestBossP2`–`TestBossP6`) has no natural spawn path at all: this reducer is the only door into a boss fight.

### Reshaping the world

```sync
![[00 End-to-End Timeline Flowchart#^admin-7{seamless:true,title:false,marker:07.}]]
```

The world-shape reducers are the heaviest hammer in the set and the least guarded: `generate_world_proc`/`generate_world_manual` run the full terrain-1 pipeline synchronously inside the reducer call, and since a reducer is one transaction, every in-world client's terrain AOI views tear down and rebuild in a single commit — correct (the client's terrain-7/terrain-8 rebuild machinery is built for exactly this row churn) but indiscriminate, with no "maintenance mode" to warn players. `internal_add_chunks`'s own validation survives into `add_chunks` (`chunk_cols` must evenly divide `chunk_rows`, positive hex radius), and the `MapConfig` upsert is what re-broadcasts new lap vectors to every client's `TableSubscriber` (conn-4) if the grid is resized — the client mirrors them, so a live resize at least propagates coherently.

### The server debug channel

```sync
![[00 End-to-End Timeline Flowchart#^admin-8{seamless:true,title:false,marker:08.}]]
```

`PlayerPositionDebug` answers the question "where does the *server* think everyone is, in hex terms?" — the positions clients are reporting (move-2/move-3), decoded into the hex/chunk addressing that terrain, AOI, and spawning all key on, so a mismatch between what you see in-game and what this table shows localizes the bug to the client, the network, or the server's canonicalization. The mirror is off by default because it costs a full `PlayerPosition` scan every second; `toggle_debug` is a genuine toggle whose "off" path also wipes the data table, so a stale snapshot can't be mistaken for live data. It mirrors *every* `PlayerPosition` row — including the ghost rows that outlive logout ([[13 Disconnect & Teardown]] covers those) — which makes the table double as a ghost-row detector.

### The client debug overlay

```sync
![[00 End-to-End Timeline Flowchart#^admin-9{seamless:true,title:false,marker:09.}]]
```

`DebugOverlay` is deliberately not a component — it's a plain `CanvasLayer` node with a script, sitting next to the component tree in `main.tscn`, because it needs nothing from the entity framework: no registration, no binders, no server data. A `CanvasLayer` draws in screen space on its own layer (here 100, above the game world and the 3D backdrop's layer), which is why the readout stays put while the camera rotates and zooms. All six counters come from Godot's `Performance.GetMonitor` — the engine's built-in profiler counters — except `EnemyCount`, which rides the [[GameManager.cs##public static int EnemyCount|GameManager facade]] down to the spawner's tracking dictionary (join-5). The 0.25 s throttle exists because formatting six monitors into a label every frame would itself show up in the frame-time counter. The hidden default is set in the scene (`visible = false`), not the script, so the shipped game starts clean and the P key is the only switch.

## Known gaps / stubs

- **A departed admin bricks the slot.** [[server/spacetimedb/src/main/admin.rs##pub fn claim_admin|claim_admin]] refuses while any row in either player table has `is_admin`, and [[server/spacetimedb/src/main/admin.rs##pub fn release_admin|release_admin]] errors unless the *caller* is the admin — there is no force-release. If the admin disconnects (or loses their token/identity) without releasing, no one can become admin short of direct database surgery.
- **`change_stats` is silently reverted by the next stat recompute.** It overwrites the six `PlayerStats` fields directly, but [[server/spacetimedb/src/player/methods.rs##pub fn recompute_stats|recompute_stats]] rebuilds them from `compute_base_stats(level)` + modifiers on every gear/effect change, discarding the admin's values.
- **`give_item`/`remove_item` skip `recompute_stats`.** Already flagged in [[10 Inventory, Items & Enchantments]] → Known gaps; repeated here because it's the admin path — a granted or revoked equipment piece doesn't change stats until something else triggers a recompute.
- **Prefix identity matching can hit the wrong player.** [[server/spacetimedb/src/main/admin.rs##pub fn find_player_by_username|find_player_by_username]] returns the first iteration-order row matching a hex prefix; a short prefix that collides picks an arbitrary target with no confirmation.
- **`is_elite` is a write-only field.** [[server/spacetimedb/src/enemy/instance_tables.rs##pub struct Enemy {|Enemy]] stores it and [[server/spacetimedb/src/main/admin.rs##pub fn spawn_enemy|spawn_enemy]] is the only writer that can set it true (natural spawns hardcode false), but no combat, XP, loot, or client code ever reads it — grep finds zero reads outside the table definition and the spawn helper.
- **No in-game admin console.** Every admin reducer is CLI-only — no client script calls any of them (verified by grep) — so administration requires shell access to the SpacetimeDB CLI. That's a deliberate boundary, but it means there is also no audit log, no confirmation prompt, and no partial permissioning anywhere in the stack.
- **`debug_overlay.tscn` is an unreferenced duplicate.** The live overlay is declared inline in `main.tscn` (admin-9); `Scenes/UI/debug_overlay.tscn` declares the same script and node names but nothing instances it — one of the nine duplicate component scenes from [[02 The Component Framework]] → Known gaps, and a drift hazard if anyone edits the wrong copy.

## Where to go next

The last doc in the series, [[13 Disconnect & Teardown]], covers what happens when rows go away: `client_disconnected`, `leave_world`, death teardown, and the ghost `PlayerPosition`/`PlayerChunk` rows that admin-8's debug table happens to make visible.
