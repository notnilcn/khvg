# Plan: Implement all abilities from `docs/Game Design Docs/Items/Abilities/Abilities.md`

> **This is a phase plan, not standing guidance.** It drives the P0–P6 ability implementation batches. Archive or delete it once P6 lands; durable conventions it establishes (status module, tick schedule, event-binder rule) must be moved into `server/AGENTS.md` / `client/AGENTS.md` at that point (P6 includes this).
>
> Revised after hostile review. Key changes from the original draft: new **P0 combat refactor** (the original assumed a damage-parameterized `deal_damage_to_enemy` that does not exist), bleed drain no longer bypasses the damage pipeline, status row schema carries real params, DamageShare is live zone membership instead of a carried status, wagers are corroborated by `player_hit_flag`, new runtime rows get a full lifecycle story, SelfSlow is server-enforced, Enraged fire-rate is priced, grapple's client cost is itemized, and P1 is split in three.

## Locked decisions (from Q&A, amended)

- **Phased by subsystem**, not by class. Each phase is independently shippable (cargo check + publish + dotnet build + manual test).
- **Bleed = mark-based, sheddable, reduced.** Ability marks a specific enemy; only damage **from that enemy** converts to a bleed pool: 70% of the hit goes into the pool (drains over ~5s), 30% is instant. One ability item per purge condition (4 total): (1) no damage taken for ~2s, (2) stay within radius of marked enemy for ~5s, (3) deal x damage to the marked enemy (x=1 for now), (4) marked enemy dies.
- **Store-damage = mark-based wager.** Mark an enemy; damage dealt **to that enemy** is stored during the window. One ability item per wager (3 total): (1) no-hit wager — 0 hits taken = 2×, 1–2 hits = 1×, **3–4 hits = 1×**, 5+ hits = 0×; (2) radius wager — never leave r1 = 2×, never leave r2 = 1×, ever leave r2 = 0×; (3) damage wager — stored ≥ x = 2×, ≥ z = 1×, < z = 0×.
  - Stored damage is **post-mitigation at hit time, released flat** (no second defense subtraction at release; no vulnerability double-dip).
  - Marked-enemy death **forfeits** the unsettled pool (explicit decision).
  - The no-hit wager is settled against `player_hit_flag` corroboration (see Trust model).
- **One active mark per player.** Recasting any bleed/store ability replaces the player's existing mark row. Replacing a bleed mark **dumps the remaining pool instantly** (no free shed); replacing a store mark forfeits the pool. This kills all multi-pool intercept ambiguity.
- **AoE ally effects target a radius around the caster.** No party system.
- **Hold-to-charge**: key-down starts charge (self-slow), key-up releases. New begin/release reducer pair. Max charge duration enforced on the tick; begin while charging is rejected; cooldown/charges are spent **at release**; death/logout/disconnect cancels the charge with no cooldown spent.
- **Deferred:** archer's shootable dummy, trickster decoy, trickster invisibility (all need enemy retargeting onto non-player entities — their own future batch).
- **Stagger:** two bash items (stagger+stun; stagger+stun+armor-break). Stun behavior is per-enemy-template: `Pause` (attacks+movement stop) / `Slowed` / `Enraged`.
- **Trickster poisons/debuffs** reuse the shared enemy-debuff functions as single-target items.
- **Lifecycle (new):** `active_status_effect`, `ability_charge`, and `ground_zone` rows are purged on death (`teardown_profile`), `leave_world`, and `client_disconnected`. No status may kill or tick a logged-out player.

## Design pitches (open-ended decisions — flag if you disagree)

