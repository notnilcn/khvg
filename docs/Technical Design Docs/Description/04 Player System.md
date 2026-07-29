# 04 Player System

## Assumed knowledge

[[01 Architecture & Sync Model]] — tables, reducers, subscriptions, views, and the base/lobby/game subscription waves are used throughout without re-explaining them. [[02 Entity & Component Framework]] — the archetype-helper pattern (`try_scaffold_profile`/`teardown_profile`) and the client's own component-registration cycle. [[03 World & Hex Grid]] — hex/chunk coordinates, torus wrap (`wrap_world_pos`, `TorusMath.NearestCandidate`), and the AOI chunk-ring mechanism this doc's movement half continues.

## The 30-second version

A player's session has two states, mirrored by two tables that never both hold a row for the same identity at once: `LoggedOutPlayer` (in the lobby, picking a profile) and `LoggedInPlayer` (in the world, attached to one `PlayerProfile`). `PlayerProfile` rows — up to three per player — persist across that boundary; joining the world lazily scaffolds the rest of that profile's rows (`PlayerData`, `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk`) the first time, and resumes them untouched on every later rejoin. Once in-world, the local player moves itself client-side and periodically *reports* its position rather than asking the server to compute it; the server wraps that position onto the torus and tracks which chunk the player is standing in, which is what every "what's near me" view (terrain, other players, enemies, loot) filters against. Remote players and enemies have no local simulation to trust, so both puppet their position by lerping toward whatever the subscription last delivered. Leveling is a simple XP-threshold formula independent of gear; a player's final displayed stats are this doc's base numbers plus whatever equipped items and enchantments add on top, folded together in [[05 Item, Equipment & Enchantment System|05]]'s territory.

## System flowchart

```sync
![[99 End-to-End Timeline Flowchart#^lobby-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^lobby-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-1{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-2{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^join-3{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-4{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-5{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-6{seamless:true,title:false,marker:09.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-7{seamless:true,title:false,marker:10.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^move-8{seamless:true,title:false,marker:11.}]]
```

## Main body

### The login state machine

