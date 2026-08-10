# 09 Combat & Damage

## Assumed knowledge

- [[08 Enemies & AI]] — where enemy bullets come from (`BulletPatternEvent`, enemy-5), the client bullet reconstruction (`BulletSpawnerComponent`, enemy-9), the `SourceStep` breadcrumb, and the phase/aggro machinery (`compute_phase`, `recompute_aggro`, `aggro_locked_until`, enemy-4) that the damage path drives.
- [[06 Movement & Position Sync]] — `require_in_world` (move-3), the guard both hit reducers start with.
- [[05 Joining the World]] — the game subscription wave (join-3) that carries HP rows back, and the join-6 despawn path that death falls into.
- [[04 Lobby & Profiles]] — profiles carry the `aim_assist`/`lock_on` flags (always false, lobby-5) and the XP/level math (`compute_level`) that `internal_gain_xp` feeds.
- [[02 The Component Framework]] — entities/components, the ancestor-walk registration that lets a dynamically spawned `HitZone` belong to the `LocalPlayer` entity, and the server-side "one table per concern" mirror that `combat/mod.rs` explicitly models itself on.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `combat` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

Combat is **client-detected, server-decided**. The client never computes a damage number — it only reports that two things touched. Player→enemy: the local player's `CombatComponent` turns the equipped weapon's stats into a timed fuse of `HitZone` hitboxes laid along each shot's path (the visible bullet is a decorative tracer with no collision); when a zone's fuse expires it reports every enemy hurtbox inside it — filtered by faction bit flags — to the `report_enemy_hit` reducer, which recomputes damage from the attacker's weapon and Strength, applies phase transitions and aggro-on-hit, and on a kill despawns the enemy and awards XP. Enemy→player: BlastBullets2D bullets overlapping the local player's hurtbox are routed by `BulletHitRouterComponent` to `report_hit`, which looks the step definition's `damage` up from the echoed `SequenceStepRef`, mitigates it by Defense, and subtracts. Either direction's HP change flows back as a table row into the victim's `HealthComponent`, a read-only mirror. Player death is the same row move as leaving the world — `teardown_profile` plus `LoggedInPlayer` → `LoggedOutPlayer` — so dying dumps you back at the lobby.

## Flowcharts

