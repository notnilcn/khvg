# 05 Player Data & Progression Tables

## Assumed knowledge

- [[02 The Component Framework]] — the client's entity/component model: components as children of an entity root, `TableBinderComponent` as the standard table-consumer, and the "mirror, never mutate" rule.
- [[03 Connection, Subscriptions & Views]] — what a reducer, a per-caller view, and a subscription wave are; how `GameTables` reaches a binder. Every read path below is an instance of that doc's pattern.
- [[04 Lobby & Identity Tables]] — profiles (`PlayerProfile`) and the `join_world` transition: this doc's three tables are all keyed by `profile_id` and are scaffolded the moment a profile enters the world.

## The 30-second version

Three per-profile tables hold the player's numbers. **PlayerData** is the live sheet — level, xp, hp, and the *resolved* `max_hp`/`defense`/`base_speed` — written by the profile scaffold, the XP/level math, the stat resolver, and the combat pipeline's two hp writers. **PlayerStatAllocation** is the progression *input* (per-stat assigned points plus `unspent_points`), written by the `allocate_stat` reducer, the XP path, and the admin `change_stats`. **PlayerStats** is the resolved *output* of the allocation plus all gear/buff modifiers — written by exactly one function, `recompute_stats`. The client reads each through its own per-caller view in the `GameTables` wave, into three binders on `LocalPlayerDataComponent` in `local_player.tscn`, which mirror the rows into the `HealthComponent`/`StatsComponent` observers the UI reads.

## Flowcharts

- [[flowcharts/main-player-data.canvas]] — this system's composed flow.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Data_subfolder/Data_subfolder.canvas]] — `LocalPlayerDataComponent` / `StatsComponent`, the client mirror.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/player_subfolder/player_subfolder.canvas]] — the server `player/` module: tables, `recompute_stats`, the XP math, `allocate_stat`.
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/combat_subfolder/combat_subfolder.canvas]] — the combat pipeline that writes `PlayerData.hp` and grants XP.

## Table changes

```sync
![[00 Table Map#^table-player-data{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 Table Map#^table-player-stats{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 Table Map#^table-player-stat-allocation{seamless:true,title:false,marker:03.}]]
```

## Main body

### Why three tables: the input/output split

