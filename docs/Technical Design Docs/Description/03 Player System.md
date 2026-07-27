# 03 Player System

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary, the connection lifecycle (steps 1–9, 90–91) this doc picks up right after, and the "subscriptions are swapped, not merged" note this doc's lobby↔in-world swap is the concrete example of.
- [[02 World & Hex Grid]] for the chunk/AOI mechanism (`PlayerChunk`, `nearby_indices_from_chunk`, the torus wrap) this doc's movement-sync section reuses rather than re-deriving, and for the world-facing half of "joining the world" it already covered.

## The 30-second version

A connected identity is either `LoggedOutPlayer` (in the lobby) or `LoggedInPlayer` (in the world) — never both, never neither, once `client_connected` has run once. Under one identity sit up to 3 `PlayerProfile` rows (name, sprite texture, aim settings), each a completely separate character: its own level, stats, inventory, and position, keyed by `profile_id` rather than `Identity`. The lobby UI is a direct rendering of the `local_player_profiles` view — no separate list state, no separate select step; picking a character *is* calling `join_world` with that profile's id. The first time a given profile ever joins, `try_scaffold_profile` lazily backfills its `PlayerData`/`PlayerStats`/`PlayerInventory`/`PlayerPosition`/`PlayerChunk` rows with starting values (level 1, a Bow/Bread/Hat/Helmet/Skull/Bag loadout, spawn at world origin) — on every later join those checks are no-ops. Movement is client-feel/server-truth: `LocalPlayer` turns WASD into velocity locally for responsiveness and reports its position to the server 10Hz, and the server is the one that actually re-wraps the position across the torus seam and decides whether the player's `PlayerChunk` (and therefore its AOI) needs to move. Leveling is a closed-form function of total XP, not a stored counter that's incremented and compared. Other players in view arrive as `RemotePlayer` nodes that extrapolate between position updates rather than teleporting on each one.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the default-profile half of `client_connected` (step 7), the lobby/profile steps it hands off to, the player-owned half of "joining the world", and the player-owned parts of "movement and AOI" (the world-subscription/terrain-rendering half of those same steps belongs to [[02 World & Hex Grid]] and isn't repeated here).

```sync
![[99 End-to-End Timeline Flowchart#^conn-3{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-1{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-2{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-3{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-4{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-1{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-2{seamless:true,title:false,marker:10.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-3{seamless:true,title:false,marker:11.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:12.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-6{seamless:true,title:false,marker:13.}]]
```

## Login state: logged out vs. logged in, never both