`require_logged_in`/`require_in_world`/`require_in_lobby`/`is_admin` (`player/methods.rs`) are the guard functions nearly every player reducer opens with — they look the caller's identity up in `LoggedOutPlayer` or `LoggedInPlayer` and reject the call if it's in the wrong state (e.g. `report_movement` requires in-world, `create_profile` requires in-lobby). The two tables are deliberately exclusive: an identity is a row in exactly one of them, never both, and [[01 Architecture & Sync Model|01]]'s `conn-3` (`client_connected`) and `06 Session end`'s `end-2` (`client_disconnected`) are what move a row between the two outside of the lobby/join flow this doc covers. [[player/methods.rs##pub fn require_in_world|require_in_world]] · [[player/methods.rs##pub fn require_in_lobby|require_in_lobby]]

### Two things called "the lobby"

There are two, easy to conflate from file names alone. `LobbyGui` (`Scenes/lobby_gui.tscn`) is the project's actual entry scene — `project.godot`'s `run/main_scene` points at it, not `main.tscn` — and it is pure Godot scene navigation with no SpacetimeDB involvement at all: its "Character Slots" button calls `GetTree().ChangeSceneToFile("res://Scenes/main.tscn")` and nothing else, while its "Server List" and "Settings" buttons only toggle panel visibility over empty, unpopulated containers. None of `LobbyGui`'s three buttons call a reducer. [[01 Architecture & Sync Model|01]]'s whole `conn-1`-`conn-7` sequence — `GameManager`/`ConnectionComponent` booting, the base and lobby subscription waves opening — only begins once a player has clicked past this screen into `main.tscn`. [[LobbyGui.cs##public void CharSlotsPressed|CharSlotsPressed]]

The *other* lobby is `LobbyComponent`, a child of `main.tscn`'s `Main` node ([[02 Entity & Component Framework|02]]'s composition map) — the actual character-select UI, fully wired to `create_profile`/`delete_profile`/`join_world`, covered by `lobby-1`-`lobby-3` and `join-1`-`join-3` above. Where this doc says "the lobby" from here on, it means `LobbyComponent`.

`lobby-1` through `lobby-3` cover the reactive create/delete flow; the one thing worth adding here is that `create_profile`'s validation (name length, 3-profile cap, case-insensitive per-player uniqueness) is the *only* gate — a player can create a second or third profile immediately after their first login, no unlock condition, no cost.

### Joining the world and the starter loadout

`join-2` names `try_scaffold_profile`'s five tables in the abstract; concretely, a first-ever join seeds `PlayerInventory`'s 24 slots into a fixed layout — slot 0 weapon-only, 1-4 hotbar/consumable-only, 5-8 accessory-only, 9-12 armor-only, 13-14 artifact-only, 15-22 general (any type), 23 bag-only — via small per-type constructors (`weapon_slot`, `hotbar_slot`, …) in `player/methods.rs`, then places a starter Bow (weapon), Bread (hotbar), Hat (accessory), Helmet (armor), Skull (artifact), and Bag (slot 23). `PlayerData` starts at level 1 with `hp`/`max_hp` both `BASE_MAX_HP`, and `PlayerPosition`/`PlayerChunk` both resolve to whatever chunk world position `(0, 0)` falls in. What each starter item actually does (`Item.behaviors`/`stat_modifiers`) and how equip/swap/enchant reducers work is [[05 Item, Equipment & Enchantment System|05]]'s territory — this doc's job stops at "these rows get created." [[player/methods.rs##pub fn try_scaffold_profile|try_scaffold_profile]]

`LocalPlayer.cs` keeps its own client-side mirror of the slot layout — `EquippedWeapon`/`HotbarSlots`/`AccessorySlots`/`ArmorSlots`/`ArtifactSlots`/`GeneralSlots` each hardcode the same index ranges the server constructors above encode (0; 1,4; 5,4; 9,4; 13,2; 15,8) — and exposes `ResolveSlotAt`/`GetSlotItemId` for the inventory UI ([[05 Item, Equipment & Enchantment System|05]]'s `SlotComponent`/`ItemSidebarComponent`) to read. [[LocalPlayer.cs##public ResolvedSlot ResolveSlotAt|ResolveSlotAt]]

### Stats and leveling

`PlayerData.xp`/`.level` and `PlayerStats`' six stats are driven by plain formulas, independent of any item: `xp_for_level(level)` is a triangular number (`100 * (level-1) * level / 2`, so level 2 needs 100 XP, level 3 needs 300, level 4 needs 600, …) and `compute_level(total_xp)` walks levels upward while the next threshold is still affordable. `compute_base_stats(level)` gives all six stats `10 + (level - 1)` — a flat `+1` per level, identical across strength/wisdom/dexterity/defense/vitality/speed — and `compute_base_max_hp(level)` is `BASE_MAX_HP + (level - 1) * HP_PER_LEVEL` (100 base, +5 per level). [[player/methods.rs##pub fn xp_for_level|xp_for_level]] · [[player/methods.rs##pub fn compute_level|compute_level]] · [[player/methods.rs##pub fn compute_base_stats|compute_base_stats]]

`internal_gain_xp` adds XP, recomputes the level, and — if the level actually changed — heals by exactly `HP_PER_LEVEL` per level gained rather than fully restoring HP, so leveling up while hurt doesn't waste or over-grant healing; it's the trigger for `recompute_stats` (below) whenever a level change happened. This function is called from combat's kill-XP grant ([[07 Combat & Damage Math|07]]'s territory) — nothing in the player module itself calls it, since nothing here is the source of XP. [[player/methods.rs##pub fn internal_gain_xp|internal_gain_xp]]

The numbers above are only the *base*. `recompute_stats` — which `try_scaffold_profile` calls once on first creation and every inventory-mutating reducer calls afterward — starts from `compute_base_stats`/`compute_base_max_hp` and folds in every equipped item's and socketed enchantment's `stat_modifiers`, plus any active consumable buffs, before writing the final `PlayerStats` row. That fold (flat-vs-mult stacking, which slots count as "equipped," `Hp` as a modifiable stat) is [[05 Item, Equipment & Enchantment System|05]]'s subject — this doc only establishes what the *base* is before items touch it. [[player/methods.rs##pub fn recompute_stats|recompute_stats]]

### The client-side stat/hp mirror

`StatsComponent` (`local_player.tscn`/`default_enemy.tscn`, [[02 Entity & Component Framework|02]]'s composition map) holds one `Stat` resource per `StatKind` and never computes a value itself — `LocalPlayer.ApplyStats`/`ApplyData` push each field of a freshly-arrived `PlayerStats`/`PlayerData` row in via `SetFromServer`, which only emits `StatChanged` on an actual change. `StatKind.Hp` is registered specially: `LocalPlayer._Ready` calls `StatsComponent.RegisterStat(StatKind.Hp, HealthComponent.Health)`, listing the *same* `Stat` instance `HealthComponent` (combat's territory, [[07 Combat & Damage Math|07]]) already owns, so a UI reader asking `StatsComponent` for HP and one asking `HealthComponent` directly see one live value instead of two independently-updated copies — comedot's shared-Stat pattern. [[StatsComponent.cs##public void SetFromServer|SetFromServer]] · [[LocalPlayer.cs##private void ApplyStats|ApplyStats]]

### Movement and AOI: the player half

`move-1` through `move-3` ([[03 World & Hex Grid|03]]) cover the world-side half of "what's near me" — chunk-ring resolution and the terrain subscription's dirty-flag consumption of it. `move-4` through `move-8` above continue the same numbered sequence with the player-specific half: `report_movement`'s throttled, client-authoritative reporting; the `PlayerPosition`-vs-`PlayerChunk` write asymmetry that AOI views depend on; the local player's own desync-correction path; and the shared `InterpolationComponent` both remote players and enemies puppet through, with `RemotePlayer` additionally extrapolating from a reported velocity that `Enemy` doesn't bother with. Nothing here duplicates [[03 World & Hex Grid|03]]'s torus-wrap math (`wrap_world_pos`, `TorusMath.NearestCandidate`) — every step above just names where the player system calls into it.

One asymmetry worth naming explicitly: the *local* player's movement is client-authoritative (Godot's `MoveAndSlide` moves it before the server ever hears about it — `move-4`), while *remote* players and enemies are entirely server-authoritative from this client's point of view (their only truth is the subscribed row — `move-7`/`move-8`). The same `PlayerPosition` table serves both roles simultaneously; which behavior applies depends only on whether the row belongs to this connection's own identity, checked via `ConnectionComponent.IsLocal` ([[01 Architecture & Sync Model|01]]) in `EntitySpawnerComponent` when deciding whether to spawn a `RemotePlayer` puppet for a given row at all.

### Remote-player rendering

`RemoteVisualComponent` (sibling of `InterpolationComponent` on `non_local_player.tscn`) owns nothing about position — it resolves the puppet's `SpriteFrames` from the profile's `texture_id` through the texture catalog ([[01 Architecture & Sync Model|01]]'s `conn-7` cache, warm well before any remote player ever spawns) and plays `Walk`/`Idle` purely off `InterpolationComponent.Moving`, the boolean that's true whenever the puppet is still more than one unit from its lerp target. It never reads a `PlayerPosition` row itself. [[RemoteVisualComponent.cs##public void SetTexture|SetTexture]]

## Known gaps / stubs

- **`LobbyGui`'s Server List and Settings panels are non-functional stubs.** Both toggle visible/hidden correctly, but neither is ever populated or wired to anything — no server list data source, no settings being read or written. Matches the roadmap's flag on `LobbyGui` more broadly: scene navigation only.
- **Profile creation has no texture picker.** `PlayerProfile.texture_id` is a real, per-profile column, but both `client_connected`'s default profile and `LobbyComponent`'s create-profile panel hardcode `"Knight"` — every profile a player will ever see today looks identical.
- **`OnDeletePressed` removes its panel optimistically**, before any confirmation that `delete_profile` succeeded — it scans sibling panels for a label-text match to the deleted id and frees it in the same call the reducer fires from, rather than reacting to a `LocalPlayerProfiles.OnDelete` row event. Low practical risk today (the only rejection path is an ownership check a client can't trigger against its own panel), but the panel and the server's actual state aren't provably in sync.
- **`report_movement` has no server-side movement validation.** The reducer accepts and stores whatever `(x, y, rotation)` the client reports, with no distance-from-last-position or speed check — nothing stops a modified client from teleporting.
- **`PlayerProfile.aim_assist`/`.lock_on` are dead toggles.** Both are read client-side by `CombatComponent` ([[05 Item, Equipment & Enchantment System|05]]'s territory) but no reducer anywhere ever sets either to `true` — `create_profile` and `client_connected` both hardcode `false`, and there's no setter. The columns and the read path exist; the write path doesn't.
- **The 24-slot layout is duplicated, not shared.** `player/methods.rs`'s per-type slot constructors and `LocalPlayer.cs`'s `HotbarSlots`/`AccessorySlots`/etc. hardcode the identical index ranges independently — a future change to the slot layout on one side and not the other would silently desync client display from server data.

## Where to go next

[[05 Item, Equipment & Enchantment System]] picks up exactly where this doc stops on inventory: the `recompute_stats` fold, equip/swap/enchant reducers, and the `SlotComponent`/`ItemSidebarComponent` UI reading the client-side slot mirror introduced here. [[06 Enemy AI & Bullet Patterns]] is the counterpart to `move-7`/`move-8`'s `InterpolationComponent` puppeting, from the enemy side. [[07 Combat & Damage Math]] covers the kill-XP trigger for `internal_gain_xp` and the death-teardown path that's the other way a `LoggedInPlayer` row disappears.
