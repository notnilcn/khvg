# 06 Combat & Damage Math

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary — `report_enemy_hit`/`report_hit` are reducers like any other, and `ctx.sender()` is the only identity either one can trust.
- [[03 Player System]] for the `LoggedOutPlayer`/`LoggedInPlayer` login-state machine and `join_world`'s scaffolding — this doc's death cascade is the reverse operation of `join_world`, and reuses `try_scaffold_profile`'s idempotent-per-table backfill rather than re-deriving it.
- [[04 Item & Equipment System]] for `Item.behaviors`/`WeaponBehavior`, the `weapon_behavior` lookup helper, and `PlayerStats` — this doc picks up exactly where 04 stopped, at `compute_player_damage`/`compute_incoming_damage`'s named-but-unexplained formulas.
- [[05 Enemy AI & Bullet Patterns]] for `SequenceStepRef`/`SingleStepDef`/`RepeatStepInstance`/`MultiStepDef`, `BulletPatternEvent`, and `report_enemy_hit`'s HP/death/phase/aggro handling once a damage number exists — this doc covers the number itself and the reverse-direction reducer 05 only named in passing.

## The 30-second version

Both directions of combat funnel through a client-side hit-detection step (the player's own zone-based melee-adjacent overlap check, or a BlastBullets2D collision on an enemy bullet) that calls a reducer carrying just enough information to look up a damage number server-side — `enemy_id` for `report_enemy_hit`, a `SequenceStepRef` for `report_hit`. Both formulas are the same shape: a base damage number (a weapon's `damage` field, or a bullet step's own `damage` field) scaled by a flat 0.2%-per-point multiplier off one stat (`strength` scaling damage up, `defense` scaling it down), floored at 1 so nothing ever deals or takes zero. Neither reducer validates range, timing, or hit frequency — the server trusts whatever `enemy_id`/`SequenceStepRef` the client passes exactly as much as it trusts `ctx.sender()` for whose stats to read. On a player reaching 0 HP, `report_hit` cascades deletes across `PlayerData`/`PlayerInventory`/`PlayerStats`/`PlayerPosition`/`ActiveConsumableEffect` and moves the identity back to `LoggedOutPlayer` — the exact reverse of `join_world`'s scaffolding, reusing the same idempotent-per-table backfill on the next join rather than a dedicated "respawn" path. On an enemy reaching 0 HP, `report_enemy_hit` cascades the enemy's own def/instance rows instead and awards XP — already [[05 Enemy AI & Bullet Patterns|05's]] territory, not repeated here.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the "Combat, both directions" fork of the parallel in-world loop.

```sync
![[99 End-to-End Timeline Flowchart#^combat-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-6{seamless:true,title:false,marker:06.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-7{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-8{seamless:true,title:false,marker:08.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-9{seamless:true,title:false,marker:09.}]]
```

## Player-attacks-enemy: zones, `report_enemy_hit`, and `compute_player_damage`