1. **Store-damage damage is *withheld*, then released.** During the window your hits on the marked enemy accumulate in a pool instead of dealing damage; at expiry the pool hits the marked enemy at 2×/1×/0× per the wager.
2. **No-hit wager gap:** 3–4 hits = normal (doc specified only 0 / 1–2 / 5+).
3. **Damage wager band normalization:** doubled if stored ≥ x, normal if ≥ z, negated if < z.
4. **Mark targeting is cursor-pick:** the ability targets the enemy nearest the (range-clamped) cursor point within a ~100 px pick radius, resolved server-side via a new `enemies_near_point` helper (P0). Client shows the same pick optimistically; on divergence the server row wins (mark indicator is server-echoed).
5. **Warrior "damage dealt *and* received → bleed":** incoming side is the standard bleed pool vs the marked enemy; outgoing side converts your hits on the marked enemy into an enemy DoT (100% of the hit, ticking over 3s) instead of instant damage. This is a `outgoing: bool` on `MarkBleedParams`, **not** a separate `StatusEffectKind` variant.
6. **Paladin smite release:** stored pre-mitigated damage is released as an AoE burst at the cursor point (range-clamped). Tank's version: at window expiry the pool freezes and the row re-arms with a long hold (~30s); the next shield bash consumes the pool into its damage and stagger.
7. **Status uniqueness rule:** one active row per (profile, kind-family) where the family is bleed-mark / store-mark / each self-buff kind. Re-applying the same kind **refreshes** (replaces params/remaining). The one-active-mark cap (locked decisions) means kind+payload uniqueness never arises for marks.
8. **Volley "strong vs low-defense":** the volley applies its damage as N separate small `apply_damage_to_enemy` calls, so flat enemy defense eats each pellet. Each pellet runs full mitigation; a kill mid-volley makes remaining pellets no-ops (guard on missing enemy row). The charge shot is one big hit.
9. **Kill credit:** enemy DoT ticks carry `source_profile_id` for XP. Aggro on tick-kills uses the source's identity **if logged in**, else no retarget (never `ctx.sender()` — it's meaningless in scheduled reducers).
10. **Charge-shot abilities are server-computed spell damage**, not weapon-pattern modifications. `SpellParams` carries a `scaling: ScalingStat` — wizard spells scale off wisdom, bullet-master charge/volley off dexterity (formula `damage × (1 + stat × 0.002)`, mirroring the str formula). Hardcoding wisdom for a dex class is rejected.
11. **Absorbed hits still count as hits.** A hit fully eaten by Invulnerable/DamageShield increments the wager `counter` and stamps `last_event_at` — a hit is a hit.
12. **Deviations from the doc, labeled:** meteor shrapnel and the converge spell are **visual-only** (damage is the single circle); fireball's "forces the player to stop shooting" **is** implemented (fire suppressed while any charge is held — client-side, since firing is client-authoritative); enemy DoT ticks are defense-mitigated per tick (min 1), so DoT is weak vs armor — consistent with volley pellets.

## Trust model (accepted risk, stated plainly)

Incoming player damage is 100% self-reported via `report_hit` (`player/reducers.rs`), and `report_enemy_hit` has no bullet-identity validation. Wagers monetize that honesty. Mitigations in this plan:

- `report_hit` gains `enemy_id` (below); server validates the enemy exists and is near the player. A cheater can still under-report hits.
- The no-hit wager settles against `player_hit_flag` (anticheat witness table — currently write-only; this plan adds its **first consumer**): witness flags for the victim during the window above a threshold clamp the multiplier to ≤1×. Residual exploit (solo play with no witnesses) is accepted and documented.

## Architecture: P0 refactor + 5 shared subsystems

The "duplicated buffs share one function" requirement drives everything. One new server module `server/spacetimedb/src/status/` (`tables.rs` / `methods.rs` / `reducers.rs`) hosts subsystems 1–4; all targeting variants (self / allies-in-radius / zone) call the same `apply_*` functions.

**Statuses vs consumables — the line:** `ActiveConsumableEffect` stays what it is — plain stat buffs pre-baked into `StatModifier`s, folded by `recompute_stats`. `ActiveStatusEffect` is for anything with **runtime state** (pools, counters, marks) or **non-stat effects** (reduction %, invuln, floors) that need consult points in the damage pipeline. `Regen`/`DamageDealtMult`/`DamageReductionPct` live as statuses even though a stat-buff approximation exists; the overlap with the `DamageDealer` flat buff is acknowledged and accepted (different mechanism: multiplicative at deal-time vs additive flat stat).

### P0 — combat refactor prerequisite (no user-visible change)

