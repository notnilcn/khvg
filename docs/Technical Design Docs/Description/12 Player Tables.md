# 12 Player

## 1. Assumed knowledge

Read [[docs/Technical Design Docs/Description/00 Table Map.md|00 Table Map]] (the `# Player` section holds this doc's 18 table entries, `^table-player-inventory-slot` through `^table-player-survival`), [[docs/Technical Design Docs/Description/01 Roadmap.md|01 Roadmap]] (conventions, reading order), [[docs/Technical Design Docs/Description/03 Connection, Subscriptions & Views.md|03 Connection, Subscriptions & Views]] (reducers-are-transactional, the `BaseTables`/`LobbyTables`/`GameTables` subscription waves, the one-binder-per-table pattern), and [[docs/Technical Design Docs/Description/02 The Component Framework.md|02 The Component Framework]] (the `Component`/`IEntity` lifecycle behind every client class named here), plus [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] (the damage sinks that write `PlayerData.hp` and call `kill_player`) and [[docs/Technical Design Docs/Description/10 Item Tables.md|10 Item]] (the catalog rows that fill these slots, and the acquire/activate paths that end here). Maintainer jump-off points, not authority: [[AGENTS.md|root AGENTS.md]], [[server/AGENTS.md|server AGENTS.md]], [[client/AGENTS.md|client AGENTS.md]]. Glossary for newcomers: a *reducer* is a server function a client calls by name that runs transactionally and returns no data — clients learn outcomes only through table/subscription updates; a *view* is a named server-side query the client subscribes to instead of writing SQL; a *profile* is one playable character (`profile_id`, the entity key joining every per-character row) while the *player* is the SpacetimeDB `Identity` holding it; a `.tscn` file is a Godot scene (node tree) authored in the editor, and "wired in `game.tscn`" means binder nodes plus signal connections declared there, not built in code; *AOI* is area-of-interest filtering by `chunk_index` so each client only receives nearby rows.

## 2. The 30-second version

Player owns 18 tables in three bands: identity/lobby (`LoggedInPlayer`/`LoggedOutPlayer`/`PlayerProfile`), the per-character sim state (`PlayerData`/`PlayerStats`/`PlayerStatAllocation`, `PlayerPosition`/`PlayerRotation`/`PlayerChunk`, `PlayerInventorySlot`, `ActiveConsumableEffect`, `LootDrop`, `PlayerKnowledge`), and server-only validation/schedule rows (`ConsumableEffectSchedule`, `PlayerMovementState`, `PlayerDisplacementAllowance`, `PlayerHitRate`, `PlayerSurvival`). Writes run through lobby reducers (`create_profile`/`join_world`/`leave_world`), the 10 Hz `report_movement` clamp, inventory reducers (`swap_slots`/`pickup_drop`/`drop_item`/`use_item`/`activate_ability`/`allocate_stat`/enchant reducers), and the single 1 Hz `tick_status_effects` dispatcher; reads fan out through per-caller `local_player_*` views into `LocalPlayer`'s child components and through chunk-filtered `nearby_*` views into spawned `RemotePlayer`/`Drop` puppets. The client never computes damage or mutates stats — it reports intent and mirrors rows.

## 3. Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I log in / pick a character / go back to the lobby? | `LoggedInPlayer`, `LoggedOutPlayer` | [[#LoggedInPlayer\|LoggedInPlayer]], [[#LoggedOutPlayer\|LoggedOutPlayer]], [[#Cross-table flows\|Cross-table flows]] (join world) |
| Where is my character (name, texture, aim settings)? | `PlayerProfile` | [[#PlayerProfile\|PlayerProfile]] |
| Where are my hp, level, defence, move speed? | `PlayerData` | [[#PlayerData\|PlayerData]] |
| Where are my six stats, and what did I spend points on? | `PlayerStats`, `PlayerStatAllocation` | [[#PlayerStats\|PlayerStats]], [[#PlayerStatAllocation\|PlayerStatAllocation]] |
| How does my position reach other players? | `PlayerPosition` | [[#PlayerPosition\|PlayerPosition]] |
| Why is facing in a separate table? | `PlayerRotation` | [[#PlayerRotation\|PlayerRotation]] |
| Why do views key off chunk instead of position? | `PlayerChunk` | [[#PlayerChunk\|PlayerChunk]] |
| Where is my inventory, and what goes in each slot? | `PlayerInventorySlot` | [[#PlayerInventorySlot\|PlayerInventorySlot]] |
| How do multi-slot ability items (tomes) occupy slots? | `PlayerInventorySlot` | [[#PlayerInventorySlot\|PlayerInventorySlot]] — Changers (span handling) |
| Where do consumable buffs live while active? | `ActiveConsumableEffect` | [[#ActiveConsumableEffect\|ActiveConsumableEffect]] |
| What drives the 1 Hz tick? | `ConsumableEffectSchedule` | [[#ConsumableEffectSchedule\|ConsumableEffectSchedule]] |
| How do dropped items appear / expire / get picked up? | `LootDrop` | [[#LootDrop\|LootDrop]] |
| What stops speed-hacked movement reports? | `PlayerMovementState`, `PlayerDisplacementAllowance` | [[#PlayerMovementState\|PlayerMovementState]], [[#PlayerDisplacementAllowance\|PlayerDisplacementAllowance]] |
| What stops hit-report spam? | `PlayerHitRate` | [[#PlayerHitRate\|PlayerHitRate]] |
| What does my character know / what renders as ??? | `PlayerKnowledge` | [[#PlayerKnowledge\|PlayerKnowledge]] |
| Where is regen / out-of-combat attrition state? | `PlayerSurvival` | [[#PlayerSurvival\|PlayerSurvival]] |
| What happens when I die? | (`kill_player` teardown) | [[#Cross-table flows\|Cross-table flows]] (death pipeline) |
| How is an ability cast validated and dispatched? | (shared helpers) | [[#Cross-table flows\|Cross-table flows]] (ability activation) |
| Where is damage actually computed? | — | [[docs/Technical Design Docs/Description/08 Combat Tables.md\|08 Combat]] (this doc only calls its sinks) |
| What do the items/enchantments in my slots do? | — | [[docs/Technical Design Docs/Description/10 Item Tables.md\|10 Item]] (catalog, staging, folds) |
| Why do I see other players' hits corroborated? | `PlayerHitFlag` | 04 Anticheat (witness flags; swept by this doc's `tick_status_effects`) |
| How do statuses / charges / zones attach to my character? | `ActiveStatusEffect`, `AbilityCharge`, `GroundZone` | 13 Status (armed from this doc's `apply_ability_effect`) |

## 4. Flowcharts

- [[flowcharts/main-player.canvas]] — the composed player flow (manifest flow `player` in `flowcharts/flows.json`, 22 member canvases covering lobby, data, inventory, movement, spawning, and the server module; link stays unresolved until the Obsidian recompose runs — do not hand-create the canvas). The old topic-split files `flowcharts/main-lobby.canvas`, `flowcharts/main-player-data.canvas`, and `flowcharts/main-movement.canvas` are retired splits merged into this flow — none exists on disk now, and if a compose leaves any of them behind, delete it by hand (stale mains are never pruned automatically; see `01 Roadmap.md` phase-0 notes).
- Deep dives (paths verified present on disk):
  - [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — the whole server module (all five files).
  - [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Movement_subfolder/Movement_subfolder.canvas]] — position sync + interpolation (local echo/reconciliation, remote dead reckoning).
  - [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]] — inventory UI + activation (slots, sidebars, optimistic casts, grapple tween).

## 6. Tables

### PlayerInventorySlot

```sync
![[00 Table Map#^table-player-inventory-slot{seamless:true,title:false,marker:01.}]]
```

#### Shape

One row per inventory slot: auto-inc `slot_id` primary key, btree on `profile_id` plus the composite `(profile_id, slot_index)` point-read index (`by_profile_slot`, used by [[server/spacetimedb/src/player/methods.rs#get_slot|get_slot]]). A slot's acceptance set is derived from its `role` (`SlotRole`: Weapon/Hotbar/Accessory/Armor/General/Bag/Ability) by [[server/spacetimedb/src/player/methods.rs#slot_allowed|slot_allowed]] — General accepts everything, every other role accepts exactly its matching `EquipSlot` — *because* roles replace the old "equipped iff allowed_slots.len() == 1" inference. Indices are load-bearing on both sides: `0` weapon, `1–4` hotbar consumables, `5` accessory, `6` armor, `7–30` general backpack, `31` bag, `32–37` ability (`ABILITY_SLOT_START` = 32, `ABILITY_SLOT_COUNT` = 6 in [[server/spacetimedb/src/main/global.rs|global.rs]]). Ability items span `Item.slot_cost` contiguous ability cells: the head row holds `item_id`, follower rows carry `occupied_by = Some(head_index)` and never hold an item. The row also carries per-slot runtime state — `cooldown_until`, `charges` (`u32::MAX` = unlimited), `active_toggle` — *because* ability cooldowns/charges/toggle patterns need a home that isn't replicated on every swap.

#### Changers

Insert of the full 38-slot set happens in [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]] (Bow at 0, Bread at 1, Hat at 5, Helmet at 6, Bag at 31, plus pre-equipped Skull at 32 and Tome of Mending spanning 33–35 via [[server/spacetimedb/src/player/methods.rs#place_span|place_span]]). Every later write goes through [[server/spacetimedb/src/player/methods.rs#update_slot|update_slot]] from: `swap_slots`, `use_item` (clears the hotbar cell), `activate_ability`/`spend_ability` (cooldown + charge spend), `apply_enchantment`/`remove_enchantment` (socket list), `pickup_drop`/`drop_item` (via [[server/spacetimedb/src/player/methods.rs#transfer_item_to_inventory|transfer_item_to_inventory]]), `take_from_chest`, trade `confirm_swap`, and admin `give_item`/`remove_item`. Delete of all rows happens only in [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]] (death and `delete_profile`); `leave_world`/disconnect keep the rows so a re-join resumes the same loadout.

`swap_slots` handling is region-based, not item-based: the span path triggers when source or target is an Ability-role slot. A span move needs `slot_cost` contiguous free cells at the target (source cells count as freed), runtime state travels with the item in both directions, and entering the region from the backpack initializes charges from the item only when the source carried no state. Ability items swapped between non-ability slots are plain single-cell swaps where runtime state travels too, so a backpack shuffle can't launder a cooldown. Stat requirements gate any item entering an equipped role from loose storage (`is_equip_role`: Weapon/Accessory/Armor/Ability). `drop_item` on a follower drops the whole span and stamps the slot's `cooldown_until`/`charges` onto the `LootDrop` row; `use_item` is Hotbar-role only and rejects items with an `Ability` behavior so a span head is never consumed out from under its followers.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player_inventory|local_player_inventory]] per-caller view → `GameTables` wave ([[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]]) → `LocalPlayerInventoryBinder` on [[client/Scripts/Components/Inventory/LocalPlayerInventoryComponent.cs#LocalPlayerInventoryComponent|LocalPlayerInventoryComponent]] (replay on, wired in [[client/Scenes/local_player.tscn|local_player.tscn]]) → the `inventorySlots` dict behind `ResolveSlotAt`/`GetSlotItemId`. Visible ends: the slot grid in [[client/Scripts/Components/Inventory/InventoryPanel.cs#InventoryPanel|InventoryPanel]] (hotbar keys call `UseItem`, ability keys route through `TryActivateAbility`), the hover composition in [[client/Scripts/Components/Inventory/ItemSidebar.cs#ItemSidebar|ItemSidebar]] (with Socket/Remove buttons calling `ApplyEnchantment`/`RemoveEnchantment`), and the effective weapon behind [[client/Scripts/Components/Weapon/CombatComponent.cs#CombatComponent|CombatComponent]] firing (re-resolved on `InventoryChanged` when the fingerprint changes). The grid itself is 39 `SlotComponent` nodes declared inline in `local_player.tscn` (5-slot Hotbar indices 0–4, 9-slot Equipment panel, 25-slot Backpack indices 7–30 plus Bag 31 — indices 0 appears twice with the default export value, which is why only 37 `SlotIndex =` lines are authored): drag/drop reports `SwapSlots`, backpack right-click reports `DropItem`, chest drags branch to `TakeFromChest`, and left-click activation stays commented out (see §9).
- ***Server*** — `equipped_head_slots` / `resolve_effective_weapon` fold these rows for stats and firing; `slot_allowed` derives acceptance from the role; `find_free_span`/`place_span`/`clear_span` manage ability-region occupancy.

### LoggedInPlayer

```sync
![[00 Table Map#^table-logged-in-player{seamless:true,title:false,marker:02.}]]
```

#### Shape

One row per identity currently in the world: `player_id` primary key (the authenticated `ctx.sender()`, never a passed argument), `username`, `is_admin`, and the unique active `profile_id` — the entity key joining every per-character row. Uniqueness on `profile_id` means one character per identity in the world at a time. This row is the "in world" fact itself: [[server/spacetimedb/src/player/methods.rs#require_in_world|require_in_world]] (and `require_logged_in`) just probe it, so every world reducer is gated by this table's presence.

#### Changers

Insert in [[server/spacetimedb/src/player/reducers.rs#join_world|join_world]] (lobby row deleted, this row inserted, then `try_scaffold_profile`). Delete plus move back to ^table-logged-out-player in [[server/spacetimedb/src/player/reducers.rs#leave_world|leave_world]], `client_disconnected`, [[server/spacetimedb/src/player/methods.rs#kill_player|kill_player]] (via `teardown_profile`), and the already-in-world branch of `client_connected` (see §9 — it bounces the player to the lobby instead of rejecting). Update of `is_admin` in `claim_admin`/`release_admin` and of `username` in [[server/spacetimedb/src/player/reducers.rs#set_username|set_username]] (which updates whichever login row exists).

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player|local_player]] per-caller view → `GameTables` wave → `LocalPlayerBinder` on [[client/Scripts/Components/Spawning/EntitySpawnerComponent.cs#EntitySpawnerComponent|EntitySpawnerComponent]] (replay on, wired in [[client/Scenes/game.tscn|game.tscn]]) → `OnLocalPlayerInsert` instantiates the `LocalPlayer` scene (`local_player.tscn`) once and calls `HideLobby()` on the `LobbyComponent` — the join completes when the row arrives, not when the reducer returns, which is why `LobbyComponent` holds the lobby up through a 5 s `JoinTimeoutSeconds` window. Row delete despawns via `OnLocalPlayerDelete`.
- ***Server*** — `require_logged_in` / `require_in_world` guard every world reducer; every AOI view left-semijoins through this table so logged-out ghosts never leak to clients; the regen/attrition pass only ticks these profiles.

### LoggedOutPlayer

```sync
![[00 Table Map#^table-logged-out-player{seamless:true,title:false,marker:03.}]]
```

#### Shape

One row per known identity currently in the lobby: `player_id` primary key, `username`, `is_admin`. No `profile_id` — lobby identities hold up to 3 profiles (see [[#PlayerProfile\|PlayerProfile]]), so the active-character link lives only on the in-world row. [[server/spacetimedb/src/player/methods.rs#require_in_lobby|require_in_lobby]] probes this table, gating all profile management.

#### Changers

Insert in [[server/spacetimedb/src/main/lifecycle.rs#client_connected|client_connected]] (first connect, with a scaffolded "Knight" profile) and on every world-exit path (`leave_world`, `client_disconnected`, `kill_player`, the already-in-world bounce). Delete in `join_world`. Update of flags in `claim_admin`/`release_admin`/`set_username`.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_lobby_player|local_lobby_player]] per-caller view → `LobbyTables` wave → the one sanctioned non-binder read: [[client/sstdbsdk/DatabaseConnector.cs#DatabaseConnector|DatabaseConnector]]'s direct `OnInsert`/`OnUpdate` hooks mirror `Username` (verified lines 107–108). The lobby panels themselves render ^table-player-profile rows, not this row; the world-exit return path re-subscribes the lobby wave (`SubscribeLobby` in `FailJoin`) so the row comes back.
- ***Server*** — `require_in_lobby` gates `create_profile`/`delete_profile`/`join_world`; the single-global-admin-slot check in `claim_admin` scans both login tables.

### PlayerProfile

```sync
![[00 Table Map#^table-player-profile{seamless:true,title:false,marker:04.}]]
```

#### Shape

One playable character per player: auto-inc `profile_id` primary key (the entity key), btree `player_id`, `profile_name` (unique per player via the `by_player_profile_name` composite index, enforced case-insensitively in `create_profile`), `texture_id` (catalog sprite/model id), and aim settings (`aim_assist`, `lock_on`). Never updated after creation — aim toggles notwithstanding, the row is insert/delete only.

#### Changers

Insert in `client_connected` (first "Knight" scaffold) and [[server/spacetimedb/src/player/reducers.rs#create_profile|create_profile]] (lobby-gated, max 3 per player, 1–20 char names). Delete in [[server/spacetimedb/src/player/reducers.rs#delete_profile|delete_profile]] (lobby-gated, ownership-checked) plus the full `teardown_profile` cleanup of every per-character row. Death keeps the profile row — `kill_player` tears down world rows but leaves the character itself intact.

#### Readers

- ***Client*** — Three views. [[server/spacetimedb/src/player/views.rs#local_player_profiles|local_player_profiles]] → `LobbyTables` → the profiles binder on [[client/Scripts/Components/Lobby/LobbyComponent.cs#LobbyComponent|LobbyComponent]] builds one profile panel per row (keyed by `ProfileId` in code so insert replays can't duplicate); Join/Delete buttons call `JoinWorld`/`DeleteProfile`, Create calls `CreateProfile` with the `"Knight"` texture. [[server/spacetimedb/src/player/views.rs#local_player_active_profile|local_player_active_profile]] → `GameTables` → `LocalPlayerActiveProfileBinder` on [[client/Scripts/Components/Visual/LocalPlayerProfileComponent.cs#LocalPlayerProfileComponent|LocalPlayerProfileComponent]] (replay on, `local_player.tscn`) → sets the sprite `SpriteFrames` from `GetResPath2D` (2D mode; hidden in 3D) and the catalog 3D model via `SetModel3DTexture`, then raises `AimSettingsChanged` for `CombatComponent`'s aim assist/lock-on. [[server/spacetimedb/src/player/views.rs#nearby_remote_players_profiles|nearby_remote_players_profiles]] (AOI-filtered through the same chunk set as positions — a right semijoin over the chunk-filtered position scan, since the builder can't filter the right side of a left semijoin) → `GameTables` → [[client/Scripts/Players/Remote/RemotePlayer.cs#RemotePlayer|RemotePlayer]] reads it one-shot via `Iter()` in `_Ready` to skin each puppet (`SetTexture`, plus the catalog 3D model in 3D mode).

### PlayerData

```sync
![[00 Table Map#^table-player-data{seamless:true,title:false,marker:05.}]]
```

#### Shape

One row per profile: `level`/`xp` plus the resolved outputs `hp`/`max_hp`, `defense`, and `base_speed` (world units/sec). `defense` and `base_speed` live here rather than in `PlayerStats` for the same reason `max_hp` does — they are modifier-only (gear/buffs), never allocatable, and `report_movement` clamps reported velocity to `base_speed` every report.

#### Changers

Insert in `try_scaffold_profile` (level 1, full hp). Update in [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] (resolution — Hp modifiers fold over `compute_base_max_hp(level)` with `hp` clamped down, Defense over 0 floored at 0, BaseSpeed over `BASE_SPEED` floored at 1.0), [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] (level/xp, healing by the level-derived max-hp increase so leveling while hurt neither fully restores nor wastes health, plus `SKILL_POINTS_PER_LEVEL` = 2 unspent points per level), the combat sinks (hp subtract/heal — the only place damage is computed, see 08 Combat), and admin `change_stats`. Delete in `teardown_profile`.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player_data|local_player_data]] per-caller view → `GameTables` → `LocalPlayerDataBinder` on [[client/Scripts/Components/Data/LocalPlayerDataComponent.cs#LocalPlayerDataComponent|LocalPlayerDataComponent]] (replay on, `local_player.tscn`) → mirrors hp into the sibling `HealthComponent` via `SetFromServer` (health bar, `HealthDidZero` death) and exposes `Level`/`Defense`/`BaseSpeed` for `StatsSidebar` and the movement clamps (`PositionSyncComponent.CurrentSpeed` scales `BaseSpeed`, and `LocalPlayer` integrates at it).
- ***Server*** — `report_movement` clamps speed to `base_speed` (tightened by the minimum held `SelfSlow` factor); damage math reads `defense`; `xp_for_level`/`compute_level` do the level curve in `u64` (the old `u32` formula overflowed past ~level 2930).

### PlayerStats

```sync
![[00 Table Map#^table-player-stats{seamless:true,title:false,marker:06.}]]
```

#### Shape

The six resolved allocatable stats — strength/wisdom/dexterity plus the damage_dealer/supporter/artisan temperaments (mechanically identical to attributes) — keyed by `profile_id`. Hp/Defense/BaseSpeed resolve into ^table-player-data instead and have no field here *because* they are modifier-only; `PlayerStats` is the recomputed output while ^table-player-stat-allocation is the input.

#### Changers

Insert in `try_scaffold_profile` (via the first `recompute_stats`, which inserts when the row is missing). Update in `recompute_stats` only — no reducer writes this table directly; `allocate_stat`, `internal_gain_xp`, equipment/enchant/buff changes all funnel through the recompute. Delete in `teardown_profile`. The resolution rules, all living in `recompute_stats` (`methods.rs`, `fold_allocation` → `fold_equipment` → `fold_buffs` into per-`StatKind` `(flat, mult)` accumulators, resolved as `(base + flat) × (1 + mult)`): mults are additive, never compounding (two +10% sources give +20%); "equipped" is an explicit `SlotRole` check — Weapon/Accessory/Armor heads contribute at scale 1.0, Ability heads scale by `ABILITY_POSITION_MULTIPLIERS[head − 32]` (`[1.5, 1.25, 1.1, 1.0, 0.9, 0.8]`, first ability stronger), span followers never contribute, and enchantment *behaviors* (as opposed to stat modifiers) are never position-scaled; allocated points fold into flat so gear mults scale them; innate enchantments apply exactly like socketed ones.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player_stats|local_player_stats]] per-caller view → `GameTables` → `LocalPlayerStatsBinder` on `LocalPlayerDataComponent` → the sibling `StatsComponent` mirrors behind `StatsSidebar`'s six stat lines and `+` buttons (raised via `RaiseStatsChanged` on `LocalPlayer`, which UI readers subscribe to instead of the component).
- ***Server*** — `compute_player_damage` scales weapon damage off strength (×(1 + str×0.002), divided among pellets); `check_stat_requirements` gates equipping against these resolved values; ability `Spell` damage scales off the ability's `ScalingStat` the same way.

### PlayerStatAllocation

```sync
![[00 Table Map#^table-player-stat-allocation{seamless:true,title:false,marker:07.}]]
```

#### Shape

The allocation *input* behind ^table-player-stats: per-stat assigned points for the same six stats plus `unspent_points`, keyed by `profile_id`. Split from the resolved output *because* every recompute must re-fold assigned points under gear mults — storing only the resolved values would bake mults into the base on every equipment change.

#### Changers

Insert in `try_scaffold_profile` (manually created profiles start at zero; the auto-created "Knight" starts at 999 per stat for testing — allocation is the recompute input, so those survive every later recompute as `BASE_STAT + 999`). Update in [[server/spacetimedb/src/player/reducers.rs#allocate_stat|allocate_stat]] (moves points from `unspent_points` into one stat, then recomputes; rejects zero-point requests, overdrafts, and Hp/Defense/BaseSpeed — "Hp, Defense and BaseSpeed come from gear, not allocation") and `internal_gain_xp` (grants per level), plus admin `change_stats`. Delete in `teardown_profile`.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player_stat_allocation|local_player_stat_allocation]] per-caller view → `GameTables` → `LocalPlayerStatAllocationBinder` on `LocalPlayerDataComponent` → the unspent-points line and per-stat `+` buttons in [[client/Scripts/Components/Inventory/StatsSidebar.cs#StatsSidebar|StatsSidebar]] (buttons visible only while `UnspentPoints > 0`, each calling `AllocateStat(stat, 1)`).
- ***Server*** — Read only by `fold_allocation` inside `recompute_stats`; no other server reader.

### PlayerPosition

```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:08.}]]
```

#### Shape

One row per profile: x/y plus movement as an angle + scalar (`movement_direction`/`movement_speed` — speed is the player's current speed, 0 when idle, clamped server-side to the resolved `base_speed`; direction keeps its last value while idle) plus btree `chunk_index` for AOI filtering. Angle+scalar instead of a velocity vector *because* clients reconstruct `velocity = direction × speed` for dead reckoning, and reporting actuals (not facing-derived values) fixed the constant-drift bug.

#### Changers

Insert in `try_scaffold_profile` (spawn is always the world origin — re-joins reset x/y there *because* `leave_world` keeps the row), respawn on re-join after death (the insert path, since `teardown_profile` deleted the row). Update in [[server/spacetimedb/src/player/reducers.rs#report_movement|report_movement]] — the most guarded writer in the module: wrap into the torus first; grapple-allowance reports skip elevation rules; mid-fall reports are ignored with a clock restamp (movement locked while the client tweens the landing); rises past `MAX_WALKABLE_UP_DELTA` reject the claim with a clock restamp (honest clients predict the block — the anti-cheat belt); drops past `MAX_WALKABLE_DOWN_DELTA` become falls (`falling_until` lockout, displacement-budget exemption up to `MAX_FALL_DISPLACEMENT`, `FALL_DAMAGE_PER_LEVEL` × drop past `FALL_SAFE_DROP` = 2, applied last since it can kill); speed clamps into `[0, base_speed]` (tightened by any held `SelfSlow`); displacement clamps along the reported wrap-aware vector to `speed_cap × dt × MOVE_SLACK` (floored at `MIN_REPORT_DISPLACEMENT` = 64, dt capped so idleness can't bank budget) — clamp, not reject, so lag doesn't rubber-band — with one oversized report accepted per live `PlayerDisplacementAllowance`; gross violations record anticheat strikes and escalate to reject+resync at threshold (see 04 Anticheat); radius-wager exits stamp per report; `PlayerChunk` updates only on a real crossing; delta-checked write skips the row when nothing subscribers see changed. Exempt teleport writes go through [[server/spacetimedb/src/player/methods.rs#teleport_player|teleport_player]] (Teleport abilities — wrap + chunk write identical to `report_movement`'s, plus a clock restamp). Delete in `teardown_profile` — rows otherwise persist while logged out as "ghosts" (see §9).

#### Readers

- ***Client*** — Own row via [[server/spacetimedb/src/player/views.rs#local_player_position|local_player_position]] → `GameTables` → `LocalPlayerPositionBinder` on [[client/Scripts/Components/Movement/PositionSyncComponent.cs#PositionSyncComponent|PositionSyncComponent]] (replay on, `local_player.tscn`): the replayed first row is the initial placement; later updates only hard-correct on real desync (nearest wrapped copy farther than `WrapSnapThreshold` = 50 — the `[Desync]` print in §9 lives here), since local motion is input-driven and the server echo must not fight it. Outbound, the same component reports at 10 Hz (`ReportInterval` = 0.1 — load-bearing, remotes rubber-band below it; `-1` selects event-driven edge-only mode) with actual direction+speed, faster screen-rotation reports on change, speed controls (Ctrl+scroll scale, hold-R self-slow ×0.4), and the charge/grapple hooks (`SetExternalSlow`, `RequestMovementReport` with reports suppressed while `ExternalMovementActive`). Remote rows via [[server/spacetimedb/src/player/views.rs#nearby_remote_players|nearby_remote_players]] (own row excluded, logged-out ghosts semijoined out) → `GameTables` → `EntitySpawnerComponent`'s `NearbyRemotePlayersBinder` spawns one `RemotePlayer` puppet (`non_local_player.tscn`) per row, and each puppet's own `NearbyRemotePlayersBinder` feeds [[client/Scripts/Components/Movement/InterpolationComponent.cs#InterpolationComponent|InterpolationComponent]] dead-reckoning targets (velocity reconstructed from the angle+scalar pair; torus-aware via `TorusMath.NearestCandidate`). [[client/Scripts/Components/Bullets/BulletSpawnerComponent.cs#BulletSpawnerComponent|BulletSpawnerComponent]] additionally reads both views directly via `Iter()` (`ResolveTargetPosition`) to aim targeted patterns at a player identity's current position.
- ***Server*** — Every AOI view builds its chunk set from ^table-player-chunk, then filters *this* table's `chunk_index`; enemy sim, trade range, zone membership, `report_hit`'s proximity check (`HIT_VALIDATION_RADIUS`), and [[server/spacetimedb/src/player/methods.rs#players_near_point|players_near_point]] (logged-in only — AoE ally targeting) all read positions.

### PlayerRotation

```sync
![[00 Table Map#^table-player-rotation{seamless:true,title:false,marker:09.}]]
```

#### Shape

One row per profile: `screen_rotation` (camera/facing angle) plus `player_id` and a `chunk_index` mirroring the position row. Split out of `PlayerPosition` *because* camera rotation streams on its own faster cadence (~30 Hz vs the 10 Hz movement report) — bundling it would either starve facing updates or rewrite the position row three times as often.

#### Changers

Insert in `try_scaffold_profile`. Update in [[server/spacetimedb/src/player/reducers.rs#report_screen_rotation|report_screen_rotation]] (sub-`ROTATION_REPORT_EPSILON` ≈ 0.5° changes skip the write, but a chunk crossing always rewrites so the mirror stays exact for AOI filtering). Never inserted/deleted elsewhere; delete in `teardown_profile`.

#### Readers

- ***Client*** — Via [[server/spacetimedb/src/player/views.rs#nearby_remote_player_rotations|nearby_remote_player_rotations]] (same AOI filter plus logged-in semijoin; no separate local view — the owner's own row arrives through this view too) → `GameTables` → each `RemotePlayer`'s `NearbyRemotePlayerRotationsBinder` (wired in `non_local_player.tscn`, `PlayerId`-filtered in code) → `InterpolationComponent.SetScreenRotationTarget` (puppet facing). `RemoteVisualComponent` plays Walk/Idle off `InterpolationComponent.Moving`, and the local cursor facing (`LocalPlayer` rotation tracking `GetCursorWorldPosition()`) is what the rotation timer reports.
- ***Server*** — No gameplay reader beyond the view's own chunk mirror; the table exists for the cadence split.

### PlayerChunk

```sync
![[00 Table Map#^table-player-chunk{seamless:true,title:false,marker:10.}]]
```

#### Shape

One row per profile holding the current chunk (`chunk_q`/`chunk_r`). Written only on real chunk crossings — unlike `PlayerPosition`, which updates every 10 Hz report — *because* SpacetimeDB views recompute when their inputs change: keying AOI views off the position row would recompute every movement tick for every subscriber, while keying off this row recomputes only when someone actually crosses a chunk boundary. Never "simplify" an AOI view to read `PlayerPosition`.

#### Changers

Insert in `try_scaffold_profile` (spawn chunk of the origin). Update in `report_movement`/`teleport_player` only when `(cq, cr)` actually changed. Delete in `teardown_profile` (otherwise persists as a ghost alongside the position row).

#### Readers

- ***Client*** — `public` but unsubscribed: no wave carries it and no view exposes it — it exists purely so views key off it server-side instead of ^table-player-position. No visible end by design.
- ***Server*** — [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]] builds every AOI view's chunk set from it (`surrounding_chunk_indices` at `DEFAULT_AOI_CHUNK_RADIUS`, with `MapConfig` fallbacks since a `ViewContext` can't call `MapConfig::load`); the enemy sim reads it per logged-in player for its simulation set.

### ActiveConsumableEffect

```sync
![[00 Table Map#^table-active-consumable-effect{seamless:true,title:false,marker:11.}]]
```

#### Shape

One row per live plain-stat consumable buff: auto-inc `effect_id`, btree `profile_id`, the `StatModifier`, and `remaining` seconds. Only pre-baked stat buffs live here — anything needing per-tick consultation (pools, counters, reduction %, invuln, floors) lives in ^table-active-status-effect instead (see 13 Status); that split is what keeps this table a dumb countdown list.

#### Changers

Insert in [[server/spacetimedb/src/player/methods.rs#apply_consumable_effect|apply_consumable_effect]] (via `use_item`, `activate_ability` Heal/Buff arms, and zone refresh) — Heal arms delegate to `combat::heal_player` instead and insert nothing; Buff arms insert then recompute. Update/decrement and expiry delete in [[server/spacetimedb/src/player/reducers.rs#tick_status_effects|tick_status_effects]] (`remaining -= CONSUMABLE_EFFECT_TICK_SECONDS`, recompute per affected profile). Mass delete in `teardown_profile`.

#### Readers

- ***Client*** — `public` but unsubscribed: no view or wave carries it, so no binder and no visible end — buffs surface through the resolved stats (the `StatsSidebar` numbers move), never through these rows.
- ***Server*** — `fold_buffs` in `recompute_stats` folds every row into the accumulators unscaled; nothing else reads it.

### ConsumableEffectSchedule

```sync
![[00 Table Map#^table-consumable-effect-schedule{seamless:true,title:false,marker:12.}]]
```

#### Shape

The single 1 Hz scheduled-job row driving the whole status tick: auto-inc `scheduled_id` plus `scheduled_at`, wired via `scheduled(tick_status_effects)`. There is exactly one 1 Hz schedule — the status module hangs its `tick` off this dispatch rather than adding a second schedule.

#### Changers

Insert in `init` (and gap-filled in `init_agents`); never updated or deleted in normal operation — interval rows persist (the job row is *not* auto-deleted per fire; the schedule re-arms from the interval).

#### Readers

- ***Client*** — Server-only schedule table (not `public`): never subscribed, no visible end.
- ***Server*** — The runtime fires `tick_status_effects`, which runs the consumable-expiry pass, the `PlayerHitFlag`/`BulletFireAttestation`/`LootDrop` sweeps, then `tick_regen` → `tick_zones` → `status::methods::tick` → `tick_cleanup` (see 13 Status and 04 Anticheat for the downstream passes).

### LootDrop

```sync
![[00 Table Map#^table-loot-drop{seamless:true,title:false,marker:13.}]]
```

#### Shape

One dropped item in the world: auto-inc `drop_id`, `item_id`, x/y, btree `chunk_index`, `expires_at`, optional `dropped_by`, carried `enchantment_ids`, plus the dropped slot's `cooldown_until`/`charges` — ability runtime state survives the trip, and whoever picks it up inherits the residual cooldown. Expiry is enforced, not advisory: `LOOT_DROP_EXPIRY` = 10 s (in [[server/spacetimedb/src/main/global.rs|global.rs]]) is rejected at pickup *and* swept on the tick, so this is not a gap (see §9).

#### Changers

Insert in [[server/spacetimedb/src/player/reducers.rs#drop_item|drop_item]] (span heads drop whole spans via `clear_span`; expiry stamped as `now + LOOT_DROP_EXPIRY`). Delete in [[server/spacetimedb/src/player/reducers.rs#pickup_drop|pickup_drop]] (after `expires_at` rejection, the wrap-aware `INTERACT_RADIUS` + territory-permission gate in `validate_interact`, and `transfer_item_to_inventory` with the carried runtime state — ability items auto-equip into the first free span when stat requirements pass, else loose backpack storage; pickup also auto-discovers the item + enchantments) and on `expires_at` in `tick_status_effects`. Never updated.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#nearby_loot_drops|nearby_loot_drops]] AOI view → `GameTables` → `NearbyLootDropsBinder` on `EntitySpawnerComponent` (replay on, `game.tscn`) → `OnDropInsert` spawns one [[client/Scripts/Items/Drop.cs#Drop|Drop]] node (`drop.tscn`: catalog sprite from `GetResPath2D` — hidden in 3D mode, where no drop model exists yet but pickup still works — plus `PickupComponent`) per row at its x/y; `OnDropDelete` despawns. The area touch reports `PickupDrop` after the pickup-lock timer, so the row lifecycle *is* the drop lifecycle on screen.
- ***Server*** — `pickup_drop`'s range/expiry gates; nothing else reads drops.

### PlayerMovementState

```sync
![[00 Table Map#^table-player-movement-state{seamless:true,title:false,marker:14.}]]
```

#### Shape

Server-only per-profile movement bookkeeping: `profile_id` primary key, `last_report` timestamp (the clock behind the displacement-clamp budget), and `falling_until` (downhill-fall lockout — while in the future, position claims are ignored while the client tweens the landing). Not `public` — clients never read it; the pull is client-tweened and the server only keeps the clocks.

#### Changers

Insert/update in `report_movement` (clock restamp every report, including early-return paths so the budget never balloons while pushing into cliffs or mid-fall) and `teleport_player` (restamp — teleports are exempt from the clamp, and the next report's budget measures from the teleport). Delete in `teardown_profile`.

#### Readers

- ***Client*** — Server-only (not `public`): never subscribed, no visible end. The only client-observable effect is second-order — clamped echoes re-assert the stored row through `PositionSyncComponent`'s desync snap.
- ***Server*** — The per-report budget (`base_speed × dt × MOVE_SLACK`, dt capped, floored at `MIN_REPORT_DISPLACEMENT`) and the `falling_until` movement lockout, plus the monotonicity/gross-violation strike inputs.

### PlayerDisplacementAllowance

```sync
![[00 Table Map#^table-player-displacement-allowance{seamless:true,title:false,marker:15.}]]
```

#### Shape

Server-only one-shot Grapple window: `profile_id` primary key, `max_distance` (the scaled grapple range), `expires_at` (`GRAPPLE_ALLOWANCE_SECONDS` = 1.0 s). One row grants exactly one oversized movement report — *because* the grapple pull is client-tweened and client-authoritative, and without this the displacement clamp would rubber-band every legitimate grapple.

#### Changers

Insert/update on Grapple cast in `apply_ability_effect` (position-multiplier-scaled range). Delete on use (consumed by the landing report), on expiry contact in `report_movement` (an expired row is swept when a clamped report touches it), and in `teardown_profile`. A live row also exempts the report from elevation rules entirely — grappling across cliffs is a feature.

#### Readers

- ***Client*** — Server-only (not `public`): never subscribed, no visible end. The pull itself is the client tween (`StartGrappleTween` over 0.25 s with `ExternalMovementActive` suspending input and timed reports, reporting exactly twice — tween start and end — so mid-tween reports can't burn the one-shot allowance).
- ***Server*** — Only `report_movement`'s clamp path reads it.

### PlayerHitRate

```sync
![[00 Table Map#^table-player-hit-rate{seamless:true,title:false,marker:16.}]]
```

#### Shape

Server-only 1-second fixed-window token bucket per profile with separate `outgoing`/`incoming` counters plus `window_start`. Split directions *because* the two self-reported hit reducers have different honest rates: outgoing weapon hits scale with fire rate × shot count, incoming bullet-hell hits are capped flat.

#### Changers

Insert/update in [[server/spacetimedb/src/player/methods.rs#hit_rate_allow|hit_rate_allow]] on every reported hit (window resets once a second has elapsed; the row persists either way so the reset isn't lost on a rejected report). Delete in `teardown_profile`.

#### Readers

- ***Client*** — Server-only (not `public`): never subscribed, no visible end.
- ***Server*** — Caps `report_enemy_hit` (in `enemy/reducers.rs`) at weapon rate × effective shot count × `HIT_RATE_SLACK` and `report_hit` at `INCOMING_HIT_RATE_CAP` = 30/s — generous for real bullet-hell play, but bounding hit-spam and the StoreIncoming farming loop's input rate without touching damage math.

### PlayerKnowledge

```sync
![[00 Table Map#^table-player-knowledge{seamless:true,title:false,marker:17.}]]
```

#### Shape

One row per profile: auto-filled `discovered` ids plus explicitly unlocked `acquired` ids (acquired implies discovered). The BitCraft `knowledge_secondary_state` pattern translated: `discover_ids` fills `discovered` on pickup/observe, `acquire_knowledge` moves ids into `acquired`, and `knowledge_state` returns 0/1/2 (unknown/discovered/acquired) with unknown items rendering as ??? client-side.

#### Changers

Insert in `try_scaffold_profile` (and lazily in `discover_ids` on first use). Update in `discover_ids` (auto-discover on `pickup_drop`/`take_from_chest`/observe) and in [[server/spacetimedb/src/player/reducers.rs#discover_knowledge|discover_knowledge]] / [[server/spacetimedb/src/player/reducers.rs#acquire_knowledge|acquire_knowledge]] (explicit observe/unlock, 1–32 ids; acquiring an undiscovered id discovers it first). Delete in `teardown_profile`. Admin [[server/spacetimedb/src/player/reducers.rs#set_ability_knowledge|set_ability_knowledge]] writes the *gate* side table (^table-ability-knowledge-requirement), not this row.

#### Readers

- ***Client*** — Via the [[server/spacetimedb/src/player/views.rs#local_player_knowledge|local_player_knowledge]] per-caller view → `GameTables` → subscribed but no binder consumes it yet, so no visible end (the `ItemSidebar` ??? masking is planned — see §9).
- ***Server*** — Ability knowledge gates consult `acquired` in `resolve_ability_slot` (via `knowledge_state` < 2 = missing; no row = no gate, backward compatible).

### PlayerSurvival

```sync
![[00 Table Map#^table-player-survival{seamless:true,title:false,marker:18.}]]
```

#### Shape

Server-only per-profile survival sim: `last_damage_at` (stamped on every hit via `intercept_incoming`), `attrition_ticks` (consecutive ticks without relief), `last_regen_at`. The BitCraft `player_regen`/`starving` pattern: regen only applies after the no-damage delay, and past the attrition grace the pressure row deals damage through the shared damage sink — never direct HP writes.

#### Changers

Insert in `try_scaffold_profile`. Update (stamp) in `intercept_incoming` on every hit and in the regen pass of the 1 Hz tick (`tick_regen`, which the `tick_status_effects` dispatcher calls). Delete in `teardown_profile`.

#### Readers

- ***Client*** — Server-only (not `public`): never subscribed, no visible end — regen/attrition surface as `PlayerData.hp` movement.
- ***Server*** — The regen/attrition pass only ticks `LoggedInPlayer` profiles, so logged-out characters neither regen nor starve.

## 7. Files — pointer deep dive

File order follows [[server/spacetimedb/src/player/pointers.md|pointers]] (generated index — regenerate, never hand-edit).

### methods.rs

Guards, math, archetypes, and slot helpers — no `#[reducer]` here (callable only server-side), but almost every reducer in the module funnels through it. Guards: `require_logged_in`/`require_in_world`/`require_in_lobby` (presence probes on the login tables) and `is_admin` (either login row). Math: `xp_for_level`/`compute_level`/`compute_base_max_hp`, `hit_rate_allow` (the token-bucket behind ^table-player-hit-rate), spell geometry (`spell_shape_contains`/`spell_targets`/`nearest_lap_copy`, unit-tested in-file), and the `recompute_stats` fold trio (`fold_allocation`/`fold_equipment`/`fold_buffs` — see [[#PlayerStats\|PlayerStats]] for the rules). Archetypes: `try_scaffold_profile` (idempotent per-row inserts: data, 38 slots + pre-equipped abilities, allocation with the Knight-999 testing exception, stats via recompute, survival, knowledge, origin reset of position/chunk, rotation) and `teardown_profile` (the one shared cleanup list — data, stats, allocation, all slots, position, rotation, chunk, movement state, displacement allowance, hit rate, strike row, knowledge, survival, channeled actions, consumable effects, plus the status/zone purges), with `kill_player` (teardown + lobby move, keyed off the passed `LoggedInPlayer` since scheduled drains have no sender) as the single death entry combat calls. Slot helpers: `slot_allowed`/`get_slot`/`update_slot`, `ability_position_multiplier`, `find_free_span`/`place_span`/`clear_span`, `check_stat_requirements`, `find_profile_by_name`, `store_item_in_inventory` + `transfer_item_to_inventory` (the single acquire entry — outside callers must not pair store + recompute by hand), `validate_interact` (the shared range + territory-permission gate), `is_storable`, and the knowledge trio (`knowledge_state`/`discover_ids`). Cross-module callers per pointers: combat (`internal_gain_xp`, `kill_player`), chest/trade (`transfer_item_to_inventory`), item/main/admin (`update_slot`, `clear_span`, `recompute_stats`, guards), status (`players_near_point`), anticheat/world (`require_in_world`, `require_logged_in`).

### mod.rs

Module declarations only (`tables`, `methods`, `reducers`, `views`). No logic; nothing outside the folder uses it directly.

### reducers.rs

Every client-callable entry point, all `Err`-not-panic. Lobby/identity: `set_username` (1–20 chars, either login row), `create_profile`/`delete_profile` (lobby-gated, 3-profile cap, case-insensitive uniqueness), `join_world`/`leave_world` (see [[#Cross-table flows\|Cross-table flows]]). Movement: `report_movement` (the validation stack — see [[#PlayerPosition\|PlayerPosition]]), `report_screen_rotation`, `report_hit` (rate-limited, attested against `BulletFireAttestation` with pellet-pool decrement and a `HIT_VALIDATION_RADIUS` proximity check — damage is the tick-stamped value, never client-supplied). Inventory: `pickup_drop`/`drop_item`/`swap_slots`/`use_item` (see [[#PlayerInventorySlot\|PlayerInventorySlot]]), `activate_ability` (see [[#Cross-table flows\|Cross-table flows]]), `set_slot_toggle` (weapon slot only, ≥ 2 toggle options, range-checked against `weapon_toggle_options`), `allocate_stat`, `apply_enchantment`/`remove_enchantment` (allowed-slots gate, socket-cap gate, duplicate gate), knowledge (`discover_knowledge`/`acquire_knowledge`, admin `set_ability_knowledge`), and `tick_status_effects` (the 1 Hz dispatcher — see [[#ConsumableEffectSchedule\|ConsumableEffectSchedule]]). Shared ability helpers factored for the charge reducers in `status/reducers.rs`: `resolve_ability_slot` (validation preamble), `apply_ability_effect` (per-effect dispatch — Heal/Buff, bullet-control relays, marks, shields, curses, Bash stagger, CastZone, Spell shape damage + visual event, Teleport via `teleport_player`, Grapple allowance stamp), `spend_ability` (OnAbilityUseBuff triggers, then cooldown + charge), `charge_range_multiplier`, `scale_ability_effect`. The client call sites live in `local_player.tscn`-wired components (`InventoryPanel`, `SlotComponent`, `StatsSidebar`, `ItemSidebar`, `PositionSyncComponent`, `CombatComponent`, `LocalPlayerInventoryComponent`), `LobbyComponent`, `DamageReceivingComponent` (`report_hit`), and `PickupComponent` (`pickup_drop`) — each verified in §6 above.

### tables.rs

All 18 table definitions plus the `SlotRole` enum (Weapon/Hotbar/Accessory/Armor/General/Bag/Ability — the acceptance axis). Index notes that matter: `by_profile_slot` composite for point slot reads, `by_player_profile_name` composite for case-insensitive profile lookup, btree `chunk_index` on position/rotation/drops for AOI scans, `profile_id` PKs joining the archetype. Visibility split by construction: the seven `public`-but-unsubscribed or server-only rows (`PlayerChunk`, `ActiveConsumableEffect`, `PlayerMovementState`, `PlayerDisplacementAllowance`, `PlayerHitRate`, `PlayerSurvival`, `ConsumableEffectSchedule`) have no client path, and the scheduled table carries `scheduled(tick_status_effects)`.

### views.rs

Fourteen views plus the two shared AOI helpers. Per-caller views (sender → `LoggedInPlayer.profile_id` → filter, `u64::MAX` sentinel when logged out): `local_player`, `local_lobby_player`, `local_player_profiles`, `local_player_active_profile`, `local_player_data`, `local_player_stats`, `local_player_stat_allocation`, `local_player_inventory`, `local_player_position`, `local_player_knowledge`. AOI views (chunk set from [[server/spacetimedb/src/player/views.rs#nearby_indices_from_chunk|nearby_indices_from_chunk]] → OR-chain over `chunk_index`, since the builder has no `IN`; empty set returns a deliberately-unmatchable sentinel query, never an early return): `nearby_remote_players` (own row excluded + logged-in semijoin), `nearby_remote_player_rotations` (same filter over the rotation table), `nearby_remote_players_profiles` (right semijoin over the same chunk-filtered position scan), `nearby_loot_drops`. The helpers are shared beyond this module: `enemy/views.rs`, `chest/mod.rs`, and `world/building.rs` + `world/views.rs` all build on `nearby_indices` / `nearby_indices_from_chunk`.

## 8. Cross-table flows

**Join world.** `LobbyComponent` (profiles from `local_player_profiles` in the `LobbyTables` wave) calls `JoinWorld` → server deletes the `LoggedOutPlayer` row, inserts `LoggedInPlayer`, and `try_scaffold_profile` (re)builds every per-character row with the position reset to the origin → the client swaps waves (`SubscribeGame`/`UnsubscribeLobby`) → the `local_player` row arrives through `GameTables` → `EntitySpawnerComponent.OnLocalPlayerInsert` spawns `local_player.tscn` and hides the lobby. The reducer callback plus the 5 s timeout cover only the failure paths; success is the row arrival. Forgetting the `JoinWorld` call is the recurring failure mode — `LobbyGui` (the main-menu scene) is navigation-only and never calls it (see §9).

**Death pipeline (`kill_player`).** Combat's sinks own damage but never own death rows: on a killing blow they call `kill_player` with the victim's `LoggedInPlayer` row → `teardown_profile` deletes every per-character row (data, stats, allocation, slots, position, rotation, chunk, movement state, allowance, hit rate, strikes, knowledge, survival, channeled actions, consumable effects, statuses, zones) → the login row is deleted and a `LoggedOutPlayer` row inserted. Death is player-domain and transactional — no deferred death event that could split the kill — and the profile row itself survives, so re-join re-scaffolds the same character. `delete_profile` reuses the same teardown list while the player sits in the lobby.

**Ability activation (shared helpers).** `activate_ability` is three shared steps — `resolve_ability_slot` (ability head, `Ability` behavior, cooldown, charges, knowledge gate; charge-capable items rejected to the hold-to-charge path), `apply_ability_effect` (cursor clamped to `MAX_ABILITY_TARGET_RANGE` = 600 around the server position row; potency scaled by `ABILITY_POSITION_MULTIPLIERS`; per-effect dispatch incl. `teleport_player` and the Grapple allowance stamp), `spend_ability` (enchantment triggers, cooldown, charge) — and `status/reducers.rs`' `begin/release_ability_charge` reuse all three, with charge-scaled range/damage on release. The client mirrors each step: `TryActivateAbility` (pre-checks + reducer + optimistic bullet-control/spell/grapple apply, one-click-one-cast guard cleared by the slot echo), `TryBeginCharge`/`TryReleaseCharge`, and the `IsChargeCapable` mirror of `ability_is_charge_capable` — both sides must change together.

## 9. Known gaps / stubs

- **`client_connected` bounces instead of rejecting.** The already-in-world branch (verified in [[server/spacetimedb/src/main/lifecycle.rs#client_connected|lifecycle.rs]]) purges the player's statuses/zones, deletes the `LoggedInPlayer` row, and inserts a `LoggedOutPlayer` row — dropping the player to the lobby — flagged only by a `log::error!`, not a comment. An unexpected duplicate connection therefore silently kills the world session.
- **Ghost rows while logged out.** `PlayerPosition`/`PlayerChunk` (and movement state) persist across `leave_world`/disconnects; only `teardown_profile` (death, `delete_profile`) deletes them. The enemy sim and every AOI view guard with `logged_in_player` checks rather than cleaning up, so the "ghosts" are load-bearing on those guards.
- **`LobbyGui` is navigation-only.** [[client/Scripts/Game/LobbyGui.cs#LobbyGui|LobbyGui]] (`main_menu.tscn`) never calls `create_profile`/`join_world` — those calls live in `LobbyComponent`; its ServerList/Settings panels are show/hide only.
- **Leftover debug prints.** `LobbyComponent.OnProfileRowInserted` unconditionally prints `Successfully synced a profile…` (verified line 107), and `PositionSyncComponent.OnPositionRowUpdated` prints `[Desync] …` on every hard correction (verified line 109). (The `TableBinderComponent` bind/insert prints are gated behind its `Verbose` export — see 03; the unconditional leftovers are these two plus the `TableSubscriber` wave-applied prints.)
- **Knowledge subscribed with no consumer.** `local_player_knowledge` rides `GameTables` but no binder consumes it — the `ItemSidebar` ??? masking for undiscovered items is planned, not built.
- **Slot left-click activation disabled.** `SlotComponent`'s left-click-to-activate path is commented out (verified [[client/Scripts/Components/Inventory/SlotComponent.cs#SlotComponent|SlotComponent.cs]] line 47) — abilities fire from hotkeys only (also noted in 10 Item §9).
- Explicitly **not** gaps (resolved, do not re-add): `LootDrop.expires_at` is enforced at pickup *and* swept on the tick (`LOOT_DROP_EXPIRY` = 10 s); `nearby_remote_players_profiles` is AOI-filtered through the same chunk set as positions.

## 10. Where to go next

Read [[docs/Technical Design Docs/Description/08 Combat Tables.md|08 Combat]] for the damage pipeline that writes `PlayerData.hp` and calls `kill_player`, then [[docs/Technical Design Docs/Description/10 Item Tables.md|10 Item]] for the catalog rows these slots wear (toggle options, fold order, the staging loop). For status/charge/zone behavior armed by `apply_ability_effect`, read 13 Status; for the movement-validation escalation inside `report_movement`, read 04 Anticheat.

**Phase 2 proposal:** if the final compose leaves `flowcharts/main-lobby.canvas`, `flowcharts/main-player-data.canvas`, or `flowcharts/main-movement.canvas` behind, delete them by hand — they are the retired topic splits merged into the `player` flow (none exists on disk at write time; stale mains are never pruned automatically).