- [[flowcharts/main-combat.canvas]] — the composed combat flow (client `Combat`/`Weapon`/`Bullets` components and their scenes, `hit_zone.tscn`/`bullet_manager.tscn`, the server's `combat` module plus the `player` and `enemy` modules it calls into).
![[flowcharts/main-combat.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Weapon_subfolder/Weapon_subfolder.canvas]] — deep dive: `CombatComponent`, the weapon-to-hit-zone firing logic.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Combat_subfolder/Combat_subfolder.canvas]] — deep dive: the combat component vocabulary (`DamageComponent`, `DamageReceivingComponent`, `HitZone`, `HealthComponent`, `FactionComponent`).
- [[flowcharts/Subflowcharts/server_subfolder/spacetimedb_subfolder/src_subfolder/combat_subfolder/combat_subfolder.canvas]] — deep dive: `combat/mod.rs`, the shared server damage pipeline.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^combat-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-7{seamless:true,title:false,marker:07.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-8{seamless:true,title:false,marker:08.}]]
```

## Main body

### The weapon becomes a fuse of hitboxes, not a projectile

```sync
![[00 End-to-End Timeline Flowchart#^combat-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-3{seamless:true,title:false,marker:03.}]]
```

The design constraint behind the zone trick is that **the server never sees a bullet** — not the player's, not the enemy's (enemy-5 established that even server-originated shots are just event rows). So a "real" projectile that reports on contact would tie damage to client physics frame rate and position jitter, while the fused-zone version ties it to exactly one input: the aim direction at fire time. `SpawnZonesAlong`'s delay arithmetic (`(i + 0.5) × _zoneStep / speed`) makes each zone's fuse equal to the time the *visual* tracer needs to reach the zone's center, so what the victim sees and what gets reported can never drift apart — the tracer is pure presentation laid over a hitscan timeline.

A few small behaviors of [[CombatComponent.cs##public override void _Process(double delta)|_Process]] are easy to miss. The fire timer is clamped (`Mathf.Min(_fireTimer + delta, _firePeriod)`), so you can't bank unused shots by waiting. Swapping weapons pre-charges the timer (`_fireTimer = _firePeriod` in `OnInventoryChanged`), so a fresh weapon fires on the next frame — no equip cooldown. And a weapon whose `ZoneCount` is 0 short-circuits the derivation in `OnInventoryChanged` before `_firePeriod`/`_zoneStep` are computed; no seeded weapon does this — the one seeded weapon sets `zone_count: 25` — so the branch is dormant. `WeaponBehavior.pierce` is part of the same story: nothing on the client reads it (verified by grep — only the generated binding defines it), and with zones each already reporting every enemy inside them, "piercing" is effectively how player shots always behave.

The origin offset deserves its line of code: `Fire` starts the shot at `player.GlobalPosition + aimDir * _playerRadius`, where `_playerRadius` is read off the player's own `Collider` capsule in [[CombatComponent.cs##protected override void OnRegistered|OnRegistered]] — so zones and tracers spawn at the body surface instead of inside it, and a point-blank enemy still gets a zone on top of it (the first zone's center is half a step out, its radius half a step, so coverage starts at the origin with no gap).

### Factions are the only friendly-fire rule

```sync
![[00 End-to-End Timeline Flowchart#^combat-4{seamless:true,title:false,marker:04.}]]
```

The faction system is as small as it looks: three bit flags and one bitwise test, `(a & b) == 0` means "opposed". The two live assignments are scene data — the player's [[local_player.tscn##Factions = 2|FactionComponent sets Factions = 2]] (`Players`) and the enemy puppet's `Factions = 4` (`Enemies`) in `default_enemy.tscn` — and they're deliberately *not* synced, because faction is implicit in entity kind; the server never needs the concept (its two reducers are already directional: `report_enemy_hit` only hurts enemies, `report_hit` only hurts the caller). `DamageReceivingComponent` declares the dependency formally through `GetRequiredComponents() => FactionComponent`, so the framework warns if a scene pairs a hurtbox with no faction.

One edge in the exact rule, because the docstrings overstate it: a *missing* `FactionComponent` falls back to `Neutral`, and `Neutral` shares no flag with `Players` or `Enemies` — but two `Neutral` entities share the `Neutral` flag, so they are *not* opposed. "Opposed to everything" in the comments really means "opposed to everything except another factionless entity". In practice every combat entity sets an explicit faction, so the fallback is dormant.

Also note where the zone's identity comes from: `HitZone` is instantiated mid-game and `AddChild`ed under `CombatComponent`, and the framework's registration walk ([[02 The Component Framework]]) climbs ancestors to the nearest `IEntity` — `LocalPlayer` — so `CanDamage` reads the *player's* faction and `ReportHits` reports in the player's name (the reducer attributes damage via `ctx.sender()` regardless). And the multiplicity is real: an enemy straddling three zones of one shot gets three `report_enemy_hit` calls and takes triple damage. That is the intended shotgun-point-blank payoff, not a bug — but see the trust discussion below.

### The server pipeline: `combat/mod.rs` is the single damage doorway

```sync
![[00 End-to-End Timeline Flowchart#^combat-5{seamless:true,title:false,marker:05.}]]
```

Both reducers are thin — resolve inputs, call into `combat/mod.rs` — and the module header says why: one place where damage is computed, mitigated, and applied, so the player and enemy paths can never drift. The formulas are linear dials: outgoing damage is `weapon.damage × (1 + strength × 0.002)` ([[server/spacetimedb/src/combat/mod.rs##pub fn compute_player_damage|compute_player_damage]]), incoming is `base × (1 − defense × 0.002)` with a floor of 1 ([[server/spacetimedb/src/combat/mod.rs##pub fn compute_incoming_damage|compute_incoming_damage]]). At 0.002 per point it takes 500 Defense to fully negate — the floor of 1 makes "immune through stats" unreachable. `compute_player_damage`'s fallbacks encode the intended failure modes: a missing stats or inventory row degrades to 1 (a desynced profile still chips), but a missing *weapon* is 0 — an unarmed report is legitimate and harmless.

The trust boundary is worth stating plainly, because the codebase draws it deliberately. The client is authoritative for **contact** — only the shooter's machine knows what its zones overlapped, and `report_enemy_hit` performs no proximity, cooldown, or rate validation; a modified client could report every enemy on the map every frame. The server is authoritative for **consequence** — the damage number, the phase transition, the XP, the death. The plan's answer to spam is not validation but shape: `deal_damage_to_enemy`'s aggro branch is idempotent-ish (a repeat attacker within `aggro_locked_until` is a no-op), and the XP reward scales with the template, not the hit count. For the current co-op scope this is an accepted trade; the unimplemented despawn protocol in Known gaps is where the tightening was headed.

The aggro-on-hit half of `deal_damage_to_enemy` completes enemy-4's targeting story: the behavior tick recomputes aggro only when an enemy *enters* simulation range, so mid-fight retargeting is entirely driven by being hit. The two branches are ordered on purpose — a phase change re-runs full `recompute_aggro` (nearest player, fresh lock), while a same-phase hit just swings aggro to the attacker *if the lock expired* — so a boss mid-phase-transition doesn't instantly glue to whoever poked it first. The `enemy.phase` write here is what the 100 ms tick detects one beat later (enemy-4), which is why phases feel responsive without the tick ever computing them.

### Enemy bullets: one breadcrumb, one reducer, one direction

```sync
![[00 End-to-End Timeline Flowchart#^combat-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^combat-7{seamless:true,title:false,marker:07.}]]
```

The elegance of the `SourceStep` design is what it *doesn't* send. The client could echo the damage number, the event id, the pellet index — instead it echoes only which step definition the shot came from, and the server re-derives everything from persistent tables. That works because of the def/instance split from enemy-3: `SingleStepDef`/`MultiStepDef` are shared, immutable content and `RepeatStepInstance` is per-enemy but durable, so the lookup in [[server/spacetimedb/src/player/reducers.rs##pub fn report_hit|report_hit]] succeeds minutes after the `BulletPatternEvent` row evaporated. The cost is that a hit report is only as fresh as the def — reseeding a template mid-fight retroactively changes what an in-flight bullet is worth. At publish-time seeding, that's theoretical.

The asymmetry with the player→enemy path is the collision owner. Player shots use Godot `Area2D` overlap (`HitZone` vs hurtbox, mask 4 → layer 4); enemy shots use the BlastBullets2D engine's own overlap against layer 2, surfaced as the factory's `area_entered` signal — which is why [[BulletHitRouterComponent.cs##protected override void OnRegistered|BulletHitRouterComponent.OnRegistered]] hand-connects a `Callable` with the GDExtension's five-argument signature instead of using any component wiring. Only the *local* player has a layer-2 hurtbox (remote puppets in `non_local_player.tscn` have no `DamageReceivingComponent` at all — they never take enemy-bullet collisions), so each client reports exactly its own hits, and the server never has to dedupe two clients reporting one victim.

What happens *visually* after the hit is the weakest link, and it's the motivation for everything in Known gaps: the BlastBullets2D default disables the bullet on the victim's client (max collision count 1, never overridden in `SetupEnemyBullets`), and nobody tells anyone else. Other players watch a bullet pass through you and keep flying; you watch it vanish. The bullet was never a shared-world object — only its spawn event was.

### Health mirrors, death, and XP

```sync
![[00 End-to-End Timeline Flowchart#^combat-8{seamless:true,title:false,marker:08.}]]
```

[[HealthComponent.cs##public partial class HealthComponent|HealthComponent]] is the client end of the authority story compressed into one class: it owns a `Stat` (the framework's observable number — [[10 Inventory, Items & Enchantments]] covers `Stat`/`StatsComponent`) but exposes no way to change it except `SetFromServer`, and its three signals (`HealthDidDecrease`/`HealthDidIncrease`/`HealthDidZero`) are derived by *diffing consecutive server values*, so UI reacts to exactly what the server said, including the zero-crossing. Both feeders are row handlers the reader has already met: the enemy puppet's `OnEnemyRowInserted`/`OnEnemyRowUpdated` (enemy-8) and the player's `LocalPlayerDataComponent` (combat-7). Nothing on the client consumes those signals yet — see Known gaps.

Death composes pieces that each already existed: `teardown_profile` is lobby-6's profile-row deleter, the `LoggedInPlayer`→`LoggedOutPlayer` swap is join-1's move run backwards, and the client exit is join-6's despawn path — combat adds no new machinery of its own, which is the point of the archetype-helper discipline from [[02 The Component Framework]]. The one genuinely new piece is [[server/spacetimedb/src/player/methods.rs##pub fn internal_gain_xp|internal_gain_xp]]: XP is the dead template's `max_hp / 10` (a boss is worth more because it's bigger, not because it's flagged as a boss), and a level-up heals by exactly the max-HP the new level granted — [[04 Lobby & Profiles]]'s `compute_level` decides the level, and `recompute_stats` reclamps HP against item bonuses afterward, a handoff detailed in [[10 Inventory, Items & Enchantments]].

## Known gaps / stubs

- **The slash/bullet-despawn protocol in `server/spacetimedb/src/plan.md` is entirely unimplemented** (verified against the code). Four facets: (1) there is no `BulletDespawnEvent` table — the name appears only in `plan.md`; (2) there is no `slash_bullet` reducer; (3) [[server/spacetimedb/src/player/reducers.rs##pub fn report_hit|report_hit]] still takes the old single `SequenceStepRef` argument (the generated `ReportHit` binding confirms it), not the planned `(attack, pattern_event_id, bullet_index, chunk_index)`; (4) `BulletPatternEvent` has no `damage` field, so the step defs' `damage` values reach the client only indirectly — combat-6's consequence (a bullet that hits you stays visible to everyone else, and enemy-8's note that step `damage` is stored but dropped from the event) is exactly what the protocol's relay table was designed to fix. The [[server/spacetimedb/src/combat/mod.rs##pub enum PlayerDamageOutcome|comment above the outcome enums]] ("payloads are part of the pipeline API for upcoming callers") is referencing this plan.
- **`PlayerDamageOutcome` / `EnemyDamageOutcome` are dead code.** Both enums in `combat/mod.rs` carry `#[allow(dead_code)]`; both callers ([[server/spacetimedb/src/enemy/reducers.rs##pub fn report_enemy_hit|report_enemy_hit]], `report_hit`) discard the return value. They exist as scaffolding for the planned callers above.
- **`plan.md`'s "current state" section cites stale client names.** It describes `BulletManager.OnEnemyBulletAreaEntered` and a `LocalPlayerCombat` script — the overlap handler has since moved to [[BulletHitRouterComponent.cs##public partial class BulletHitRouterComponent|BulletHitRouterComponent]] (its own docstring says "logic moved out of BulletManager.cs") and the combat script is now `CombatComponent`. The server-side description in the plan is accurate.
- **The observer signals have no subscribers.** `DamageComponent.DidHitReceiver`, `DamageReceivingComponent.DidReceiveDamage`, and all three `HealthComponent.HealthDid*` signals are emitted but nothing in `client/Scripts` or any `.tscn` connects to them (verified by grep) — hooks left for future hit/damage FX. The same is true in spirit for `DidReceiveDamage`'s attacker payload.
- **`WeaponBehavior.pierce` is defined but never read** by any client script — player shots already hit every enemy in every zone, so the flag currently has no behavioral meaning either way.
- **Hit legitimacy is client-asserted.** Neither hit reducer validates range, rate, or line of sight (see the trust discussion above); documented here so it isn't mistaken for an oversight — the plan's protocol assumes the same boundary.

## Where to go next

The weapon stats, inventory slot, and `recompute_stats` pipeline that `compute_player_damage` reads are [[10 Inventory, Items & Enchantments]]. The admin reducers behind `immortal` enemies and hand-spawned bosses are [[12 Admin & Debug]], and the full lifecycle of the rows `teardown_profile` deletes — including the ghost rows it *doesn't* — is [[13 Disconnect & Teardown]].
