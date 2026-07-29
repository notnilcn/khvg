# 07 Combat & Damage Math

## Assumed knowledge

[[01 Architecture & Sync Model]] — tables, reducers, subscriptions, and views are used throughout without re-explaining them. [[02 Entity & Component Framework]] — the archetype-helper pattern (`teardown_profile`/`despawn_enemy_archetype`), both of which this doc's death/kill handling calls directly. [[04 Player System|04]] — `PlayerData`/`PlayerStats`, the leveling formulas, and `internal_gain_xp`, which this doc's kill path is the trigger for. [[05 Item, Equipment & Enchantment System|05]] — `recompute_stats`'s output (the final `PlayerStats` row) is this doc's *input*, not something re-derived here; also `WeaponBehavior`/`ItemBehavior::Weapon` and `CombatComponent`'s equipped-weapon resolution, which this doc picks up from where it fires. [[06 Enemy AI & Bullet Patterns|06]] — `BulletPatternEvent`'s event-table semantics, `SequenceStepRef`'s def-or-instance bullet pricing (`report_hit` re-derives it, doesn't re-price it), and the phase/aggro consequences `deal_damage_to_enemy` triggers into enemy behavior state.

## The 30-second version

Every damage number in the game — in both directions — is computed by exactly one file, `server/spacetimedb/src/combat/mod.rs`: `compute_player_damage`/`compute_incoming_damage` are the two formulas, `deal_damage_to_player`/`deal_damage_to_enemy` apply them and handle what happens at zero HP, and `heal_player` is the third, simpler mutator. Two reducers are the only entry points — `report_hit` (an enemy bullet landing on a player) and `report_enemy_hit` (a player's attack landing on an enemy) — and both do nothing but resolve a base damage number and delegate. The two directions get there by genuinely different client-side mechanisms: a player's outgoing weapon fire spawns a cosmetic, non-colliding bullet purely for visuals, alongside a separate, invisible `HitZone` timed to the same path that does the actual hit detection; an enemy's bullet, by contrast, *is* its own hit detector, reported the instant its BlastBullets2D collision shape overlaps the player. Both formulas mitigate by a flat 0.2%-per-point stat scaling (Strength for outgoing, Defense for incoming), floored at 1 damage either way. Zero HP means two very different things depending on which side of the fight it happens to: an enemy at zero is deleted along with its entire behavior tree and awards XP; a player at zero is a full permadeath — every scaffolded row for that character is torn down and the player is dropped back to the lobby, though the profile slot itself survives for a fresh restart.

## System flowchart

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
```sync
![[99 End-to-End Timeline Flowchart#^combat-10{seamless:true,title:false,marker:10.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-11{seamless:true,title:false,marker:11.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^combat-12{seamless:true,title:false,marker:12.}]]
```

## Main body

### One pipeline, two directions, three verbs

`combat/mod.rs`'s own header comment states the design intent plainly: "the single place where damage is computed, mitigated, and applied." Every other reducer that deals damage — today just the two named above — resolves a *base* damage number from its own domain (a weapon's stats, a bullet's fired-from step) and hands off; nothing outside this one file ever subtracts from an `hp` field directly. The module splits cleanly into three kinds of function: **formulas** (`compute_player_damage`, `compute_incoming_damage` — pure math, no writes), **appliers** (`deal_damage_to_player`, `deal_damage_to_enemy` — call a formula, subtract, and own everything that happens next, including death/kill side effects), and one **plain mutator** (`heal_player`, addition with a clamp, no formula to speak of). `combat-1` through `combat-6` above walk the outgoing (player→enemy) direction; `combat-7` through `combat-11` walk incoming (enemy→player); `combat-12` covers healing, which is direction-agnostic.

### Why outgoing and incoming hit-detect completely differently

The two directions could have shared one mechanism — bullets on both sides use the same BlastBullets2D bridge ([[06 Enemy AI & Bullet Patterns|06]]) — but they don't, and the split is deliberate rather than incidental. An enemy's bullet is server-driven content: it exists because a `BulletPatternEvent` row said so, and its `SequenceStepRef` is baked in at spawn time, so the bullet itself is the natural, self-sufficient thing to report a hit (`combat-7`). A player's outgoing damage isn't tied to any one shot at all — `compute_player_damage` (`combat-4`) reads whatever's presently equipped in inventory slot 0 at the moment the hit is reported, not anything carried by the projectile — so there is no equivalent reason for the *cosmetic* bullet to carry hit logic, and it doesn't. Instead `CombatComponent.Fire` spawns two unrelated things per shot along the same trajectory: a visible, uncollidable bullet through `BulletManager`, and a train of invisible `HitZone` probes (`Scenes/Components/hit_zone.tscn`) whose lifetime timers are individually tuned so each one's fuse expires exactly when the cosmetic bullet would have reached that point in space (`combat-1`). A `HitZone` never asks "did the bullet I represent actually get there" — it just checks who else is standing in that spot when its own clock runs out, which is visually indistinguishable from the bullet doing the hitting but implemented as two independent timers agreeing by construction, not one object driving the other.

One practical consequence: because `DamageComponent.ReportHits()` (the method every `HitZone` inherits) reports *every* opposed receiver currently overlapping it in one pass, a single zone can and does hit multiple enemies standing in the same spot — there's no per-shot cap, and `WeaponBehavior.pierce` (seeded on the Bow, `false`) is read nowhere in the codebase to turn that off (Known gaps). [[item/tables.rs##pub struct WeaponBehavior {|WeaponBehavior]]

### The two formulas, and where they stop being symmetric

`compute_player_damage` and `compute_incoming_damage` read as mirror images — one scales up by the attacker's Strength, the other scales down by the victim's Defense, both at the same `0.002` (0.2%) per stat point, both floored with `.max(1.0)` so a hit is never fully absorbed to zero:

- Outgoing: `(weapon.damage as f32 * (1.0 + strength * 0.002)).max(1.0) as u32`
- Incoming: `(base_damage as f32 * (1.0 - defense * 0.002)).max(1.0) as u32`

They diverge in one place worth naming: neither `0.002` coefficient lives in `main/global.rs`, the module every other tunable in this codebase (`BASE_MAX_HP`, `HP_PER_LEVEL`, AOI radii, tick rates) is centralized in — they're inline literals in `combat/mod.rs` (Known gaps). A defense-stacked player can push incoming damage arbitrarily close to `1` but never below it; the same floor exists on the outgoing side so a weaponless attacker's `0` base (see below) never becomes an actual hit at all rather than a degenerate `1`.

`compute_player_damage` has two distinct zero-ish outcomes that read the same in a damage log but mean different things: no `PlayerStats`/`PlayerInventory` row at all (a profile mid-teardown, effectively impossible to observe in normal play) returns a flat `1`; a valid profile with nothing equipped in slot 0 returns an explicit `0` — a real, intentional "this hit does nothing" rather than a fallback minimum. `report_enemy_hit`/`deal_damage_to_enemy` don't special-case that `0` at all — it just subtracts to a no-op.

### Kill vs. survive: what `deal_damage_to_enemy` does with the result

Past the formula, `deal_damage_to_enemy` (`combat-5`) is entirely about the branch at zero. A kill calls `despawn_enemy_archetype` — deleting the enemy's entire behavior tree (`EnemyBehavior`/`EnemyPhase`/`EnemyAttack`/`EnemySequenceStep`/`RepeatStepInstance`), not just the flat `Enemy` row, mirroring `spawn_enemy_archetype`'s own construction one-for-one ([[06 Enemy AI & Bullet Patterns|06]]'s `enemy-3`) — then awards `template.max_hp / 10` xp via `internal_gain_xp`, the trigger [[04 Player System|04]] flagged as deferred to this doc. A survived hit instead recomputes the enemy's phase from its template's HP thresholds and, only on an actual phase crossing, force-re-aggros onto whoever's nearest regardless of the existing lock; short of a phase change, the enemy re-aggros onto the attacker specifically, but only once its own `aggro_locked_until` has already expired on its own. Both re-aggro paths write into the enemy's `EnemyBehavior` row, which is [[06 Enemy AI & Bullet Patterns|06]]'s data to own — this doc's job is only to say what triggers them and when.

Client-side, a kill has exactly one observable effect: the `Enemy` row's delete event frees the puppet node (`combat-6`). There's no death animation, no particle burst, no loot-drop insert tied to the kill (nothing in this project's enemy-cleanup path creates a `LootDrop` — loot only ever enters the world via a player's own `drop_item`, [[05 Item, Equipment & Enchantment System|05]]'s `equip-6`), and no on-screen damage-number feedback for either direction of combat at all — the only visual signal a hit happened is the `HealthComponent`'s mirrored value itself changing.

### Permadeath: what actually gets deleted, and what survives it

`deal_damage_to_player` (`combat-10`) is where "permadeath" — named in this project's own one-line pitch (root `CLAUDE.md`) — is actually implemented, and the implementation is narrower than the word suggests. `teardown_profile` deletes five rows: `PlayerData`, `PlayerStats`, `PlayerInventory`, `PlayerPosition`, `PlayerChunk`, plus any `ActiveConsumableEffect`s. It does not delete `PlayerProfile` — the character's *name* and profile id survive. Combined with `join_world`'s own scaffolding logic ([[04 Player System|04]]'s `join-2`, "every one of those five `is_none()` checks"), this has a precise consequence: a profile that just permadied looks, to `join_world`, identical to a profile that has never joined the world at all — both have a `PlayerProfile` row and nothing else. Clicking Join on that same profile again doesn't resume anything; it re-scaffolds a brand new level-1 character with the starter loadout, under the name the dead character had. Permadeath here means losing a character's progress, not losing the roster slot it lived in.

The identity-level move — deleting `LoggedInPlayer`, inserting `LoggedOutPlayer` with the same `username`/`is_admin` — is the same shape `client_disconnected` performs ([[01 Architecture & Sync Model|01]]'s `end-2`), which is worth contrasting explicitly since both look like "a `LoggedInPlayer` row disappeared" from the same table: a disconnect touches only that one row and leaves the other five profile rows intact, ready to resume; a death runs `teardown_profile` first and leaves nothing but the bare profile behind. Two different amounts of cleanup, dispatched from two entirely different triggers, converging on the same table.

It's also the identical row-level effect `leave_world` (`player/reducers.rs`) produces — delete `LoggedInPlayer`, insert `LoggedOutPlayer`, no `teardown_profile` call — which exists in the reducer set but is never called from anywhere in the client (Known gaps), so in current gameplay the only way a connected client's `LocalPlayer` ever disappears out from under it, while the connection itself stays open, is a combat death.

### The client-side death signal, and the guard that keeps it from firing mid-disconnect

`EntitySpawnerComponent.OnLocalPlayerDelete` (`combat-11`) is what actually reacts to a `LoggedInPlayer` row vanishing: it frees `LocalPlayer` and `BulletManager` unconditionally, then checks `Conn.IsActive` (the SDK connection object's own live-connection flag) before doing anything UI-facing — showing the lobby, resubscribing the lobby wave, unsubscribing the game wave. That guard is what separates the two situations this same delete event can represent: during an ordinary combat death the connection is still fully alive, so the guard passes and the player lands back in the lobby able to pick a profile and rejoin immediately; during a genuine disconnect, the row delete and the connection teardown race each other, and the guard exists specifically so this handler doesn't try to drive lobby UI on a connection that's already going away ([[01 Architecture & Sync Model|01]]'s `end-4` covers the scene-teardown side of that same moment).

### Healing

`heal_player` (`combat-12`) is the simplest function in the module by a wide margin — `(hp + amount).min(max_hp)`, silently doing nothing if the profile has no `PlayerData` row. It has exactly one caller today, `apply_consumable_effect`'s `Heal` branch ([[05 Item, Equipment & Enchantment System|05]]'s `equip-8`), reached when a player eats/drinks an item carrying `ConsumableEffect::Heal`. This doc's job stops at documenting the function; the trigger, and why it's a behavior test rather than a slot test, is 05's.

### An API built for callers that don't exist yet

`PlayerDamageOutcome`/`EnemyDamageOutcome` — the two enums `deal_damage_to_player`/`deal_damage_to_enemy` return (`Alive { hp }` / `Died`, and `Alive { hp, phase }` / `Died { xp_reward }`) — are both marked `#[allow(dead_code)]`, and the module's own header comment explains why: they're "part of the pipeline API for upcoming callers," not something either of today's two reducers actually branches on. That's confirmed by reading the call sites, not just the comment: `report_hit` calls `deal_damage_to_player(ctx, &player, base_damage)?` — the `?` only propagates the `Err` case, the `Ok(PlayerDamageOutcome)` payload is discarded either way — and `report_enemy_hit` calls `deal_damage_to_enemy(ctx, player.profile_id, enemy)` with no assignment at all. The comment's claim that "today's reducers only branch on the variant" doesn't hold up against either call site as written today (Known gaps) — the return shape exists for a future caller (a kill-feed message, a client-visible "you died" event distinct from the row delete) that hasn't been written yet.

## Known gaps / stubs

- **`WeaponBehavior.pierce` is unused.** The field is seeded (`false` on the Bow) and exists in the schema, but nothing anywhere — server or client — reads it. A `HitZone` already reports every opposed receiver it overlaps in one pass with no cap, so there's no "non-piercing" behavior implemented for the flag to toggle off in the first place.
- **`HealthComponent.HealthDidZero` has no subscriber.** It fires correctly the instant a mirrored `hp` value hits `0` (for both `LocalPlayer` and `Enemy`, since both share the same `HealthComponent`), but nothing in the codebase listens to it — the actual on-screen consequence of a death or kill comes entirely from the row-delete event a moment later (`OnLocalPlayerDelete`/`OnEnemyDelete`), not from this signal.
- **`leave_world` is never called from the client.** The reducer exists, does the same `LoggedInPlayer`→`LoggedOutPlayer` move `client_disconnected` and a combat death both perform (minus `teardown_profile`), but no button, key, or menu action in the client fires it — there's no voluntary "return to lobby without dying" path today.
- **The `0.002` Strength/Defense damage coefficients are hardcoded in `combat/mod.rs`**, not grouped with the rest of this project's tunables in `main/global.rs` the way `BASE_MAX_HP`/`HP_PER_LEVEL`/every AOI radius and tick rate are.
- **`PlayerDamageOutcome`/`EnemyDamageOutcome` are computed and discarded.** Both are `#[allow(dead_code)]`, and neither of today's two entry-point reducers reads the value it gets back — the module comment's claim that current reducers "branch on the variant" doesn't match either call site.
- **No damage-number or hit-feedback UI exists for either direction.** A hit's only visible trace is the `HealthComponent` mirror changing (and, on the enemy side, an eventual row delete) — no floating combat text, no screen flash, no hit sound hookup is wired anywhere in this doc's read of the client.
- **No crit, resistance, or elemental system.** Damage is a single flat number per hit, mitigated by exactly one stat per direction — anything beyond that (crit chance, damage types, status effects/DoT — the same gap [[05 Item, Equipment & Enchantment System|05]] already flags for `ConsumableEffect`) is absent from the code, not merely undocumented.

## Where to go next

[[08 Client Rendering & Camera]] covers the camera/rendering half of "spectating all of it" this doc's combat steps run alongside — none of the hit/health visuals above touch camera state directly, but every entity this doc damages or despawns is something the camera system is simultaneously rendering. [[09 Admin, Debug & World Lifecycle]] is where the `immortal` flag this doc's `report_enemy_hit` no-ops on (admin-set, [[06 Enemy AI & Bullet Patterns|06]]'s territory) is actually turned on.