Steps 1–2 above are the client-side detection mechanism, and it's worth being precise about what it actually is, because it isn't hit-scan and it isn't a hitbox on the bullet sprite. `LocalPlayerCombat.Fire` spawns the *visual* bullet through `BulletManager.SpawnPlayerBullet(s)` and, separately, calls `SpawnZonesAlong` to lay a chain of one-shot `Area2D` "zones" spaced `_zoneStep` (`weapon.Range / weapon.ZoneCount`) apart along the same firing line. Each zone is scheduled to check itself — via a `SceneTreeTimer`, not a physics callback — at the exact moment the visual bullet would be passing through that point (`distance-along-path / projectile speed`), then frees itself immediately whether or not it found anything. `ScanZone` calls `ReportEnemyHit(enemy.EnemyId)` once per `Enemy` node caught in that check. Practically: a weapon's damage output is really gated by `ZoneCount` (how many discrete checks its range is divided into) and `FireRate`, not by anything resembling a continuous hitbox — and because each zone is independent, a slow-moving or piercing shot can land more than one `ReportEnemyHit` call against the same enemy from a single trigger pull if the enemy sits across more than one zone's position over the shot's lifetime. See [[LocalPlayerCombat.cs##private void Fire|Fire]], [[LocalPlayerCombat.cs##private void SpawnZonesAlong|SpawnZonesAlong]], and [[LocalPlayerCombat.cs##private void SpawnZone|SpawnZone]].

`report_enemy_hit(enemy_id)` (step 3) is where the damage number [[05 Enemy AI & Bullet Patterns|05]] deferred actually gets computed. `compute_player_damage(ctx, profile_id)`:

1. Looks up `PlayerStats` and `PlayerInventory` for the caller's own profile — if either is missing (shouldn't happen for a logged-in player, but the function degrades to returning `1` rather than panicking).
2. Reads whatever `Item` sits in inventory slot 0 specifically — there's no "currently equipped weapon" pointer independent of slot position, slot 0 *is* the weapon slot by construction (`try_scaffold_profile`, [[03 Player System]]).
3. Pulls a `WeaponBehavior` out of that item's `behaviors` via the same `weapon_behavior` helper [[04 Item & Equipment System|04]] introduced — if slot 0 is empty or holds a non-weapon item, `compute_player_damage` returns `0` outright (an unarmed player's attacks land, but for no damage — there is no innate unarmed damage floor).
4. Otherwise scales the weapon's own `damage: u32` field by the caller's `strength`:

   ```rust
   (weapon.damage as f32 * (1.0 + stats.strength as f32 * 0.002)).max(1.0) as u32
   ```

   That's **+0.2% damage per point of strength**, applied multiplicatively to the weapon's base number, with a hard floor of `1` so a weapon can never be scaled down to 0 (strength only ever scales *up* in this formula — there's no stat that reduces your own outgoing damage). At the game's current level-derived stat range (base 10 at level 1, +1/level, [[03 Player System|03's]] `compute_base_stats`), strength contributes a fairly small percentage swing unless heavily stacked by items/buffs — the formula rewards raw weapon damage far more than raw strength investment at low levels.

See [[methods.rs##pub fn compute_player_damage|compute_player_damage]] and [[reducers.rs##pub fn report_enemy_hit|report_enemy_hit]] (step 8, `enemy/reducers.rs`).

What happens to the `Enemy`/`EnemyBehavior` rows once this number exists — HP subtraction, the death cascade across `EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance`, XP award via `internal_gain_xp`, and the phase/aggro re-check on a non-lethal hit — is entirely [[05 Enemy AI & Bullet Patterns|05's]] "Taking damage" section; this doc doesn't repeat it, only the formula feeding it (step 4 above).

## Enemy-attacks-player: bullets, `report_hit`, and `compute_incoming_damage`

Steps 5–7 are the reverse path. A `BulletPatternEvent` row ([[05 Enemy AI & Bullet Patterns|05's]] step 10) reaching `Enemy.cs` and being forwarded to `BulletManager.SpawnEnemyBullet` is where this side actually starts: `SpawnEnemyBullet` stamps every bullet's `bullets_custom_data` with `new BulletData { SourceStep = bulletPattern.SourceStep }` before building the pattern's spread — so every individual bullet, no matter which `PatternType` spawned it or how many pellets came out of one event, carries its own copy of the `SequenceStepRef` (a `Single`/`Repeat`/`Multi` def id) that produced it, all the way through flight. The BlastBullets2D plugin fires one `area_entered` signal per bullet-vs-`Area2D` collision; `BulletManager.OnEnemyBulletAreaEntered` reads that bullet's `BulletData` back off the signal payload and calls `ReportHit(data.SourceStep)` — the reducer receives exactly the def reference the bullet was born with, not anything recomputed at the moment of collision. See [[BulletManager.cs##public void SpawnEnemyBullet|SpawnEnemyBullet]] and [[BulletManager.cs##private void OnEnemyBulletAreaEntered|OnEnemyBulletAreaEntered]].

`report_hit(ctx, attack: SequenceStepRef)`:

1. Resolves `attack` against whichever table it names to get a `base_damage: u32` — `SingleStepDef`/`RepeatStepInstance` by their own primary key, or `MultiStepDef`'s `shots[shot_index].damage` for the `Multi` case. All three shapes carry their own `damage` field independently ([[def_tables.rs##pub struct SingleStepDef|SingleStepDef]], [[instance_tables.rs##pub struct RepeatStepInstance|RepeatStepInstance]]) — this is the same base number an enemy's phase/sequence data was authored with back at template-seeding time, untouched by anything server-side until this reducer scales it.
2. Passes that through `compute_incoming_damage(ctx, profile_id, base_damage)`:

   ```rust
   (base_damage as f32 * (1.0 - stats.defense as f32 * 0.002)).max(1.0) as u32
   ```

   **-0.2% damage taken per point of defense**, the mirror image of the strength formula above, also floored at `1` — defense can reduce incoming damage arbitrarily close to that floor but never below it, and (unlike `compute_player_damage`) there's no separate "no stats found" early return that skips the reducer entirely — a missing `PlayerStats` row just falls back to `base_damage.max(1)` un-scaled.
3. Subtracts the result from `PlayerData.hp` (`saturating_sub`, so it can't underflow past 0).

See [[reducers.rs##pub fn report_hit|report_hit]] (`player/reducers.rs`).

### Why a flat multiplicative percent-per-point, not a curve

Both formulas are the same shape — `base * (1 ± stat * 0.002)` — which means the scaling is **linear in the stat, not diminishing-returns**: the 50th point of strength or defense is worth exactly as much percentage swing as the 1st. There's no soft cap, no square-root/logarithmic falloff, and no interaction between strength and defense (a high-strength, high-defense character just gets both bonuses independently; they don't offset or compound against each other). The only non-linearity in the whole system is the `.max(1.0)` floor on each formula, which only matters once a scale factor would otherwise cross zero or go negative — at `defense * 0.002 >= 1.0` (defense ≥ 500, far beyond anything the current level-derived stat range produces) incoming damage would floor at exactly 1 regardless of the attack's base damage, effectively becoming defense-capped rather than scaling further. Given the game's actual numbers (base 10 per stat at level 1, +1/level, plus whatever `Flat`-mode item modifiers `recompute_stats` folds in — [[04 Item & Equipment System|04]]), that cap is not something ordinary play reaches; it exists as a mathematical property of the formula, not a tuned design target.

Comparing this against the *design intent* in `docs/Game Design Docs/02 Gameplay.md` (an aspirational, non-technical doc — treat it as intent, not as describing shipped code) is a short mismatch worth naming, not a deep dive: that doc specifies defense as a **flat subtraction** per hit (`max(raw_damage - defense, 1)`), explicitly to make defense counter *many small hits* disproportionately more than *few large hits* (its own worked example: 10 bullets of 10 damage against 5 defense end up doing half the total damage of one 100-damage bullet against the same defense). The shipped `compute_incoming_damage` formula does the opposite of that on purpose or not — a percentage reduction scales *with* the attack's base damage, so it helps equally against a swarm of small hits and one big hit, rather than disproportionately blunting the swarm the way the design doc's flat-subtraction model does. Nothing in the design doc's "damage buffs"/"status conditions" list (shot-count trade-offs, cursed/bleed conditions, nexus-canceling debuffs) exists in the code at all yet — the implemented system covers only the two base formulas and nothing from that list.

## Death and the `LoggedOutPlayer` transition

Step 8 is `report_hit`'s death branch, and it's worth reading directly against two things this doc's assumed reading already covers: `join_world`'s scaffolding ([[03 Player System]]) and `report_enemy_hit`'s own death cascade ([[05 Enemy AI & Bullet Patterns]]). When the HP subtraction above reaches exactly 0, `report_hit`:

1. Deletes `PlayerData`, `PlayerInventory`, `PlayerStats`, and `PlayerPosition` for the profile.
2. Deletes every `ActiveConsumableEffect` row for the profile (any timed buff the player was carrying evaporates, same as `delete_profile`'s cascade does).
3. Deletes the caller's `LoggedInPlayer` row and inserts a `LoggedOutPlayer` row carrying the same `username`/`is_admin` — the identity moves back to the lobby.

Two things this cascade deliberately does **not** touch: `PlayerProfile` itself survives (the character isn't deleted, only its in-world state), and — more subtly — **`PlayerChunk` is never deleted either**, a gap that's easy to miss because nothing in the reducer references it at all. `try_scaffold_profile`'s idempotent per-table checks (`if ... .find(&profile_id).is_none()`) mean that on the next `join_world` for this profile, `PlayerData`/`PlayerInventory`/`PlayerPosition` all get freshly recreated at world origin exactly as they would for a brand-new profile — but the pre-existing `PlayerChunk` row (still pointing at wherever the player died) is left alone, since it already exists and the backfill check only fires on a missing row. The practical effect: a respawned player's `PlayerPosition` reports `(0, 0)` immediately, but the AOI views keyed on `PlayerChunk` ([[02 World & Hex Grid]]'s mechanism) keep serving the death location's chunk ring until the very next `report_movement` call recomputes and corrects it — a window of at most one movement tick (~100ms), not a permanent desync, but a real gap in the "this is a clean fresh join" story `try_scaffold_profile` otherwise tells. `delete_profile` ([[03 Player System]]) has this exact same omission for the same reason — `PlayerChunk` deletion isn't in either reducer's cascade — but it matters less there since the profile itself is gone and nothing will ever read that row again.

This whole sequence is the mirror image of `join_world`'s `LoggedOutPlayer` → `LoggedInPlayer` move plus `try_scaffold_profile`'s inserts ([[03 Player System|03's]] "Joining the world" section) — same two tables, same direction reversed, triggered by an enemy's bullet landing instead of a lobby click. `report_enemy_hit`'s own death cascade ([[05 Enemy AI & Bullet Patterns|05]]) is structurally similar (delete a cluster of rows, one final state change) but operates on the *enemy's* def/instance rows and awards XP rather than moving a login-state row — the two deaths share a shape, not any code.

Step 9 is the client's reaction, and it's the same code path regardless of *why* `LoggedInPlayer` disappeared: `GameManager.OnLocalPlayerDelete` fires on the row deletion itself, not on any dedicated "you died" message, so a death via `report_hit` and a deliberate `leave_world` call both free `LocalPlayer`/`BulletManager`/`TerrainManager`, call `ShowLobby()`, and swap the subscription back to the lobby set — the exact reverse of `OnJoinPressed`. There is no separate death screen, respawn countdown, or death-cause distinction anywhere in this path; from the client's point of view, dying and quitting to the character-select screen look identical. See [[GameManager.cs##private void OnLocalPlayerDelete|OnLocalPlayerDelete]].

## Known gaps and stubs

- **No i-frames or hit cooldown on either side.** Neither `report_enemy_hit` nor `report_hit` rate-limits how often the same enemy or the same player can be hit — the server applies whatever damage the formula computes every single time the reducer is called, with no timestamp check against a previous hit. The only throttling that exists is client-side and incidental: a weapon's `FireRate`/zone geometry naturally spaces out `ReportEnemyHit` calls, and however long BlastBullets2D takes to destroy a spent bullet naturally spaces out `ReportHit` calls. Neither is a server-enforced invincibility window; a modified client calling either reducer in a tight loop would apply damage every call.
- **Neither reducer validates its own input against reality.** `report_enemy_hit(enemy_id)` never checks that the caller is anywhere near that enemy — only that it exists and isn't `immortal` — before applying `compute_player_damage`'s result. `report_hit(attack: SequenceStepRef)` is the more interesting case: it trusts the client-supplied `SequenceStepRef` completely, with no check that this def/instance/shot reference was ever actually sent to this player in a `BulletPatternEvent`, that such an event ever fired, or that the caller was ever near its origin. Per `server/CLAUDE.md`'s SpacetimeDB rule ("`ctx.sender` is the authenticated principal — never trust identity args"), `ctx.sender()` correctly gates *whose* HP/stats get touched in both reducers — but the *content* of the attack (which enemy, which bullet step) is taken entirely on faith. A malicious client could call `report_hit` with a low-`damage` `SequenceStepRef` regardless of what actually hit it (minimizing damage taken), or simply never call it at all (never taking damage), and could call `report_enemy_hit` against any known `enemy_id` regardless of distance. This is a real trust gap, not a hypothetical one — nothing server-side would currently detect either case.
- **No unarmed damage floor.** `compute_player_damage` returns `0` outright, not `1`, when inventory slot 0 holds no weapon (empty or a non-weapon item) — the `.max(1.0)` floor only applies once a `WeaponBehavior` is found. An unarmed player's attacks call `report_enemy_hit` and go through the whole reducer for a guaranteed no-op.
- **The 0.2%-per-point scaling constant lives nowhere near `main/global.rs`.** Every other tunable in the game — HP-per-level, AOI radii, loot expiry — is centralized in `main/global.rs` under its own comment ("single home for every tunable constant, grouped by subsystem"), but the `0.002` multiplier in both `compute_player_damage` and `compute_incoming_damage` is a bare literal written twice, once in each function, in `player/methods.rs`. Retuning damage scaling today means editing two magic numbers in place rather than one named constant.
- **`PlayerChunk` is never deleted on death or profile deletion.** Covered above under "Death and the `LoggedOutPlayer` transition" — worth restating here since it's a genuine stub, not a design choice: neither `report_hit` nor `delete_profile` clears the row, so a respawned profile's AOI briefly (one movement tick) still centers on the death location.
- **No enemy-side equivalent of a "weapon."** `compute_incoming_damage`'s `base_damage` comes from a bullet step's own `damage` field, set once at template-authoring time — there's no enemy-side stat (an "attack power") that scales it the way `strength` scales the player's side. Enemy damage output is entirely per-template, per-step tuning; only the player's incoming-defense side of the formula is stat-driven.

## Where to go next

[[07 Client Rendering & Camera]] is the next doc in reading order — it covers the 3D/2D presenter split this doc's client-side bullet/zone visuals sit on top of, and why only the player's own model gets a 3D representation. If you want the other unfinished branch of the parallel in-world loop instead, the "Camera and rendering, spectating all of it" section of [[99 End-to-End Timeline Flowchart]] is where that doc's own numbered steps will land.