Progression data is split so that no writer ever has to merge concerns. [[server/spacetimedb/src/player/tables.rs#PlayerStatAllocation|PlayerStatAllocation]] is what the player *chose* — a `u32` per allocatable stat plus `unspent_points` — and it survives every gear swap because nothing but allocation, leveling, and the admin tool touches it. [[server/spacetimedb/src/player/tables.rs#PlayerStats|PlayerStats]] is what those choices *resolve to* once equipment and buffs are folded in, and it is disposable: [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]] rebuilds it from scratch on every relevant change. [[server/spacetimedb/src/player/tables.rs#PlayerData|PlayerData]] is the runtime sheet the simulation actually reads and writes at combat speed — `hp` changes per bullet, `xp` per kill — and it also carries the three *modifier-only* resolutions (`max_hp`, `defense`, `base_speed`), which live here rather than in `PlayerStats` *because* they are not allocatable: gear raises them, players never spend points on them (the table comments in `player/tables.rs` say this explicitly). All three tables share the `profile_id` primary key, so the archetype helpers can scaffold and tear them down as one unit — the server-side mirror of the client's one-component-per-concern model described in [[02 The Component Framework]].

The `StatKind` enum defines the vocabulary both sides share: six allocatable stats (Strength/Wisdom/Dexterity — the attributes — plus DamageDealer/Supporter/Artisan — the temperaments, mechanically identical to attributes) and three modifier-only kinds (Hp/Defense/BaseSpeed). The server defines it in `item/tables.rs` next to `StatModifier` (gear's way of adding to a stat); the client mirrors it in [[client/Scripts/Resources/Stats/StatKind.cs|StatKind.cs]], which also warns that the generated bindings define their own `SpacetimeDB.Types.StatKind` — the two must be qualified apart at call sites like `AllocateStat`.

### Lifecycle: scaffold, live, teardown

The rows appear in [[server/spacetimedb/src/player/methods.rs#try_scaffold_profile|try_scaffold_profile]], the archetype helper `join_world` runs (see [[04 Lobby & Identity Tables]]). `PlayerData` inserts at level 1, 0 xp, full hp, `defense: 0`, `base_speed: BASE_SPEED`. `PlayerStatAllocation` inserts all-zero — with one deliberate exception: the auto-created "Knight" profile (the default profile `client_connected` makes, see [[04 Lobby & Identity Tables|doc 04]]) starts with 999 points in every stat so testing doesn't require grinding, and since allocation is the input `recompute_stats` reads, that head start survives every later recompute. `PlayerStats` has no direct insert here at all — the scaffold simply calls `recompute_stats` when the row is missing, *because* that function is already the insert-or-update resolver; keeping one code path guarantees the first row obeys the same formula as every later one.

The rows disappear in [[server/spacetimedb/src/player/methods.rs#teardown_profile|teardown_profile]], the shared inverse — called by combat death cleanup and by `delete_profile`. There is no partial delete and no logout delete: `leave_world` keeps the rows, so a player who leaves and rejoins keeps their level, xp, allocation, and gear (only their position resets — see [[06 Movement & AOI Tables]]).

### `recompute_stats`: the one place stats resolve

Every resolved number a player has comes out of [[server/spacetimedb/src/player/methods.rs#recompute_stats|recompute_stats]], and the callers list shows why it must be the only resolver: it is invoked by `allocate_stat`, by `internal_gain_xp` on level-up, by `apply_consumable_effect` when a buff lands, by the admin `change_stats`, and by every inventory reducer that can change what is equipped — `swap_slots`, `use_item`, `pickup_drop`, `drop_item`, `apply_enchantment`, `remove_enchantment`, plus `take_from_chest` in `chest/mod.rs`. Any one of those can change the flat or mult total, so each one ends in a recompute rather than trying to patch the delta.

The function folds three source layers into a pair of nine-slot accumulators (`flat` and `mult`, indexed by `stat_index` — the six allocatable stats first, then Hp, Defense, BaseSpeed):

1. **Allocation** — the `PlayerStatAllocation` row adds straight into `flat`, so allocated points behave exactly like flat gear bonuses and are scaled by gear mults like any other flat source (the code comment calls this out as what makes temperaments work identically to attributes).
2. **Equipped gear** — only slots whose *role* is Weapon, Accessory, or Armor contribute, at scale 1.0; Ability heads contribute scaled by `ability_position_multiplier` (the `ABILITY_POSITION_MULTIPLIERS` ladder — first ability stronger, the reward for slot ordering); span followers (`occupied_by.is_some()`) and every other role (Hotbar/General/Bag) contribute nothing, *because* "equipped" is an explicit role check, not an inference from the item. Each contributing slot folds its item's `stat_modifiers` plus the modifiers of its innate enchantments (on the item row, unremovable, socket-free) and socketed enchantments (on the slot row) — innate before socketed, matching the deterministic fold order [[10 Items, Inventory & Loot Tables|doc 10]] covers.
3. **Active consumable buffs** — every `ActiveConsumableEffect` row folds its stored `StatModifier` at scale 1.0 (the row is inserted pre-flattened by `buff_to_modifier`).

Resolution is `(base + flat) × (1 + mult)`, rounded to `i32`. Mults are **additive, not compounding** — two +10% sources give +20% — because they sum in one accumulator before the single multiply. The six allocatable stats resolve over the flat `BASE_STAT` (10 in [[server/spacetimedb/src/main/global.rs|main/global.rs]]): levels never raise stats directly, they only grant points. The result upserts the `PlayerStats` row (update if present, insert if not — the lazy-insert path the scaffold relies on).

The modifier-only kinds resolve onto `PlayerData` instead: Hp becomes `max_hp` over `compute_base_max_hp(level)` (floored at 1), Defense becomes `defense` over a base of 0 (floored at 0), BaseSpeed becomes `(BASE_SPEED + flat) × (1 + mult)` floored at 1.0 and deliberately *not* rounded — speed stays `f32`. The `PlayerData` update is written **only when something actually changed**, and it clamps `hp` down to the new `max_hp` — so unequipping an hp item can't leave `hp > max_hp`, and a no-op recompute (e.g. a buff expiring into the same totals) costs no row update and therefore no client echo.

What consumes the resolved row: outgoing damage reads `PlayerStats.strength` in [[server/spacetimedb/src/combat/mod.rs#compute_player_damage|compute_player_damage]] (`damage × (1 + strength × 0.002)`, divided across the effective shot count), equip gating reads all six in [[server/spacetimedb/src/player/methods.rs#check_stat_requirements|check_stat_requirements]] (classes emerge from gear locked behind stat thresholds — see [[10 Items, Inventory & Loot Tables|doc 10]]), `PlayerData.defense` is the flat subtraction in [[server/spacetimedb/src/combat/mod.rs#compute_incoming_damage|compute_incoming_damage]], and `PlayerData.base_speed` caps every movement report in `report_movement` ([[06 Movement & AOI Tables|doc 06]]).

### XP, levels, and the level-up heal

XP has exactly one source: killing an enemy. [[server/spacetimedb/src/combat/mod.rs#apply_damage_to_enemy|apply_damage_to_enemy]] — the single enemy damage sink — computes the reward as `template.max_hp / 10` on a kill and calls [[server/spacetimedb/src/player/methods.rs#internal_gain_xp|internal_gain_xp]] for the credited profile (see [[09 Combat & Event Tables|doc 09]] for how credit is attributed).

The level curve is quadratic: `xp_for_level(level) = 100 × (level − 1) × level / 2` total xp, so level 2 costs 100, level 3 costs 300 cumulative, level 4 costs 600. The math is `u64` on purpose — the comment above `xp_for_level` records that the old `u32` formula overflowed around level 2930 and made `compute_level`'s loop spin forever; `compute_level` is additionally bounded by `MAX_LEVEL` (10 000) as a corruption guard. `internal_gain_xp` saturating-adds the xp, recomputes the level, and on each level gained does three things: heals `HP_PER_LEVEL` (5) per level *on top of current hp* — deliberately not a full restore, so leveling while hurt doesn't waste the heal — grants `SKILL_POINTS_PER_LEVEL` (2) to `unspent_points`, and calls `recompute_stats`, which raises `max_hp` by the same `HP_PER_LEVEL` per level (via `compute_base_max_hp`) and folds any gear Hp bonus before clamping. The level-up path is the only writer of `unspent_points` besides the admin tool.

### The hp writers: damage, healing, death

`PlayerData.hp` is written in exactly three places, and the discipline matters: `recompute_stats` (the max_hp clamp above), `internal_gain_xp` (the level-up heal), and the combat pipeline. All combat hp change flows through [[server/spacetimedb/src/combat/mod.rs#deal_damage_to_player_opts|deal_damage_to_player_opts]] and [[server/spacetimedb/src/combat/mod.rs#heal_player|heal_player]] — no other code subtracts hp, including the status module's bleed drains, which route through the same flagged damage path *because* death cleanup must have exactly one home (the doc comment on `deal_damage_to_player_opts` states "No direct HP writes anywhere"). Damage runs the status intercept first (absorption, redirects, floors — [[11 Status, Zone & Charge Tables|doc 11]]), then subtracts `PlayerData.defense` with a minimum of 1 via `compute_incoming_damage`, then `saturating_sub`s from hp. `heal_player` is the mirror: add, clamp to `max_hp`.

At 0 hp the same function runs the full death cleanup: `teardown_profile` deletes every per-profile row (this doc's three included), the `LoggedInPlayer` row is deleted, and a `LoggedOutPlayer` row is inserted — the player wakes in the lobby, and their next `join_world` re-scaffolds fresh rows. Note it keys off `player.player_id`, not `ctx.sender()`, because scheduled bleed drains have no sender.

### `allocate_stat` and the admin `change_stats`

[[server/spacetimedb/src/player/reducers.rs#allocate_stat|allocate_stat]] is the only player-facing progression reducer. (A *reducer*, recall from [[03 Connection, Subscriptions & Views|doc 03]], is the server's transactional RPC: the client calls it by name, it validates and writes tables, and the client learns the result through row echoes — reducers return nothing to the caller.) It requires the caller to be in-world, rejects `points == 0` and overspending `unspent_points`, adds the points to the chosen allocation field, and ends in `recompute_stats` — the input-then-recompute two-step every stat writer follows. Allocating into Hp/Defense/BaseSpeed is an explicit error ("come from gear, not allocation"), which is what makes the modifier-only split a rule rather than a convention.

[[server/spacetimedb/src/main/admin.rs#change_stats|change_stats]] (admin-only, CLI-driven — see [[13 Admin & Debug Tables|doc 13]]) writes the *allocation* row, not `PlayerStats`, clamping negatives because allocation is unsigned. Its comment explains why: `PlayerStats` is rebuilt from allocation + gear on every inventory reducer, so values written there would be wiped on the next equip.

### The client mirror: rows → components → UI

The three per-caller views — [[server/spacetimedb/src/player/views.rs#local_player_data|local_player_data]], [[server/spacetimedb/src/player/views.rs#local_player_stats|local_player_stats]], [[server/spacetimedb/src/player/views.rs#local_player_stat_allocation|local_player_stat_allocation]] — each resolve the caller's `profile_id` from their `LoggedInPlayer` row and filter to that one row (with the `u64::MAX` sentinel matching nothing when the caller isn't in-world — the standard per-caller-view idiom from [[03 Connection, Subscriptions & Views|doc 03]]). Because the tables are `public` but the client only ever subscribes through these views, a client never sees another player's stats; all three ride in the `GameTables` wave in [[client/sstdbsdk/TableSubscriber.cs#GameTables|TableSubscriber]], subscribed on world entry.

On the client the rows land in [[client/Scripts/Components/Data/LocalPlayerDataComponent.cs|LocalPlayerDataComponent]], a plain `Component` child of the `LocalPlayer` entity whose three binder children are declared inline in `client/Scenes/local_player.tscn` — `LocalPlayerDataBinder`, `LocalPlayerStatsBinder`, `LocalPlayerStatAllocationBinder`, each `ReplayExistingRows = true` (persistent tables, so a late-bound component still sees the current row), with `RowInserted` **and** `RowUpdated` wired to the same handlers: insert and update are the same mirror operation here. No `RowDeleted` is wired — the only delete path is death/teardown, which also deletes the `LoggedInPlayer` row, and *that* is what despawns the whole `LocalPlayer` entity via the spawner (the entity takes its components with it, so per-table delete handling would be dead code).

The three handlers push into the mirror components:

- `OnDataRow` casts the binder's `LastRow` to `PlayerData`, stores `Level`/`Defense`/`BaseSpeed` as plain properties, and calls [[client/Scripts/Components/Combat/HealthComponent.cs#SetFromServer|HealthComponent.SetFromServer(hp, maxHp)]]. `HealthComponent` owns the hp as a clamped [[client/Scripts/Resources/Stats/Stat.cs|Stat]] resource (`MinValue = 0`, `MaxValue = max_hp`) and emits `HealthDidDecrease`/`HealthDidIncrease`/`HealthDidZero` for observers — it deliberately has no `Damage()`/`Heal()` methods, because the client never computes health; it mirrors.
- `OnStatsRow` pushes each of the six resolved stats through [[client/Scripts/Components/Data/StatsComponent.cs#SetFromServer|StatsComponent.SetFromServer]], which writes the `Stat` and emits `StatChanged` only on an actual change. `StatsComponent` is just a `StatKind → Stat` dictionary with lazy creation.
- `OnAllocationRow` stores `UnspentPoints`.

All three then call `RaiseStatsChanged()` on the `LocalPlayer` entity. Two wiring details make the observer graph work: in `OnEntityReady`, `LocalPlayerDataComponent` registers the *same* `Stat` instance `HealthComponent` owns into the `StatsComponent` under `StatKind.Hp` (comedot's shared-Stat pattern — one instance, so hp observers and stat observers can never disagree); and `Stat.SetValue` emits `ValueChanged` only on real changes, so a no-op server echo costs no UI refresh.

Readers never touch the components directly: [[client/Scripts/Players/Local/LocalPlayer.cs|LocalPlayer]] exposes stable pass-through properties (`Hp`, `MaxHp`, `Level`, the six stats, `Defense`, `BaseSpeed`, `UnspentPoints`) so UI survives component refactors. The stats UI is [[client/Scripts/Components/Inventory/StatsSidebar.cs|StatsSidebar]], a plain `Control` script declared inline in the inventory UI in `local_player.tscn` (node `InventoryCanvas/InventoryPanel/Menu/StatsSidebar`): it finds its `LocalPlayer` by walking ancestors (not `LocalPlayer.Local`, which is still null during `_Ready`), refreshes every label on `StatsChanged`, and shows one `+` button per allocatable stat only while `UnspentPoints > 0` — each wired in `_Ready` to call `GameManager.Conn?.Reducers.AllocateStat(stat, 1)`. That call is the entire client side of progression: click → reducer → server recomputes → the three rows echo back → the same labels refresh through the mirror. `BaseSpeed` similarly flows out through the `LocalPlayer.BaseSpeed` pass-through into movement ([[06 Movement & AOI Tables|doc 06]]), keeping the client's speed honest with the server's clamp.

## Known gaps / stubs

- **Five of the six resolved stats have no gameplay formula yet.** Only `strength` is consumed by a damage calculation (`compute_player_damage`); `wisdom`, `dexterity`, `damage_dealer`, `supporter`, and `artisan` resolve, display, and gate equipment through `check_stat_requirements`, but no damage/healing/utility formula reads them (verified by grep — their only server-side uses are the requirement checks in `player/methods.rs`). Allocation beyond Strength is currently cosmetic-plus-gating.
- **The default "Knight" profile ships maxed.** `try_scaffold_profile` inserts 999 points in every stat for the auto-created Knight profile — a deliberate testing scaffold, but it means the out-of-box new-player experience is not representative of real progression.

## Where to go next

[[06 Movement & AOI Tables]] shows the other consumer of `PlayerData` — `report_movement`'s speed clamp against the resolved `base_speed`. [[09 Combat & Event Tables]] covers the pipeline that spends `hp` and grants the XP this doc's tables bank, and [[10 Items, Inventory & Loot Tables]] explains where every stat modifier `recompute_stats` folds actually comes from.