`LoggedOutPlayer` and `LoggedInPlayer` are the two halves of a state machine keyed on `Identity`, not `profile_id` — an identity is in exactly one of them once `client_connected` has run for it once (step 1 above), and every transition between them is a delete-from-one/insert-into-other pair rather than a shared "status" flag on one table. `join_world` (step 7) does the lobby→world move; `leave_world`, hitting 0 HP in `report_hit`, and `client_disconnected` (step 91) all do the reverse. `is_admin` and `username` live on both halves identically (see `set_username`, which patches whichever one currently holds the row) so neither carries information the other lacks — the split is purely about *where* the player currently is, not about their identity or permissions. The single-admin-slot mechanics (`claim_admin`/`release_admin`) that read `is_admin` are [[08 Admin, Debug & World Lifecycle|08's]] concern once it exists.

## Profiles: up to 3 characters per identity

`PlayerProfile` (`profile_id`, `player_id`, `profile_name`, `texture_id`, `aim_assist`, `lock_on`) is the character-select unit — everything downstream (`PlayerData`, `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk`) is keyed on `profile_id`, so two profiles under the same identity share nothing but the login state above them. `create_profile`/`delete_profile` (steps 3–4 above) are both gated by `require_in_lobby` — a profile can only be created or deleted while the identity itself is in the lobby, not mid-session — and `create_profile` additionally caps the count at 3 and rejects a case-insensitive duplicate `profile_name` per identity. See [[reducers.rs##pub fn create_profile|create_profile]].

There's no dedicated "select profile" reducer or table field — a profile only becomes "active" in the sense of `LoggedInPlayer.profile_id` the moment `join_world` succeeds (step 6), and `local_player_active_profile` is just a view filtered to that id. Switching characters mid-session isn't a thing the reducers support today; it's leave-world-then-rejoin-with-a-different-id, same as picking one for the first time.

## Joining the world and profile scaffolding

Steps 6–8 above are `join_world` end to end: ownership/state checks, the login-state move, and `try_scaffold_profile` (step 7). Scaffolding is deliberately idempotent per table — each of the five profile-keyed tables gets its own `if ... .find(&profile_id).is_none()` check before inserting, so a profile that already has, say, `PlayerData` from a previous session but is somehow missing `PlayerChunk` gets only the missing piece backfilled rather than the whole set re-created. The starting inventory loadout is hardcoded in `try_scaffold_profile` itself (`weapon_slot`/`hotbar_slot`/etc. helpers building a fixed 24-slot layout, then five slots seeded with `Bow`/`Bread`/`Hat`/`Helmet`/`Skull`/`Bag`) — the [[04 Item & Equipment System|item system doc]] covers what those slot-shape helpers and item ids actually mean. See [[player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]].

Spawn position is always world origin `(0, 0)`, resolved to whichever chunk contains it via the same `world_to_chunk`/`spiral_chunk_index` helpers [[02 World & Hex Grid]] documents for movement — there's no per-region or per-class spawn point yet, every fresh profile lands in the same spot.

## Leveling and stats

`PlayerData` (`level`, `xp`, `hp`, `max_hp`) and `PlayerStats` (`strength`, `wisdom`, `dexterity`, `defense`, `vitality`, `speed`) are kept deliberately separate: `PlayerData` is "how much progress and how much health," `PlayerStats` is "the derived numbers combat math actually reads." Level isn't a counter that's incremented — `compute_level` re-derives it from total XP on every call via `xp_for_level`, a closed-form triangular-number formula (`100 * (level - 1) * level / 2`) rather than a loop or a stored threshold table, so "what level is 4,300 XP" is always a pure function of the total, never something that can drift out of sync with it. `compute_base_stats`/`compute_base_max_hp` are equally simple: every one of the six stats gets `+1` per level above 1, off a flat base of 10 (and `BASE_MAX_HP + HP_PER_LEVEL` per level for HP). See [[methods.rs##pub fn compute_level|compute_level]].

`internal_gain_xp` (called from `enemy/reducers.rs` on a kill — [[06 Combat & Damage Math|06's]] concern for *when* it fires) adds XP, recomputes the level, and heals by exactly the level-derived max-HP increase rather than fully restoring HP — leveling up while hurt doesn't waste or fully refund the damage taken. `PlayerStats` itself isn't just the base-stat formula, though: `recompute_stats` folds in every equipped item's `stat_modifiers` and any `ActiveConsumableEffect` on top of the level-derived base, which is why this doc stops at the base formula and defers the modifier-folding mechanics to [[04 Item & Equipment System]] — it's triggered by inventory/consumable changes, not by anything in this doc's own flowchart steps.

## Movement sync

Step 9 above is the client half: `LocalPlayer._PhysicsProcess` computes velocity and rotation from input every physics frame — for responsiveness, this never waits on the server — and separately accumulates a timer that calls `ReportMovement` at a fixed ~10Hz, decoupled from the physics frame rate. Steps 10–11 are the server half: `report_movement` re-wraps whatever `(x, y)` the client reported through `wrap_world_pos` (so a client can't report a position outside the canonical range and corrupt the AOI math) and only touches the profile's `PlayerChunk` row if the recomputed chunk actually differs from what's stored — the 10Hz `PlayerPosition` write and the much-rarer `PlayerChunk` write are intentionally two different tables for exactly this reason, covered in more depth by [[02 World & Hex Grid]]'s AOI section. `LocalPlayer` never snaps its own `GlobalPosition` to match the server's echoed-back position directly (`OnPositionUpdate`) — it stays a continuous, unbounded local reference frame, and only hard-corrects if the nearest wrapped copy of the server's report is still far away, i.e. an actual desync rather than a routine seam crossing.

## RemotePlayers: AOI-scoped peers

Step 12 above covers the client side of every *other* player in view. `nearby_remote_players` is filtered by the same `PlayerChunk`-keyed AOI ring [[02 World & Hex Grid]] documents for terrain, so a `RemotePlayer` node only exists while that player is within `DEFAULT_AOI_CHUNK_RADIUS` chunks — `GameManager` spawns and frees the node purely off `NearbyRemotePlayers` insert/delete. Once spawned, a `RemotePlayer` doesn't teleport to each new position update; it extrapolates a `snapVelocity` (derived from that player's own `PlayerStats.Speed`, fetched by a direct `PlayerStats.ProfileId.Find` rather than a subscribed field) forward every frame between updates, using the same wrap-aware `TorusMath.NearestCandidate` trick `LocalPlayer` and `TerrainManager` both use to pick the nearest copy of a wrapped position, with a hard snap reserved for genuine desyncs. See [[RemotePlayer.cs##private void OnPositionUpdate|OnPositionUpdate]].

## Known gaps and stubs

- **`aim_assist`/`lock_on` can be read but never set.** `PlayerProfile.aim_assist`/`lock_on` are consumed client-side (`LocalPlayerCombat.cs` reads them into `_aimAssist`/`_lockOn` on profile load), but every place a `PlayerProfile` is inserted (`create_profile`, `client_connected`'s default Knight) hardcodes both to `false`, and no reducer exists to change them afterward. The fields and their consumer both exist; the setter doesn't.
- **`LobbyGui.cs` isn't wired to any reducer** — already noted in [[01 Architecture & Sync Model]]'s "Known gaps," restated here because it's this doc's flow the dead-end applies to: the actual create/join/delete implementation lives in `GameManager.cs` against the `%CharSlots` UI in `main.tscn`, not in `LobbyGui.cs`/`lobby_gui.tscn`.
- **No character customization.** `create_profile` always passes `"Knight"` as the texture id — every profile looks identical regardless of name; there's no texture picker in the create-profile panel.
- **Profile deletion doesn't react to its own row event.** `OnDeletePressed` (timeline step 5 above) removes the UI slot by scanning children for a matching id label rather than handling a `LocalPlayerProfiles.OnDelete` callback — it happens to work because deletion is always initiated from that same button, but a profile deleted by any other path wouldn't clean up its slot.
- **No mid-session character switch.** Swapping profiles means `leave_world` then `join_world` with a different id — there's no reducer that moves a logged-in identity directly from one profile to another.

## Where to go next

[[04 Item & Equipment System]] picks up immediately where "Joining the world and profile scaffolding" stops short — the inventory slot layout, what equipping actually does to `PlayerStats` via `recompute_stats`, and the loot/pickup flow. [[06 Combat & Damage Math]] is the other immediate branch: what calls `internal_gain_xp`, and the other direction of `report_hit`/`report_enemy_hit` this doc only gestured at via the death→`LoggedOutPlayer` transition.
