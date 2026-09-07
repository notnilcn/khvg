# 10 Player Tables

## Assumed knowledge

[[01 Roadmap]] for conventions, [[02 The Component Framework]] for the archetype helpers and `recompute_stats`, [[03 Connection, Subscriptions & Views]] for per-caller/AOI views and the binder checklist. The combat sink (`06 Combat Tables`), status consults (`11 Status Tables`), and catalog (`08 Item Tables`) all read and write these rows; this doc owns the rows themselves.

## The 30-second version

Sixteen tables hold the whole player domain: two login rows (lobby vs world, moved between by join/leave/disconnect/death), the profile plus its data/stats/allocation triple (resolved gear/buff numbers live on `PlayerData`, the six allocatable stats on `PlayerStats`, the spent-points input on `PlayerStatAllocation`), position/rotation/chunk streaming rows (angle+scalar movement, own-cadence facing, chunk-gated AOI keys), one row per inventory slot (39 starter rows with span followers and per-slot cooldown/charges/toggle state), consumable buff rows on the single 1 Hz schedule, loot drops with traveling runtime state, and three server-only validation rows (movement state, grapple allowance, hit-rate bucket). Every world-affecting call runs through `require_in_world` and resolves `profile_id` off the login row; every acquire routes through `transfer_item_to_inventory` + `recompute_stats`.

## Find it fast