1. **Split `deal_damage_to_enemy`** (`combat/mod.rs:79`) — it currently computes damage internally from the attacker's equipped weapon and takes no amount. Into:
   - `apply_damage_to_enemy(ctx, enemy, amount: u32, mitigation: MitigationMode, xp_credit: Option<u64>, aggro_from: Option<Identity>)` — owns enemy-status mitigation consult (ArmorBreak → defense 0; Vulnerability → ×(1+mag)), kill → `despawn_enemy_archetype` + `internal_gain_xp` for `xp_credit`, phase recompute, re-aggro onto `aggro_from` (skip when `None`). `MitigationMode` = `Normal` (defense + statuses) | `FlatRelease` (skip defense; store-damage release — already mitigated at hit time).
   - `deal_damage_to_enemy` stays as the weapon-path wrapper: `compute_player_damage` → delegate with `(Normal, Some(profile_id), Some(ctx.sender()))`.
2. **`deal_damage_to_player` gains an internal flags path** — `{ skip_conversion: bool, respect_floor: bool }`; public behavior unchanged. Bleed-pool drains call it with `(true, true)`: no re-conversion of its own ticks, but HpFloor still applies and death cleanup (`teardown_profile`) runs. **No direct HP writes anywhere** — death cleanup lives only in this function (`combat/mod.rs:64-69`).
3. **Reducer-side AOI helpers** (new): `enemies_near_point(ctx, x, y, radius)` using the `Enemy.chunk_index` btree (`instance_tables.rs:100`) + wrapped distance; players-near-point following the `nearby_player_position_chunks` precedent (`enemy/methods.rs:14-18`). The view-side `nearby_indices` helpers are `ViewContext`-only (`player/views.rs:10-21`) and cannot serve reducers.
4. **`teardown_profile` extended** (`player/methods.rs:350-362`): add `active_status_effect`, `ability_charge`, `ground_zone` purge — and fix the pre-existing `player_rotation` leak while there.
5. **`leave_world` / `client_disconnected`** purge the same three runtime tables for the departing profile.
6. **Tick rename:** `tick_active_consumable_effects` → `tick_status_effects` (same single `Interval(1s)` schedule row created in `init`; rename noted in AGENTS.md, bindings regen via `server/build.sh`).

Verify: `cargo check`, `server/build.sh`, `dotnet build client/khvg.csproj`, manual smoke of existing combat/consumables.

### 1. Player status effects (warrior/paladin/tank/trickster buffs)

```rust
#[table(accessor = active_status_effect, public)]
pub struct ActiveStatusEffect {
    #[primary_key] #[auto_inc] pub effect_id: u64,
    #[index(btree)] pub profile_id: u64,
    pub kind: StatusEffectKind,
    pub params: StatusParams,    // struct-wrapped payload mirroring kind: purge/wager
                                 // thresholds (r1, r2, x, z), conversion, pool_seconds,
                                 // per_second, pct, release spec… — cast-time values the
                                 // tick/intercept need later. NOT a generic aux f32.
    pub pool: f32,               // bleed pool / stored damage pool
    pub counter: u32,            // hits taken during window
    pub violated: bool,          // radius wager: r2 was exited (stamped from report_movement)
    pub remaining: f32,
    pub marked_enemy_id: u64,    // 0 = none
    pub source_profile_id: u64,  // redirect target = the paladin, etc.
    pub last_event_at: Timestamp,// last damage taken / last radius-exit
}

pub enum StatusEffectKind {
    DamageToBleed,        // params carry PurgeKind + conversion + pool_seconds (+ outgoing flag)
    StoreDamage,          // params carry WagerKind + thresholds
    Invulnerable,
    DamageShield,         // absorbs next single hit
    DamageReductionPct,
    DamageDealtMult,
    HpFloor,              // exists only for the witch-doctor zone (honest framing)
    DebuffImmune,         // exists only for the witch-doctor zone
    RedirectToCaster,
    Regen,
    StoreIncomingSmite,
    StoreIncomingBash,
    SelfSlow,
}
```

