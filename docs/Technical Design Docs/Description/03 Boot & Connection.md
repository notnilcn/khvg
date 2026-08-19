# 03 Boot & Connection

## Assumed knowledge

- [[00 End-to-End Timeline Flowchart]] — the global timeline this doc expands (its *Boot & Connection* section is transcluded below).
- [[02 The Component Framework]] — what a `Component` is, how components register with an `IEntity`, and how `TableBinderComponent` re-fires table row events as Godot signals. `TableSubscriber` and `GameManager` are both framework citizens.

## The 30-second version

Boot happens twice, once per side. **Server:** publishing the module (`server/build.sh`) wipes the database and fires the `init` reducer exactly once, which inserts the three scheduled-job rows that drive all server-side time, seeds every static content table (textures, terrain rules, enemy templates, items, enchantments, world definitions), lays down the hex/chunk grid as `BuildingTile` rows plus the singleton `MapConfig`, and procedurally generates the "Earth" terrain. **Client:** Godot reads `project.godot`, registers the `DatabaseConnector` autoload (a singleton that owns the SpacetimeDB connection), and opens the menu scene. The autoload connects immediately — reusing a persisted auth token so a returning player keeps their identity — and fires a `Connected` signal. Nothing subscribes yet; when the gameplay scene `game.tscn` loads, its inline `TableSubscriber` component opens the base and lobby subscription waves, and from then on every server row the client cares about flows in through those subscriptions.

## Flowcharts