| Question / concept | Table | Where |
|---|---|---|
| How do I join the world / leave / die? | `LoggedInPlayer` | [[#LoggedInPlayer]] — join/leave/disconnect/death moves |
| Where is my profile / how are profiles made? | `PlayerProfile` | [[#PlayerProfile]] — Knight starter, create/delete, aim settings |
| Where are level, XP, hp, defence, speed? | `PlayerData` | [[#PlayerData]] — resolved numbers; XP heal rule |
| Where are my six stats? | `PlayerStats` | [[#PlayerStats]] — `recompute_stats` output only |
| How do I spend skill points? | `PlayerStatAllocation` | [[#PlayerStatAllocation]] — `allocate_stat`, unspent gating |
| How does movement reporting/validation work? | `PlayerPosition` | [[#PlayerPosition]] — clamp-not-reject, grapple allowance, elevation falls |
| Why is facing a separate table? | `PlayerRotation` | [[#PlayerRotation]] — own faster cadence, mirrored chunk |
| Why do views key off chunks, not positions? | `PlayerChunk` | [[#PlayerChunk]] — written only on crossings |
| How do inventory slots / spans work? | `PlayerInventorySlot` | [[#PlayerInventorySlot]] — 39 rows, region-based spans, state travels |
| What happens when I use / drop / pick up items? | `PlayerInventorySlot` / `LootDrop` | [[#PlayerInventorySlot]], [[#LootDrop]] — single acquire path |
| How do ability casts validate and dispatch? | `PlayerInventorySlot` | [[#PlayerInventorySlot]] — resolve/spend/dispatch trio |
| How long do consumable buffs last? | `ActiveConsumableEffect` | [[#ActiveConsumableEffect]] — 1 Hz expiry + recompute |
| What stops movement/hit spam? | validation tables | [[#PlayerMovementState]], [[#PlayerDisplacementAllowance]], [[#PlayerHitRate]] |

## Flowcharts

- [[flowcharts/main-player.canvas]]
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Inventory_subfolder/Inventory_subfolder.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Movement_subfolder/Movement_subfolder.canvas]]

## Table changes

```sync
![[00 Table Map#^table-player-inventory-slot{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-logged-in-player{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-logged-out-player{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 Table Map#^table-player-profile{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 Table Map#^table-player-data{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 Table Map#^table-player-stats{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 Table Map#^table-player-stat-allocation{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 Table Map#^table-player-position{seamless:true,title:false,marker:08.}]]
```

```sync
![[00 Table Map#^table-player-rotation{seamless:true,title:false,marker:09.}]]
```

```sync
![[00 Table Map#^table-player-chunk{seamless:true,title:false,marker:10.}]]
```

```sync
![[00 Table Map#^table-active-consumable-effect{seamless:true,title:false,marker:11.}]]
```

```sync
![[00 Table Map#^table-consumable-effect-schedule{seamless:true,title:false,marker:12.}]]
```

```sync
![[00 Table Map#^table-loot-drop{seamless:true,title:false,marker:13.}]]
```

```sync
![[00 Table Map#^table-player-movement-state{seamless:true,title:false,marker:14.}]]
```

```sync
![[00 Table Map#^table-player-displacement-allowance{seamless:true,title:false,marker:15.}]]
```

```sync
![[00 Table Map#^table-player-hit-rate{seamless:true,title:false,marker:16.}]]
```

(The entries above are the complete changer/reader reference. The sections below add Shape plus the reducer-level mechanics the entries summarize.)

## PlayerInventorySlot

### Shape

One row per slot (auto-inc `slot_id`, btree on `profile_id`, composite `(profile_id, slot_index)` point reads): `slot_index`, `role` (`SlotRole`), `item_id`, socketed `enchantment_ids`, and per-slot runtime state — `cooldown_until`, `charges` (`u32::MAX` = unlimited), `active_toggle` — plus `occupied_by` marking span followers. Scaffold deals 39 rows: 0 weapon (`Bow`), 1–4 hotbar (`Bread` in 1), 5 accessory (`Hat`), 6 armor (`Helmet`), 7–30 general, 31 bag (`Bag`), 32–37 ability (`Skull` at 32, `Tome of Mending` spanning 33–35 so activation is testable immediately).

### Changers

Scaffold/teardown bracket everything (see entries above). Day-to-day writes: `swap_slots` (span handling is **region-based, not item-based** — it triggers when source or target is an Ability-role slot; span moves need `slot_cost` contiguous free cells counting source cells as freed; runtime state travels with the item both directions, and entering the region from the backpack initializes charges from the item only when the source carried no state; ability items between non-ability slots swap as plain single cells with state still traveling; **stat requirements gate any item entering an equipped role from loose storage**), `use_item` (Hotbar role only, `Consumable`-behavior only, never an `Ability` carrier), `activate_ability` + charge spend (cooldown + charge on the slot), `set_slot_toggle` (weapon slot only, validated against `weapon_toggle_options` count), `apply_enchantment` / `remove_enchantment` (socket state), and every acquire through `transfer_item_to_inventory` (the single `store_item_in_inventory` + `recompute_stats` path — outside callers never pair them by hand).

### Readers

- ***Client*** — via `local_player_inventory` in the Game wave → `LocalPlayerInventoryComponent` → the 39 inline `SlotComponent`s, hover composition, effective-weapon resolution, and casts: `TryActivateAbility` (cooldown/charge pre-check → `ActivateAbility` with cursor world position → optimistic bullet-control/spell visuals; per-slot `castPendingUntilMs` echo guard = one click, one cast), charge-capable items routing key-down/up to `TryBeginCharge`/`TryReleaseCharge` via `IsChargeCapable`, and the client-tweened grapple (reports at tween start/end only, input suspended via `ExternalMovementActive`). Called from `InventoryPanel` (hotbar keys), `SlotComponent` (drag/drop → `SwapSlots`, clicks, right-click `DropItem`, chest-drop branch → `TakeFromChest`), `StatsSidebar` (`AllocateStat`), `ItemSidebar` (enchant socket/remove), `CombatComponent` (toggle key → `SetSlotToggle`).
- ***Server*** — `slot_allowed` (General accepts everything — backpack stores ability items loose in one cell — every other role its matching `EquipSlot`), `recompute_stats` (Weapon/Accessory/Armor heads at 1.0, Ability heads position-scaled, followers never), `resolve_effective_weapon` / `weapon_toggle_options` (weapon path).

Ability casts share one validation/dispatch/spend trio with the P4 charge path: `resolve_ability_slot` (Ability-role head with an `Ability` behavior, off cooldown, charges left — the item is never consumed), `apply_ability_effect` (position-scaled dispatch with `charge_fraction` 1.0 for instant casts: Heal/Buff → `apply_consumable_effect`; bullet-control → validated `BulletControlEvent` appends with range-clamped cursor; Teleport → server-side move; Grapple → validated only; Spell → per-pellet mitigated shape damage + visual relay; marks → server-side `cursor_pick_enemy` nearest-in-`MARK_PICK_RADIUS`, server row winning over the client's optimistic pick), `spend_ability` (`OnAbilityUseBuff` triggers, then cooldown + charge).

## LoggedInPlayer

### Shape

Login state keyed by identity: `player_id` PK, `username`, `is_admin`, unique `profile_id` (the active profile). One row = one player in the world; every world reducer resolves `profile_id` off this row via `require_logged_in` / `require_in_world`.

### Changers

`join_world` moves the lobby row in (delete logged-out, insert here, then `try_scaffold_profile`); `leave_world`, `client_disconnected`, `kill_player` death cleanup, and the already-in-world recovery arm of `client_connected` move it back (all with the status/zone purge — no status may outlive the session). `set_username` edits either login row; `claim_admin` / `release_admin` flip `is_admin`.

### Readers

- ***Client*** — via the `local_player` view (own row only) in the Game wave → `EntitySpawnerComponent` spawns the `LocalPlayer` entity; `LobbyComponent` hides the lobby only on the `JoinWorld` success callback (failure or 5 s timeout re-shows with JoinError; `--charN` auto-join presses the Nth panel by ProfileId after `LobbyApplied`).
- ***Server*** — the omnipresent guard; AOI views semijoin it to drop logged-out ghosts; the enemy sim's chunk set keys off logged-in players; `change_stats`-style admin targeting resolves through it.

## LoggedOutPlayer

### Shape

Lobby state keyed by identity (`player_id` PK, `username`, `is_admin`) — deliberately not `public`, served only through the per-identity `local_lobby_player` view.

### Changers

Inserted by `client_connected` (unknown identities also get the starter "Knight" profile), `client_disconnected`, `leave_world`, and death cleanup; deleted by `join_world`; updated by `set_username` and the admin flips (see entries above).

### Readers

- ***Client*** — via `local_lobby_player` in the Lobby wave → `DatabaseConnector`'s internal hook (username/admin display) and `LobbyComponent`'s CharSlots panels (via `local_player_profiles`; inserts only, removal stays local via Delete).
- ***Server*** — `require_in_lobby` gates `create_profile` / `delete_profile` / `join_world`; `claim_admin` scans both login tables for the taken slot.

## PlayerProfile

### Shape

`profile_id` auto-inc PK, btree `player_id`, unique-per-player `profile_name`, catalog `texture_id`, and the `aim_assist` / `lock_on` toggles that drive the client's aim cone and snapping.

### Changers

`client_connected` seeds the first ("Knight" — which scaffold starts maxed at 999 per stat for testing, while manual profiles start at zero); `create_profile` / `delete_profile` (ownership-checked, lobby-gated; delete runs the union cleanup incl. allocation + rotation rows); aim-setting writes (see entries above).

### Readers

- ***Client*** — `local_player_profiles` (Lobby + Game waves → CharSlots panels) → Join calls `join_world`; `local_player_active_profile` (Game → `LocalPlayerProfileComponent` → 2D SpriteFrames / 3D `SetModel3DTexture` + `CombatComponent`'s assist cone and lock-on); `nearby_remote_players_profiles` (AOI-filtered through the remote-position chunk set, not all of `LoggedInPlayer` → `RemotePlayer`'s one-shot texture lookup + 3D mirror).
- ***Server*** — `find_profile_by_name` (case-insensitive per-player match for admin targeting).

## PlayerData

### Shape

The resolved numbers keyed by `profile_id`: `level` / `xp`, `hp` / `max_hp`, modifier-only `defense` and `base_speed` (resolved over 0 and `BASE_SPEED` respectively — never allocatable, same pattern as `max_hp`).

### Changers

Scaffolded at level 1 / `BASE_MAX_HP` / `BASE_SPEED`; `recompute_stats` resolves all four derived numbers (clamping `hp` down, updating only on change); `internal_gain_xp` adds xp, levels via `compute_level`, heals by the level-derived max-hp increase on top of current hp (never a full restore), and grants `SKILL_POINTS_PER_LEVEL` unspent points per level before recomputing; the combat sinks subtract/heal (death at zero → `kill_player`); teardown deletes (see entries above).

### Readers

- ***Client*** — via `local_player_data` in the Game wave → `LocalPlayerDataComponent` → `HealthComponent` mirror (health bar, zero-death UI), `Level`/`Defense`/`BaseSpeed` mirrors (`StatsSidebar` DEF line, `PositionSyncComponent.CurrentSpeed`).
- ***Server*** — `report_movement` clamps against `base_speed`; mitigation reads `defense`; `compute_player_damage` reads stats (below), spells read their scaling stat.

## PlayerStats

### Shape

Exactly the six allocatable stats (strength/wisdom/dexterity + damage_dealer/supporter/artisan temperaments — mechanically identical to attributes) keyed by `profile_id`. Hp/Defense/BaseSpeed have no field here by design.

### Changers

Scaffolded at flat `BASE_STAT` (growth comes from allocation, not base); updated ONLY by `recompute_stats` — allocated points + equipped modifiers + buffs as `(base + flat) × (1 + mult)` with additive mults, rounded; deleted by teardown (see entries above).

### Readers

- ***Client*** — via `local_player_stats` in the Game wave → `StatsComponent` mirrors (`StatsSidebar` six lines + `+` buttons; `HealthComponent.Health` shared-registered under `Hp`).
- ***Server*** — `check_stat_requirements` (equip gates), spell scaling, the strength damage term.

## PlayerStatAllocation

### Shape

The allocation *input* behind `PlayerStats`: per-stat assigned points + `unspent_points`, keyed by `profile_id`.

### Changers

Scaffolded zeroed (or 999-maxed for Knight); `allocate_stat` moves unspent into one of the six (rejects Hp/Defense/BaseSpeed); `internal_gain_xp` tops up unspent per level; consumed by `recompute_stats`; deleted by teardown (see entries above).

### Readers

- ***Client*** — via `local_player_stat_allocation` in the Game wave → `UnspentPoints` → `+` button visibility → `AllocateStat` calls.
- ***Server*** — input to `recompute_stats` only (`fold_allocation`).

## PlayerPosition

### Shape

`profile_id` PK (+ `player_id`), `x`/`y`, movement as angle + scalar (`movement_direction` keeping its last value while idle, `movement_speed` the clamped current speed, 0 when idle), and btree `chunk_index`. Clients reconstruct velocity = direction × speed for dead reckoning.

### Changers

Scaffolded (always reset to world origin on join — `leave_world` keeps the rows, so re-join would otherwise resume mid-map); `report_movement` (10 Hz: torus-wrap the claim → nearest-lap copy → grapple-allowance check → fall-lockout check → elevation rules via `hex_elevation` (±1 walks, steeper-up rejects the position claim, steeper-down is a fall with `falling_until` lockout + displacement exemption up to `MAX_FALL_DISPLACEMENT` + `FALL_DAMAGE_PER_LEVEL` past `FALL_SAFE_DROP`; a live grapple allowance exempts elevation entirely) → speed clamp (`base_speed` × held-charge `SelfSlow`) → displacement clamp along the reported vector (`base_speed` × dt × `MOVE_SLACK`, floor `MIN_REPORT_DISPLACEMENT` — clamp, never reject, so lag doesn't rubber-band; unchanged positions skip the write); `teleport_player` (exempt, restamps state); Teleport-ability arm. Deleted only by teardown — rows persist while logged out ("ghosts"; see [[#Known gaps / stubs]]).

### Readers

- ***Client*** — own row via `local_player_position` (Game wave — spawn/echo anchor; also read by `BulletSpawnerComponent` for band math); others via `nearby_remote_players` (own row excluded, ghosts semijoined out) → `RemotePlayer` binders → `InterpolationComponent.SetTarget` (torus-aware lerp + velocity dead reckoning) → moving puppets; `PositionSyncComponent` reports at 10 Hz (`ReportInterval`, event-driven `-1` mode on edges only) with speed-scale/self-slow controls. Visible end: every remote player gliding on screen.
- ***Server*** — every range check (`pickup_drop` / `take_from_chest` `INTERACT_RADIUS`, `report_enemy_hit` `HIT_VALIDATION_RADIUS`, `players_near_point` AoE/zone targeting, mark picks); AOI views deliberately do NOT key off this table (see next).

## PlayerRotation

### Shape

Camera/facing angle split out of position so it streams on its own faster cadence: `profile_id` PK, `player_id`, `screen_rotation`, and a `chunk_index` mirroring the position row so AOI views filter identically.

### Changers

Scaffolded; `report_screen_rotation` (~30 Hz, facing-change-gated client-side; sub-`ROTATION_REPORT_EPSILON` ~0.5° server skip); teardown deletes (see entries above).

### Readers

- ***Client*** — remote rows via `nearby_remote_player_rotations` → `RemotePlayer` rotation binder → `SetScreenRotationTarget` (puppet facing). No local echo — the local client owns its camera.
- ***Server*** — none (display + client aim state).

## PlayerChunk

### Shape

`profile_id` PK + `chunk_q`/`chunk_r`. Written only when the chunk actually changes — unlike position at 10 Hz — so everything keyed off it recomputes on real crossings.

### Changers

Scaffolded (origin chunk); `report_movement` / `teleport_player` update on change only; teardown deletes (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed by design).
- ***Server*** — `nearby_indices` / `nearby_indices_from_chunk` (chunk rings around the caller, `surrounding_chunk_indices` over the map grid, `1,1` fallback when unconfigured) feed every AOI view in this module — and chest/enemy/world AOI views reuse them. The one exception that proves the rule: reducer-side circle queries (`players_near_point`, `enemies_near_point`) use `chunk_indices_covering` over `PlayerPosition.chunk_index` directly, since views can't run inside reducers.

## ActiveConsumableEffect

### Shape

One row per live buff: auto-inc `effect_id`, btree `profile_id`, the `StatModifier`, `remaining` seconds. Plain pre-baked stat buffs only — anything with runtime pools or non-stat effects lives in ^table-active-status-effect instead.

### Changers

`apply_consumable_effect` inserts Buff rows (Heal arms delegate to `combat::heal_player` instead); the 1 Hz `tick_status_effects` decrements and deletes on expiry, then recomputes affected profiles; replacement deletes same-kind rows (see entries above).

### Readers

- ***Client*** — none (`public` but unsubscribed — no binder, no UI mirror by design).
- ***Server*** — `fold_buffs` in `recompute_stats`; teardown purges with the profile.

## ConsumableEffectSchedule

### Shape

Scheduled-job row driving `tick_status_effects` every `CONSUMABLE_EFFECT_TICK_SECONDS` (1 s). Armed once by `init`; the module's exactly-one 1 Hz schedule — consumable expiry, hit-flag/attestation/loot sweeps, then `status::methods::tick` (see entries above).

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — the scheduler fires the tick per row.

## LootDrop

### Shape

One row per ground drop: auto-inc `drop_id`, catalog `item_id` + socketed `enchantment_ids`, `x`/`y` + btree `chunk_index`, `expires_at` (`LOOT_DROP_EXPIRY` = 10 s), optional `dropped_by`, and the dropped slot's carried `cooldown_until`/`charges` — ability runtime state survives drop → pickup, and the picker inherits residual cooldown.

### Changers

`drop_item` inserts (follower drops take the whole span); `pickup_drop` deletes on take (range + expiry rejected up front; carried state restored; ability items auto-equip into the first free span when requirements pass, else loose backpack storage); the 1 Hz tick sweeps expired rows (see entries above).

### Readers

- ***Client*** — via `nearby_loot_drops` in the Game wave → `EntitySpawnerComponent` → `drop.tscn` puppets (`Drop.cs`, catalog sprite; 3D hides the sprite, pickup still works) with `PickupComponent` Area2D → body-entered calls `PickupDrop`. Visible end: loot glittering on the ground, vanishing into the backpack on touch.
- ***Server*** — `pickup_drop`'s guards only.

## PlayerMovementState

### Shape

Server-only movement bookkeeping: `profile_id` PK, `last_report` (the displacement budget's dt anchor), `falling_until` (fall-tween lockout; None = walking).

### Changers

Created on first report/teleport contact; restamped every report; falls set the lockout; teardown deletes (see entries above).

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — `report_movement`'s budget and fall-damage path (`FALL_DAMAGE_PER_LEVEL` past `FALL_SAFE_DROP`, lockout while tweening).

## PlayerDisplacementAllowance

### Shape

Server-only one-shot Grapple window: `profile_id` PK, `max_distance`, short `expires_at` (`GRAPPLE_ALLOWANCE_SECONDS`).

### Changers

Stamped by the Grapple arm; consumed by the next qualifying oversized report (which also skips elevation rules); dies on expiry contact and teardown (see entries above).

### Readers

- ***Client*** — none (not `public`). The client tween (`StartGrappleTween`, reports at start/end only, timed/edge reports suppressed via `ExternalMovementActive`) exists precisely so it never burns this allowance mid-tween.
- ***Server*** — the clamp's one-shot exemption: without it every legit grapple would rubber-band.

## PlayerHitRate

### Shape

Server-only 1 s fixed-window token bucket per profile: `window_start`, `outgoing` (weapon-hit claims), `incoming` (damage-taken claims).

### Changers

Stamped by both self-reported hit reducers through `hit_rate_allow`; teardown deletes (see entries above).

### Readers

- ***Client*** — none (not `public`).
- ***Server*** — `outgoing` caps `report_enemy_hit` at fire_rate × shot_count × `HIT_RATE_SLACK` (bounds hit-spam and StoreIncoming farming); `incoming` caps `report_hit` at `INCOMING_HIT_RATE_CAP` (~30/s, bullet-hell generous, bounding the StoreIncoming input rate).

## Files — pointer deep dive

Entry points: [[server/spacetimedb/src/player/pointers.md|player pointers]].

### methods.rs

Guards (`require_logged_in` / `require_in_world` / `require_in_lobby`, `is_admin`), stat math (`xp_for_level`, `compute_level`, `compute_base_max_hp`, `fold_*` + `recompute_stats`), session ops (`teleport_player`, `internal_gain_xp`, `apply_consumable_effect`, `try_scaffold_profile` / `teardown_profile`, `kill_player`, `players_near_point`), slot helpers (`slot_allowed`, `get_slot`, `update_slot`, `ability_position_multiplier`, `find_free_span`, `place_span`, `clear_span`), `check_stat_requirements`, `find_profile_by_name`, and the single acquire path `transfer_item_to_inventory`. Consumed by anticheat (login guard), chest (guards + acquire), combat (xp + death), enemy (hit-rate + guard), item (slot helpers + guards), admin/debug (guards, stats, lookup), world (guards), and status (AOI query, charge guard).

### mod.rs

Module declarations only.

### reducers.rs

Every player reducer (see all entries above): identity/profile management, movement + rotation reports, hit reports, inventory ops, ability dispatch trio (`resolve_ability_slot` / `apply_ability_effect` / `spend_ability` + `scale_ability_effect` + `cursor_pick_enemy`, shared with the status charge reducers), and the 1 Hz tick. Called from `LobbyComponent` (profiles/join), `PositionSyncComponent` (movement/rotation), `DamageReceivingComponent` (`report_hit` via [[client/Scenes/Components/Combat/damage_receiving_component.tscn|damage_receiving_component.tscn]]), `PickupComponent` (`pickup_drop` via [[client/Scenes/Components/Interaction/pickup_component.tscn|pickup_component.tscn]]), the inventory UI (`use_item`, `drop_item`/`swap_slots`, `allocate_stat`, enchantment socket/remove, `set_slot_toggle`, `activate_ability` — all inline in [[client/Scenes/local_player.tscn|local_player.tscn]]), and `SlotComponent`'s chest branch (`take_from_chest` lives in the chest module).

### tables.rs

All sixteen tables above. Read across the module boundary by chest (position), combat (data/stats/login), enemy (login/position/chunk), item (slots/roles), admin/lifecycle/debug (login rows, allocations, schedules, position/rotation), and status (login/position).

### views.rs

The per-player views (`local_player`, `local_lobby_player`, `local_player_profiles`, `local_player_active_profile`, `local_player_data`, `local_player_stats`, `local_player_stat_allocation`, `local_player_inventory`, `local_player_position`), the AOI views (`nearby_remote_players` + rotations + profiles, `nearby_loot_drops`), and the shared `nearby_indices` / `nearby_indices_from_chunk` helpers that chest/enemy/world AOI views build on. Consumed client-side by the spawner, lobby, inventory/data/profile components, `RemotePlayer`, `BulletSpawnerComponent` (position views for band math), `CombatComponent` (active profile for aim), and `DatabaseConnector` (lobby player).

## Cross-table flows

Joining the world spans the module in one transaction: lobby row → login row → full scaffold (data/stats/allocation/slots/position/rotation/chunk) → spawn at origin. Death runs the inverse through `kill_player` (teardown + lobby move + status/zone purges). Casting spans slots → dispatch → event appends → spend. Each link lives on its own entry above.

## Known gaps / stubs

- **`client_connected` mishandles the already-in-world case**: it purges the player's statuses/zones, deletes the `LoggedInPlayer` row, and inserts a `LoggedOutPlayer` row (dropping the player back to the lobby) instead of rejecting — flagged as a bug only in a `log::error!` message, not a comment.
- **`PlayerPosition`/`PlayerChunk` rows persist while logged out ("ghosts")**; the enemy sim guards against them via `logged_in_player` checks rather than cleaning up (cleanup happens only in `teardown_profile`, i.e. on death or `delete_profile`).
- **Leftover debug prints**: `GD.Print`s in [[client/Scripts/Components/Lobby/LobbyComponent.cs#LobbyComponent|LobbyComponent.cs]] (auto-join + profile-sync lines) and the `[Desync]` print in [[client/Scripts/Components/Movement/PositionSyncComponent.cs#PositionSyncComponent|PositionSyncComponent.cs]]. (`TableBinderComponent` prints are Verbose-gated opt-in diagnostics — see `03`; the `TableSubscriber` wave-applied prints are the same leftover family.)
- **`LobbyGui` is scene navigation only** — not yet wired to the `create_profile`/`join_world` reducers (those calls live in `LobbyComponent`). Its ServerList/Settings panels are show/hide only.

## Where to go next

[[11 Status Tables]] covers the buffs, marks, zones, and charges these rows carry. For the login-row lifecycle hooks, see `09 Main Tables`.