**The one shared entry point:** `status::methods::apply_status_effect(ctx, profile_id, kind, params)`. Self-casts call it once; paladin/trickster AoE casts iterate `logged_in_player`s within radius (P0 players-near-point helper) and call the same function per ally. Warrior's bleed and paladin's AoE bleed are literally the same call with different targeting.

**Interception hooks** (the only two damage sinks), with a **fixed evaluation order** — not an unspecified "verdict":
- `deal_damage_to_player` → `status::methods::intercept_incoming(ctx, profile, source_enemy_id, base_damage)` before mitigation, evaluated as: `Invulnerable` → `DamageShield` (consumes itself) → `RedirectToCaster` (re-route raw hit to the source paladin; loop-guarded — redirected hits can't re-redirect) → **zone DamageShare** (live membership: split across players whose *current position rows* are inside the share zone's radius — no carried status, no zone-identity problem, no stale 1 Hz membership) → `DamageReductionPct` → `DamageToBleed` (matching `marked_enemy_id`: 70% to `pool`, 30% proceeds) → mitigation → `HpFloor` post-mitigation clamp. **Every** hit — even fully absorbed — increments `counter` and stamps `last_event_at`.
- `apply_damage_to_enemy` → consults attacker's `StoreDamage` (withhold into `pool` when target == marked enemy), attacker's `DamageDealtMult`, and target's enemy statuses (subsystem 2). On kill, purge all statuses referencing that `enemy_id` (`OnKill` purge; wager pools forfeit — locked decision).