- [[flowcharts/main-boot.canvas]] — the composed boot & connection flow (server `init` pipeline + client connect/subscribe path).
- [[flowcharts/Subflowcharts/client_subfolder/sstdbsdk_subfolder/sstdbsdk_subfolder.canvas]] — the first-party SDK layer: `DatabaseConnector`, `TableSubscriber`, `TableBinderComponent`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/main_subfolder/main_subfolder.canvas]] — the server `main` module: `lifecycle.rs` (`init`/`client_connected`), `seeds.rs`, `admin.rs`, `global.rs`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/world_subfolder/terrain_subfolder/terrain_subfolder.canvas]] — the terrain generation pipeline `init` ends in.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^boot-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^boot-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-1{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-2{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-4{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-5{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^conn-6{seamless:true,title:false,marker:10.}]]
```

## Main body

### Server boot: publish is a hard reset, `init` rebuilds everything

The server has no persistent state across publishes. [[server/build.sh|build.sh]] does exactly two things: regenerate the C# client bindings into `client/Scripts/module_bindings/`, then `spacetime publish bullethell --delete-data -y`. The `--delete-data` flag drops every table row, so the database the `init` reducer runs against is always empty — schema changes are hard cuts with no migration path, and `init` is therefore not a first-run convenience but *the* world constructor, re-run in full on every publish.

```sync
![[00 End-to-End Timeline Flowchart#^boot-1{seamless:true,title:false}]]
```

A **scheduled table** is a SpacetimeDB table whose rows carry a `ScheduleAt` field; while such a row exists, the host re-fires the reducer named by the table's `scheduled_reducer` attribute on that schedule. [[server/spacetimedb/src/main/lifecycle.rs#init#1|init]] inserts exactly one row into each of the three: `EnemyBehaviorSchedule` at a 100 ms interval (the fixed-timestep enemy simulation — `BEHAVIOR_TICK_DT` in [[server/spacetimedb/src/main/global.rs|global.rs]] matches at 0.1 s), `EnemySpawnSchedule` at 2 s (top-up passes over the biome regions), and `ConsumableEffectSchedule` at `CONSUMABLE_EFFECT_TICK_SECONDS` = 1.0 s (damage-over-time / regen ticks). These three rows are the entire server clock: nothing on the server moves, spawns, or ticks except through them. Deleting a schedule row would stop that subsystem — there is no other timer mechanism in play.

```sync
![[00 End-to-End Timeline Flowchart#^boot-2{seamless:true,title:false}]]
```

Two idempotency patterns share the seeding work, because the tables differ in whether they have a natural key to upsert against. Catalog rows — `TextureEntry`, `EnemyTemplate`, `WorldDef`, `BiomeDef`, `BiomeRegionDef`, `Enchantment`, `MapConfig` — go through the `Seed` trait in [[server/spacetimedb/src/main/seeds.rs#seed#4|seeds.rs]]: find by the string/id key, update if present, insert otherwise. The rule tables (`LayeringRule`, `BaseAdjacencyRule`, `OverlayAdjacencyRule`, `DecorGroundRule`) have only an auto-increment surrogate key, so [[server/spacetimedb/src/main/seeds.rs#seed_layering_rules#1|seed_layering_rules]] and its siblings instead delete every existing row and reinsert the full set — same "republish overwrites, never duplicates" guarantee by different means. The order `init` calls these in matters: [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc#1|internal_generate_world_proc]] looks up the `WorldDef`/`BiomeDef`/`BiomeRegionDef` rows by id, so [[server/spacetimedb/src/main/seeds.rs#seed_world_defs#1|seed_world_defs]] must run before world generation, and the enemy templates must exist before any `BiomeRegion` references them at spawn time. What gets seeded is demo-scale content: three biomes (Grassland 40 / Meadow 30 / Highlands 30 by weight), nine `BiomeRegionDef`s each allowing up to 5 enemies from a uniform Enemy+Archer pool, and — notably — **no decor**: every `BiomeDef` has `decor_configs: vec![]`, so the decor pipeline (ground → overlay → decor passes) runs but emits zero `HexDecor` rows at boot. The mechanism stays built; the content is parked (see the comment block above `seed_world_defs`).

```sync
![[00 End-to-End Timeline Flowchart#^boot-3{seamless:true,title:false}]]
```

The world is a flat hex grid that wraps like a torus — walking off one edge brings you back on the opposite one — and this step is where that geometry becomes rows. [[server/spacetimedb/src/main/admin.rs#internal_add_chunks#1|internal_add_chunks]] is called with the [[server/spacetimedb/src/main/global.rs|global.rs]] defaults: `DEFAULT_CHUNK_HEX_RADIUS` = 2 (each chunk is a hex of radius 2 measured in tiles, i.e. `hex_area(2)` = 19 tiles via `Hex::range_count`), `DEFAULT_CHUNK_COLS` = `DEFAULT_CHUNK_ROWS` = 6, `DEFAULT_HEX_OUTER_RADIUS` = 48.0 world units per tile. For each of the 36 chunks it computes the chunk's center tile with [[server/spacetimedb/src/world/hex.rs#chunk_center_hex#1|chunk_center_hex]] and its stable id with [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]] (a bijective spiral ordering of the axial chunk coordinates, so chunk ids are small dense integers the AOI views can range over), then inserts one empty `BuildingTile` per tile — 36 × 19 = 684 rows that are the base-building substrate (`place_building` later writes into them). It finishes by deriving the two **lap vectors**: the world-space displacement of crossing the whole grid once along each axis, computed as `hex_to_world` of the chunk centers `(cols, 0)` and `(0, rows)`, and upserts them with the grid dimensions into the singleton `MapConfig` row (id 0). Every wrap-aware distance on both sides — server Voronoi bucketing, client movement interpolation — is computed "mod the lap vectors," which is why the client also subscribes `MapConfig` (conn-5) instead of hardcoding the grid size. The comment on `DEFAULT_CHUNK_COLS` records why the map is 6×6 and not the original 22: with the ~45-enemy cap, a ~9000-unit map felt empty; ~2500 units is walkable.

```sync
![[00 End-to-End Timeline Flowchart#^boot-4{seamless:true,title:false}]]
```

`init` passes the literal seed `0` to [[server/spacetimedb/src/world/terrain/mod.rs#internal_generate_world_proc#1|internal_generate_world_proc]], and the whole pipeline threads one mutable `draw` counter through [[server/spacetimedb/src/world/prng.rs#pick_index#1|pick_index]] — every random choice consumes the next draw — so the published world is bit-for-bit reproducible: same code, same map, every publish. The pipeline itself (`run_generation`, shared with the admin manual-generation front-end) is: clear any prior geometry, distribute the `BIOME_VORONOI_SEED_BUDGET` = 16 Voronoi seed points proportionally to biome weights via [[server/spacetimedb/src/world/terrain/voronoi.rs#distribute_biome_seeds#1|distribute_biome_seeds]], bucket every one of the 684 world hexes under its nearest seed (lap-vector-aware, so biome regions can straddle the wrap seam), then per biome draw one region seed per `RegionWeight` entry, insert the `BiomeRegion` rows the 2-second spawn tick later populates with enemies, and finally per hex emit six `TriangleTile` wedge rows through ground → overlay → decor passes that consult already-written neighbors for the adjacency/layering rules. The pass order and the draw-counter discipline are the determinism: reorder two passes and every downstream random pick shifts. Terrain pass internals are [[07 Terrain & World Streaming]]'s subject; from boot's perspective the takeaway is that after `init` returns, the server holds a complete, queryable world before the first client ever connects.

### Client boot: autoloads, and the swapped scene names

```sync
![[00 End-to-End Timeline Flowchart#^conn-1{seamless:true,title:false}]]
```

An **autoload** is Godot's singleton mechanism: a scene the engine instantiates before the main scene and keeps alive across scene changes, reachable from anywhere. [[client/project.godot|project.godot]] registers three — `PhantomCameraManager` (the phantom-camera plugin's own manager), `McpRuntimeAutoload` (editor tooling), and `DatabaseConnector` pointing at [[client/sstdbsdk/DatabaseConnector.tscn|DatabaseConnector.tscn]], a one-node scene whose only job is to attach [[client/sstdbsdk/DatabaseConnector.cs|DatabaseConnector.cs]] to a `Node`. The autoload is a *scene*, not the bare `.cs`, because Godot can only autoload scenes and scripts — and the scene form is what older notes get wrong. Because autoload `_Ready` runs before the main scene's, the connection attempt starts before any UI exists; every consumer of the connection is written to tolerate "already connected" for exactly this reason (see `TableSubscriber.OnRegistered` below). The `run/main_scene` uid resolves to [[client/Scenes/main_menu.tscn|main_menu.tscn]] — and the file names are genuinely swapped relative to their roles: `main_menu.tscn` is the *menu* (root `MainMenu`, a `Control` running [[client/Scripts/Game/LobbyGui.cs|LobbyGui]], three buttons and two show/hide-only panels), while `game.tscn` is the gameplay scene. The only exit from the menu is **Character Slots** → [[client/Scripts/Game/LobbyGui.cs#CharSlotsPressed#1|CharSlotsPressed]] → `ChangeSceneToFile("res://Scenes/game.tscn")`; that transition is lobby-1, covered by [[04 Lobby & Profiles]].

### The connection: `DatabaseConnector`

```sync
![[00 End-to-End Timeline Flowchart#^conn-2{seamless:true,title:false}]]
```

[[client/sstdbsdk/DatabaseConnector.cs#Connect#1|Connect]] builds the `DbConnection` from the exported `Host` (default `http://127.0.0.1:3000`) and `DbName` (`bullethell`). The auth token is how a returning player keeps their identity: the SpacetimeDB SDK persists one token per host on disk, and `Connect` reads/writes it through the SDK's own `AuthToken` helper under a key derived from the host string (`://`, `:`, `/` all replaced by `_`) — *because tokens are signed per host instance*, the comment notes. There is deliberately **no first-party `AuthToken.cs`**; older notes citing one are stale. `_Ready` first scans both `OS.GetCmdlineArgs()` and `OS.GetCmdlineUserArgs()` for a `--pN` argument (via `IsPlayerArg`) and suffixes the token key with `_pN`, so launching several debug instances from the editor gives each a distinct identity instead of them fighting over one token — this is the entire local-multiplayer-testing story at the connection layer. After connecting, `_Process` calls `Conn?.FrameTick()` every frame: the SDK's network thread queues callbacks, and `FrameTick` drains them onto the Godot main thread, which is the only thread allowed to touch the scene tree. `_ExitTree` disconnects cleanly so closing the game fires the server's `client_disconnected` rather than leaving a timed-out ghost.

```sync
![[00 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false}]]
```

The two sides of one connect, spelled out. Client side, [[client/sstdbsdk/DatabaseConnector.cs#OnConnected#1|OnConnected]] caches the `Identity` (SpacetimeDB's stable per-token account id — same token, same identity, same player), persists the (possibly rotated) token, subscribes its own `Username` property to the `LocalLobbyPlayer` view's insert/update events, and only then fires `Connected` — so by the time any consumer reacts to `Connected`, identity is known and username will track the lobby row. Server side, [[server/spacetimedb/src/main/lifecycle.rs#client_connected#1|client_connected]] branches on where the identity already sits: a brand-new identity gets a `LoggedOutPlayer` row (empty username, not admin) plus a default "Knight" `PlayerProfile`, which is the guarantee that the character-select screen is never empty; an identity already in the lobby gets nothing (reconnect while seated — the row survived); an identity already *in the world* hits the bug path described in Known gaps below. Note what does *not* happen here: no stats, no position, no inventory — those are per-profile and are scaffolded at join time (join-2), not at connect time.

### Subscriptions: `TableSubscriber` and the wave model

```sync
![[00 End-to-End Timeline Flowchart#^conn-4{seamless:true,title:false}]]
```

A **subscription** is a standing SQL query the server evaluates for you: it pushes the matching rows once, then pushes every later insert/update/delete that changes the match set. The client never polls; the subscribed row set *is* its picture of the world. `TableSubscriber` is a `Component` (framework base class — [[02 The Component Framework]]) declared inline in [[client/Scenes/game.tscn|game.tscn]] as the first child of the root, and its `OnRegistered` encodes the autoload-ordering fact from conn-1: if `Conn.IsActive` it subscribes immediately, otherwise it defers to the `Connected` signal — both orders work, so the menu-to-game scene change can never race the connection.

```sync
![[00 End-to-End Timeline Flowchart#^conn-5{seamless:true,title:false}]]
```

The waves exist because each screen needs a different slice of the database, and subscriptions are the bandwidth bill. Base (`AllTextures`, `AllItems`, `AllEnchantments`) is static catalog data every screen reads, so it opens first and never closes. Lobby (`LocalLobbyPlayer`, `LocalPlayerProfiles`) is exactly the character-select screen's data, opened alongside base and closed at join. Game (19 tables in [[client/sstdbsdk/TableSubscriber.cs#GameTables#1|GameTables]]) opens at join-1 — [[05 Joining the World]] — and includes `MapConfig`, whose insert/update [[client/sstdbsdk/TableSubscriber.cs#OnConnected#1|TableSubscriber.OnConnected]] mirrors into its `LapQ`/`LapR` `Vector2` properties; those are the client's copy of the torus lap vectors from boot-3, consumed by the movement/interpolation math ([[06 Movement & Position Sync]]) via the `GameManager` facade. The SQL itself is built by [[client/sstdbsdk/TableSubscriber.cs#TableSql#1|TableSql]], which reflects over the generated bindings' `From` API by table *name* and calls `ToSql()`: if a table is renamed server-side, the regenerated bindings drop the method and the lookup throws an `InvalidOperationException` naming the offender at subscribe time, instead of hand-written SQL silently returning zero rows forever. The same `BaseTables`/`LobbyTables`/`GameTables` arrays also feed `TableBinderComponent`'s editor dropdown (`AllSubscribedTables`), so "what is subscribed" has exactly one source of truth. The lobby and game waves keep their `SubscriptionHandle`s (`lobbySub`/`gameSub`) so `UnsubscribeLobby`/`UnsubscribeGame` can retire each wave independently — the join/death transitions that call those are join-1 and end-3.

```sync
![[00 End-to-End Timeline Flowchart#^conn-6{seamless:true,title:false}]]
```

That transcluded step is the whole row-delivery contract for this doc too: everything `TableSubscriber` pulls in reaches game logic exclusively through `TableBinderComponent` children wired inline in the live scenes. The binder mechanics (`LastRow`/`ReplayExistingRows`, signal signatures) are framework territory and documented in [[02 The Component Framework]].

### `GameManager`: the scene-root facade

[[client/Scenes/game.tscn|game.tscn]]'s root node `game` (a `Node2D`) runs [[client/Scripts/Game/GameManager.cs|GameManager]], an `IEntity` with its own `EntityRegistry` that the scene-level components — `TableSubscriber`, `CatalogComponent`, `EntitySpawnerComponent`, the camera/terrain/overlay components — register with as they ready up. It holds no logic of its own; its value is the static facade. Spawned entities (a freshly instanced player puppet, a bullet, a drop) live in their own subtrees with their own component registries and cannot see the scene-level components through the framework's sibling lookup, and walking node paths upward is brittle; so `GameManager` exposes static pass-throughs — `Conn` and `Username` straight to the `DatabaseConnector` autoload, `LapQ`/`LapR` to the registered `TableSubscriber`, `GetItem`/`GetEnchantment`/`GetResPath` to `CatalogComponent`, `GetEnemy`/`EnemyCount` to `EntitySpawnerComponent`. Every static accessor null-guards through `IsInstanceValid(instance)` and the component registry, because during scene teardown the facade can be called after parts of the scene are already freed — a no-op returning an empty default is the designed failure mode once `game.tscn` is gone (see the `EnchantmentsChanged` event's comment).

## Known gaps / stubs

- **`client_connected` mishandles the already-in-world case** ([[server/spacetimedb/src/main/lifecycle.rs#client_connected#1|client_connected]]): if an identity connects while it still has a `LoggedInPlayer` row (e.g. a duplicate login or a stale row after a crash), the reducer logs `log::error!("Player ... connected while already in world — this is a bug.")` and then *enacts* the bug-adjacent behavior anyway — it deletes the `LoggedInPlayer` row and inserts a `LoggedOutPlayer` row, silently dropping the player back to the lobby, instead of rejecting the connection. The only flag that this is unintended is the error message itself; there is no code comment or tracking TODO.
- **Leftover debug prints in the binder plumbing**: [[client/sstdbsdk/TableBinderComponent.cs#Bind#1|TableBinderComponent.Bind]] ends with a `GD.Print($"[TableBinderComponent] DEBUG bound ...")` that fires once per binder per scene load, and [[client/sstdbsdk/TableBinderComponent.cs#HandleInsert#1|HandleInsert]] prints a `DEBUG first insert` line per table. Harmless but noisy; they ship in every session's output.

## Where to go next

With the lobby subscriptions open, the next beat is the character-select screen itself — profiles, usernames, and the XP math behind them: [[04 Lobby & Profiles]]. The third (game) subscription wave and everything it spawns is [[05 Joining the World]]; the terrain rows generated at boot are rendered in [[07 Terrain & World Streaming]].