**Ticking** (`tick_status_effects`, 1 Hz): drains bleed pools via the flagged `deal_damage_to_player` path (P0); applies Regen via `heal_player`; evaluates purges — no-damage-2s via `last_event_at` (tick-based; it's time-since, not sampling), stay-close via accumulated seconds-in-radius, deal-x via the damage counter; settles wagers (release `pool` × multiplier at the marked enemy via `FlatRelease`; NoHits clamps against `player_hit_flag`); sweeps expired rows and timed-out `AbilityCharge`s; (P2) decays stagger; (P3) ticks zones.

**Radius exits are stamped from `report_movement`**, not sampled at 1 Hz: each movement report checks the player's active radius-mark rows against the marked enemy's position and sets `violated` / stamps `last_event_at` on exit. Darting out and back between ticks cannot cheat the "never leave r2" wager. The same clamp site in `report_movement` enforces **SelfSlow**: speed clamps to `base_speed × factor` while the status row is present (the client-side slow is cosmetic without this).

### 2. Enemy statuses + stagger (tank, trickster, bullet-master, witch doctor)

```rust
#[table(accessor = active_enemy_status_effect, public)]
pub struct ActiveEnemyStatusEffect {
    #[primary_key] #[auto_inc] pub effect_id: u64,
    #[index(btree)] pub enemy_id: u64,
    pub kind: EnemyStatusKind, // DoT | Slow | Vulnerability | ArmorBreak | Stunned
    pub magnitude: f32,
    pub remaining: f32,
    pub source_profile_id: u64,
}
```

Shared entry point: `status::methods::apply_enemy_status(ctx, enemy_id, kind, magnitude, duration, source)`. Consulted in: `apply_damage_to_enemy` (ArmorBreak → defense 0; Vulnerability → ×(1+magnitude) — so weapon, spell, DoT, and zone damage all respect them), `enemy/methods.rs::apply_movement` (Slow → displacement ×(1−mag) at the three behavior arms), and the behavior tick (Stunned per template `stun_behavior`: `Pause` → skip attacks+movement; `Slowed` → treat as Slow; `Enraged` → movement and fire-rate ×(1+mag)). DoT ticks call `apply_damage_to_enemy` with `Normal` mitigation, `xp_credit = source_profile_id`, `aggro_from` = source's identity if logged in.

**Enraged fire-rate is priced:** fire timing is wall-clock comparisons in ~6 sites across `tick_sequence`/`tick_phase` plus the catch-up loop — there is no choke point today. Introduce `effective_interval(base: f32, enemy_id) -> f32` (consults Enraged) and convert all sites. If that conversion turns ugly, Enraged v1 is movement-only and the plan says so — no hand-wave.

**Stagger:** add `stagger: f32` to `Enemy` (runtime bar, 0–100; decays on the status tick — a per-second scan of enemies with stagger > 0, acceptable at current enemy counts) and `stun_behavior: StunBehavior` to `EnemyTemplate` (default `Pause` for existing seeds). Bash adds stagger; at 100 → apply `Stunned` for `BashParams.stun_seconds` per the template's behavior + reset bar.

### 3. Ground zones (all 11 witch doctor circles + wizard spire)

```rust
#[table(accessor = ground_zone, public)]
pub struct GroundZone {
    #[primary_key] #[auto_inc] pub zone_id: u64,
    pub owner_profile_id: u64,
    pub kind: ZoneKind,
    pub x: f32, pub y: f32, pub radius: f32,
    pub magnitude: f32, pub remaining: f32,
}
```

`ZoneKind`: `DebuffImmunity`, `DamageReduction`, `HpFloor`, `HealOverTime`, `BurstHeal`, `DamageBuff`, `EnemyDot`, `EnemyBurst`, `EnemySlow`, `EnemyVulnerability`, `DamageShare`. All but `DamageShare` map onto the shared status/enemy-status/heal functions per tick (HpFloor/DebuffImmune exist *only* for zones — no false reuse claims). `DamageShare` is **not** applied as a status: `intercept_incoming` checks at damage time whether the victim stands in a share zone and splits across live occupants (see subsystem 1). Zone-applied statuses get ~1.5s `remaining` refreshed each tick so leaving sheds them. Burst variants fire once at expiry via `apply_damage_to_enemy` / `heal_player`, then delete the row. Zones die with their owner **and** on owner logout/disconnect (P0 lifecycle). The wizard's offensive spire is a seeded `EnemyDot` zone.

### 4. Charge + movement (bullet master, wizard fireball, warrior/trickster teleport, grapple)

```rust
#[table(accessor = ability_charge, public)]
pub struct AbilityCharge {
    #[primary_key] pub profile_id: u64,
    pub slot_index: u32,
    pub started_at: Timestamp,
}
```

`begin_ability_charge(slot_index)` validates like `activate_ability` (rejects if a charge row already exists; no cooldown spent yet) and applies `SelfSlow`. `release_ability_charge(slot_index, target_x, target_y)` computes `charge_seconds`, deletes the row, removes `SelfSlow`, dispatches the effect scaled by charge, then sets cooldown/charges. The tick sweeps charges older than `MAX_CHARGE_SECONDS` (auto-release at current charge — no free indefinite SelfSlow). Death/logout/disconnect purge the row (P0). While any charge row exists, the local client's `CombatComponent` holds fire (doc's fireball requirement; client-side because firing is client-authoritative). Client: `InventoryPanel` gains `IsActionJustReleased` handling (abilities are press-edge-only today — new input plumbing), and `PositionSyncComponent` gains a **public** slow API (the existing `speedScale` is private with no setter; don't touch it).

**Teleport** (warrior windup = charge-scaled range; trickster = instant): server validates range, wraps coordinates, writes `PlayerPosition` + `chunk_index` + `PlayerChunk` exactly like `report_movement` does. The existing 50px desync snap (`PositionSyncComponent.cs:80-96`) corrects local and remote puppets; no extra report needed (next scheduled/edge report carries the new position).

**Grapple** (bullet master): client-tweened; server arm only sets cooldown. Itemized client work (the original draft's "frequent `ReportNow`" glossed over all of this): timed movement reports run at **1 Hz** (`local_player.tscn:1472` override) and `ReportNow` is private — add a public report/trigger method and call it on tween frames; zero velocity and suspend input movement during the tween so `MoveAndSlide` (`LocalPlayer.cs:136-144`) doesn't fight the externally-set position; accept that remotes see a snap (their 50px `WrapSnapThreshold` self-corrects; their dead-reckoning clamps to `base_speed` mid-tween).

### 5. Spell damage + visuals (wizard, bullet-master charge/volley)

`AbilityEffect::Spell(SpellParams { shape: SpellShape, damage: f32, hits: u32, charge_scaled: bool, scaling: ScalingStat })` where `SpellShape` is `Circle { radius }` / `Line { length, width }` / `Cone { length, spread }` (struct-wrapped). Server arm: query enemies in the shape (P0 `enemies_near_point` + geometry test), compute `damage × (1 + scaling_stat × 0.002)`, call `apply_damage_to_enemy` per enemy (`hits` times with divided damage for volley, per-pellet mitigation, dead-enemy guard).

**Visuals:** append-only `AbilityVisualEvent` event table (same pattern as `BulletControlEvent`: kind, x/y, target_x/y, radius, duration, cast_by) — subscribed raw by every client (consistent with the existing unfiltered event tables; noted as scaling debt, not fixed here), rendered by a new `SpellVisualComponent` (code-created `Polygon2D`s like `SpawnRectVisual`/`SpawnAttractZone`). Caster renders optimistically; echo skips own `cast_by` exactly like `BulletControllerComponent.cs:68`.

### Bullet-control extensions

- **Quick slash** (bullet master): new `BulletControlKind::SlashRect` + `AbilityEffect::Slash(SlashParams { length, width, max_bullet_damage })`. Encodes like DeleteRect with `duration` repurposed to carry the damage threshold; client deletes only bullets whose `SourceStep` def damage ≤ threshold. Server stays bullet-agnostic.
- **Shield wall** (tank): new `BulletControlKind::WallRect` — lingering delete-rect (`duration` as linger, like Attract); `BulletControllerComponent` re-applies the delete query on a tick interval until expiry (the `SpawnAttractZone` pattern).

**Event-binder rule (new, applies to all three event tables):** binders must keep `ReplayExistingRows = false` — late-join non-replay currently holds only by that default plus the server `event` table attribute; there is no last-seen-id tracking. Add a code comment at each binder. One flipped flag would re-fire every wall/slash/visual in history on scene entry.

## `report_hit` attribution change (required by all mark mechanics)

`SequenceStepRef` identifies the step def, not the enemy. Change `report_hit(ctx, attack, enemy_id)`: client stamps each spawned bullet's data with the `enemy_id` from its `BulletPatternEvent` (`BulletData` next to `SourceStep` — BlastBullets custom data is per-spawn-batch, which suffices; per-bullet-within-a-batch is not supported and not needed), and `DamageReceivingComponent.ProcessBulletHit` passes it through. Server validates the enemy exists near the player. Residual trust documented above.

## New `AbilityEffect` variants (`item/tables.rs` — all unit/single-newtype; params struct-wrapped)

| Variant | Used by | Phase |
|---|---|---|
| `MarkBleed(MarkBleedParams{purge, conversion, ally_radius, pool_seconds, outgoing})` | warrior bleed ×4, paladin AoE bleed ×4, warrior dealt+received | 1b/2 |
| `MarkStore(MarkStoreParams{wager, x, z, r1, r2, max_hits, ally_radius})` | warrior store ×3, paladin AoE store ×3 | 1c |
| `Invulnerable(f32 /*ally_radius*/)` | paladin self, tank self, trickster AoE | 1b |
| `DamageShield(f32)` | paladin single-hit shield | 1b |
| `ReduceDamage(ReduceParams{pct, ally_radius})` | tank %-reduction | 1b |
| `RedirectToCaster(f32 /*radius*/)` | paladin redirect | 1b |
| `StoreIncoming(StoreIncomingParams{release: Smite\|Bash, release_radius})` | paladin smite, tank bash-store | 1c |
| `RegenAura(RegenParams{per_second, ally_radius})` | paladin regen | 1b |
| `CastZone(ZoneParams{kind, radius, magnitude})` | witch doctor ×11, wizard spire | 3 |
| `Curse(CurseParams{kind: EnemyStatusKind, magnitude})` | trickster poison/slow/vuln, bullet-master mark | 2 |
| `Bash(BashParams{stagger, armor_break, range, stun_seconds})` | tank bash ×2 | 2 |
| `Slash(SlashParams{length, width, max_bullet_damage})` | bullet-master slash | 2 |
| `Wall(WallParams{length, width})` | tank shield wall | 2 |
| `Teleport(TeleportParams{range, charge_scaled})` | warrior, trickster | 4 |
| `Grapple(f32 /*range*/)` | bullet master | 4 |
| `Spell(SpellParams{…, scaling})` | wizard ×5, bullet-master charge/volley | 5 |

Each variant gets: a `scale_ability_effect` arm, an `activate_ability` arm (the server `match` is exhaustive — `cargo check` enforces this), an `ItemSidebar.FormatAbility` line (the `_ => ""` fallback at `ItemSidebar.cs:299` otherwise renders a malformed `"| 5s CD"` tooltip), and a `TryActivateAbility` optimistic arm **only where optimistic visuals are warranted** — that switch has no exhaustiveness enforcement and no optimistic cooldown; buffs need nothing there.

## Per-ability map (doc → implementation)

**Bullet master** — toggle patterns: **exists** (`WeaponToggle`/`set_slot_toggle`; seed-only new toggle weapons, P6) · charge shot: `Spell` line, charge-scaled, dex-scaling, `SelfSlow` (P5) · volley: `Spell` cone, `hits: N` divided pellets (P5) · dummy: **deferred** · mark: `Curse(Vulnerability)` (P2) · slash: `Slash` (P2) · grapple: `Grapple` (P4).

**Warrior** — bleed ×4 purge conditions: `MarkBleed` self (P1b) · store ×3 wagers: `MarkStore` self (P1c) · windup teleport: `Teleport{charge_scaled}` (P4) · dealt+received bleed: `MarkBleed{outgoing: true}` (P2, needs enemy DoT).

**Wizard** — beam: `Spell` line · converge: `Spell` circle at cursor + converging visual (visual-only deviation) · fireball: `Spell` circle, charge-scaled, suppresses fire while charging · spire: `CastZone(EnemyDot)` (P3) · meteor: `Spell` big circle + shrapnel visual (visual-only deviation) · laser: `Spell` long thin line (all others P5).

**Witch doctor** — all 11 circles: `CastZone` with the 11 `ZoneKind`s (P3). Circles whose effects duplicate paladin buffs route through the same `StatusEffectKind`s; HpFloor/DebuffImmune/DamageShare exist only here.

**Paladin** — regen: `RegenAura` · store ×3: `MarkStore{ally_radius}` (same function as warrior) · bleed ×4: `MarkBleed{ally_radius}` (same function as warrior) · invuln: `Invulnerable(0)` · single-hit shield: `DamageShield` · redirect: `RedirectToCaster` · smite: `StoreIncoming(Smite)` (all P1b/1c).

**Tank** — bash stagger: `Bash{stagger}` · bash stagger+break: `Bash{stagger, armor_break}` (both P2) · invuln: `Invulnerable(0)` (P1b) · %-reduction: `ReduceDamage` (P1b) · store→bash: `StoreIncoming(Bash)` (P1c release, P2 stagger coupling) · shield wall: `Wall` (P2).

**Trickster** — invisibility/decoy: **deferred** · poisons: `Curse(DoT)` · debuffs: `Curse(Slow)` + `Curse(Vulnerability)` (all P2) · teleport: `Teleport{instant}` (P4) · AoE invuln: `Invulnerable(radius)` (P1b).

## Implementation phases

**P0 — combat refactor prerequisite.** As detailed above: `apply_damage_to_enemy` split, `deal_damage_to_player` flags path, reducer-side AOI helpers, teardown + logout lifecycle, tick rename. No user-visible change. Verify: `cargo check`, `server/build.sh`, `dotnet build client/khvg.csproj`, manual smoke of existing combat.

**P1a — `report_hit` +`enemy_id` protocol change.** Server signature/validation, `BulletData` stamp in `BulletSpawnerComponent`, `DamageReceivingComponent` pass-through, bindings regen + publish. Independently testable with existing content (hit attribution visible in logs; combat behavior unchanged).

**P1b — status core + bleed family.** `status/` module (table, `StatusParams`, `apply_status_effect`, `intercept_incoming` with the fixed evaluation order, tick integration, one-active-mark cap with pool-dump/forfeit on replace); `MarkBleed` (self + AoE), `Invulnerable`, `DamageShield`, `ReduceDamage`, `RedirectToCaster`, `RegenAura` variants + seeds; mark cursor-pick in `activate_ability`; client sidebar lines; `active_status_effect` added to `TableSubscriber.GameTables` (raw public subscription — acceptable at co-op scale). Manual: bleed pool drains (and respects HpFloor later in P3), purges fire, redirect/shield/invuln absorb correctly, absorbed hits still count.

**P1c — wagers + store-incoming.** `MarkStore` ×3 (self + AoE), withhold/release via `FlatRelease`, `player_hit_flag` corroboration on NoHits, radius-exit stamping in `report_movement`; `StoreIncoming` (smite release now, bash arm wires fully in P2). Manual: 2×/1×/0× payouts, radius wager can't be tick-dodged, marked-enemy death forfeits.

**P2 — enemy statuses + stagger (~8 items).** `ActiveEnemyStatusEffect`, `Enemy.stagger`, `EnemyTemplate.stun_behavior`, `effective_interval` conversion; consultation hooks in `apply_damage_to_enemy`/`apply_movement`/behavior tick; `Curse`/`Bash`/`Slash`/`Wall` variants + seeds; `SlashRect`/`WallRect` bullet-control kinds + client arms; outgoing-bleed warrior item. Manual: armor break zeros defense for weapon *and* spell damage, stun behaviors differ per enemy, wall eats bullets for its duration.

**P3 — ground zones (~12 items).** `GroundZone` + tick + lifecycle; `CastZone` + witch doctor seeds + wizard spire; live-membership DamageShare; client `ZoneRendererComponent` + `ground_zone` subscription. Manual: effects apply inside/shed outside, bursts fire on expiry, zones vanish on owner death **and** logout, share splits across whoever is actually inside.

**P4 — charge & movement (~6 items).** `AbilityCharge` + timeout sweep, begin/release reducers, `SelfSlow` (incl. `report_movement` clamp enforcement + public client slow API), fire suppression while charging, `InventoryPanel` key-down/up input; `Teleport`/`Grapple` variants + seeds + itemized grapple client work. Manual: charge scaling, timeout auto-release, slow is server-enforced, teleport range clamp + chunk crossing, grapple tween + remote snap.

**P5 — spell damage (~7 items).** `Spell` dispatch + shape queries; `AbilityVisualEvent` + subscription + `SpellVisualComponent`; wizard ×5 + bullet-master charge/volley seeds. Manual: per-class scaling-stat formula, volley pellets vs high/low defense, visuals echo to a second client.

**P6 — content & polish.** `stat_requirements` on all new ability items per class headers; shot-pattern toggle weapon seeds; status-icon UI (subscribe `active_status_effect`) and enemy debuff indicators (new attach point on `Enemy` — none exists today); update `server/AGENTS.md`, `server/spacetimedb/src/item/AGENTS.md`, `client/AGENTS.md`, the ability-authoring workflow doc, and **archive this file**.

## Explicitly out of scope

Archer dummy, trickster decoy, trickster invisibility (enemy retargeting overhaul — future batch); parties/squads; procedural item generation.

## Risks / notes

- Every publish wipes the DB — new tables/fields are hard cuts, seeds re-run. Coordinate publishes between phases.
- `ABILITY_POSITION_MULTIPLIERS` and all server fallbacks (zero-direction, range clamp, cursor pick radius) must mirror into the client's optimistic paths or caster/remote visuals diverge.
- Bleed-pool drains go through the flagged `deal_damage_to_player` path — never direct HP writes (death cleanup and HpFloor live there).
- Redirect loop guard: a redirected hit is applied to the paladin with redirection disabled.
- Enemy DoT kills during the same tick the enemy despawns: collect-then-delete pattern (existing convention) everywhere statuses reference enemies.
- Scheduled reducers have no meaningful `ctx.sender()` — aggro from ticks only via an explicitly looked-up identity.
- The 1 Hz tick now scans consumable rows + status rows + zones + staggered enemies — fine at current scale; revisit if enemy counts grow.
- Event tables are subscribed whole/unfiltered by every game client (existing pattern, `AbilityVisualEvent` joins it) — known scaling debt, not this plan's fix.
