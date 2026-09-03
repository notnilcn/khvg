# enemy/AGENTS.md

Everything an agent needs to work in this module without re-deriving the model. Read
`server/AGENTS.md` first (module-wide map + SpacetimeDB Rust rules — they apply here).

## File map

| File | Role | Edit it when |
|---|---|---|
| `def_tables.rs` | Static template data: `EnemyTemplate` (+ `PhaseDef`/`AttackSequence`/`SequenceStepDef`/`StunBehavior`), the four step-def tables, `PatternType` + param structs, the `SteeringMovement`/`MoveGoal`/`MovePattern` steering model, `EnemyTarget` | Adding a primitive the content needs (a new field/pattern), not for new enemies |
| `instance_tables.rs` | Runtime rows: `Enemy`, `EnemyBehavior`, `EnemyPhase`, `EnemyAttack`, `EnemySequenceStep`, `RepeatStepInstance`, schedules, and the event tables: `BulletPatternEvent` (`damage` resolved at insert), `BulletControlEvent` (rect kinds encode origin (x, y) + far end (target_x, target_y) + width (radius); Attract `duration` = linger, 0 = one-shot; SlashRect `duration` = damage threshold as f32 bits; WallRect `duration` = linger seconds), `AbilityVisualEvent` (P5 spell flashes; per-kind encoding documented on the table) | Rarely — schema changes |
| `methods.rs` | Pure logic: archetype helpers, behavior-tree build/teardown, `tick_phase`/`tick_sequence`, movement, biome spawning | Changing *how* enemies simulate |
| `reducers.rs` | `tick_enemy_spawn`/`tick_enemy_behavior` (scheduled), `report_enemy_hit` (rate-limited + range-checked, see below) | Wiring, not content |
| `emitters.rs` | **Authoring vocabulary**: `Emitter` builder + `Difficulty`/`DifficultySteps`; `compile()` lowers an emitter to an `AttackSequence` | Extending the vocabulary (e.g. a new builder) |
| `defs/` | **Enemy content lives here.** `mod.rs` = raw step helpers + module declarations; one file per enemy/boss (`gorgon_boss.rs` is the reference) | Adding/changing enemies — usually the only place |
| `views.rs` | `nearby_enemies` (AOI), `enemy_templates` (global) | Exposing new data to clients |

## The model in one paragraph

Template = data. A behavior tick (100 ms, `BEHAVIOR_TICK_DT`) walks every in-range enemy,
ticks its current phase's attack sequences (concurrent), and appends one
`BulletPatternEvent` per shot; the client renders events verbatim (angles/offsets are
baked server-side at fire time). HP thresholds advance phases via `compute_phase` in the
combat path. Spawn/despawn always goes through `spawn_enemy_archetype` /
`despawn_enemy_archetype` — the behavior tree (EnemyBehavior → EnemyPhase → EnemyAttack →
EnemySequenceStep → RepeatStepInstance) must not have orphan rows.

## Movement (context steering)

Movement is **context-based steering** (design doc: `enemy/new_enemy_movement_system.md`),
authored per phase as a `SteeringMovement` = **Goal × Pattern × Avoidance** (`MoveGoal` —
Idle/AggroTarget/SpawnPoint/FixedPoint; `MovePattern` — Direct/Flee/Orbit/Meander/Zigzag/
Lunge; plus `separation_weight` and `avoid_cliffs`). Per tick `apply_movement` scores a
fixed set of `STEERING_DIRECTIONS` (12) candidate directions in an *interest map* —
`max(0, dot(candidate, desired))` per pattern, plus shaped separation (per-pair angle
offset, anti-jitter) and cliff probing (`world_to_hex` + `hex_elevation` vs the
`MAX_WALKABLE_*_DELTA` constants; steep down is blocked too — enemies have no fall
mechanic) — and moves along the argmax. Blocked directions fall to the next best instead
of cancelling out, so enemies slide along cliffs and don't clump; it is local steering,
not pathfinding. `EnemyBehavior.velocity_x/y` is the **unit heading memory** (Meander's
persistent noise-swayed heading; other patterns' last chosen direction), server-only as
before. The `Enemy` row additionally carries `spawn_x`/`spawn_y` (Meander's leash anchor)
and `movement_direction`/`movement_speed` — movement as an **angle + scalar** (speed 0
when standing, same convention as `PlayerPosition`); clients reconstruct the velocity for
dead reckoning (`Enemy.cs` → `InterpolationComponent.SetTarget`). All angles in the
movement layer are **radians**; bullet angles stay degrees.

## Semantics that will bite you

- **`repeat_target: 0` = fire forever** (not "skip"). Infinite repeats never finish, so
  their sequence never advances and the phase never restarts from them.
- **A phase restarts only when EVERY sequence finishes.** Windowed (finite) emitters are
  what make a phase loop; `phase_loop_delay` is the silence between loops and also the
  rest after an HP-threshold phase change.
- **`loop_delay` on AttackSequence**: `< 0` = run once per phase cycle (default);
  `>= 0` = self-restart after that delay, excluded from the phase-level restart check.
  This is burst cadence (`Emitter::burst` compiles to it).
- **Catch-up firing**: intervals < 100 ms fire multiple shots per tick, capped at
  `MAX_CATCHUP_SHOTS` (8) — ceiling ~80 shots/s per sequence. `step_timer` advances by
  whole intervals (no drift). `step_timer` doesn't advance while an enemy is out of sim
  range, so before the catch-up loop it's clamped to `now − BACKLOG_CAP × interval` —
  range re-entry fires at most a ~2-shot burst, not a seconds-long wall of bullets.
- **`report_enemy_hit` is self-reported but gated**: a `PlayerHitRate` token bucket (player/
  module) caps sustained rate at the equipped weapon's fire_rate × shot_count ×
  `HIT_RATE_SLACK`, and a wrap-aware `HIT_VALIDATION_RADIUS` range check against the
  attacker's position row rejects nonsense attributions. There is **no `control_bullets`
  reducer** — player bullet-controller casts append `BulletControlEvent`s only via
  `activate_ability` → `apply_ability_effect` with full ability validation.
- **`angle_step` bakes spin per shot** (`base + step × repeat_count`) and rotates the
  origin **offset** by the same accumulated spin (orbiting-arm model); `base_angle_offset`
  itself tilts only the firing direction.
- **Angles are degrees**, except `spread` (and `CurtainParams.angle_span`) which are
  radians in the raw structs, and the whole movement layer (SteeringMovement fields,
  `apply_movement` internals), which is radians throughout. `Emitter::shotgun` takes degrees.
- **`line`/`line_speed_step`** (Ring/Volley/Shotgun): bullet j of a stack flies at
  `speed + step × (j+1)` — a speed ladder, not spatial copies.
- **`angular_speed`/`speed_acceleration`** (every step + `BulletPatternEvent`): mid-flight
  curve/accel, rendered client-side via BlastBullets2D rotation/speed data. If you add an
  event field, the client (`BulletSpawnerComponent.cs`) must learn to render it, and
  bindings regenerate via `server/build.sh` (never hand-edit `module_bindings/`).
- **Difficulty is compile-time.** Variants are separate `EnemyTemplate` rows seeded per
  `Difficulty` tier (`Foo_Easy` … `Foo_Madness`); spawn by name. No runtime difficulty flag.
- **Enemy statuses + stagger (status/ module, P2).** `Enemy.stagger` (0–100) decays on the
  1 Hz status tick; a full bar applies a Stunned debuff whose effect depends on the
  template's `stun_behavior`: Pause freezes the enemy wholesale in `tick_enemy_behavior`,
  Slowed/Enraged scale movement in `apply_movement`, and Enraged additionally divides every
  fire-pacing interval via `effective_interval` — convert ANY new wall-clock timing in
  `tick_phase`/`tick_sequence` through it. All current seeds are Pause.

## How to add an enemy (the recipe)

1. New file `defs/my_enemy.rs`: one `fn` per emitter returning a base `Emitter`; phases via
   `make_phase(ctx, index, hp_threshold, movement, loop_delay, vec![emitter.compile(ctx, difficulty), …])`
   where `movement` is a `SteeringMovement` — `SteeringMovement::idle()` to stand still or
   `steering(speed, goal, pattern)` (separation + cliff avoidance on; `.with_cycle(a, p)`
   for a move/pause cycle);
   `pub fn seed(ctx)` building the template(s) (`EnemyTemplate::seed` upserts by `template_id`).
2. `pub mod my_enemy;` in `defs/mod.rs`; seed call in `main/lifecycle.rs::init`.
3. `cargo check` → `server/build.sh` → `dotnet build client/khvg.csproj`.
4. Spawn with admin `spawn_enemy <template_id> x y false false`; inspect with `spacetime sql`.
Full tutorial: `docs/Technical Design Docs/Workflows/Adding a Boss (Enemy Authoring Workflow).md`.

## Anti-patterns — don't

- **Don't hand-unroll steps** (40 `seq_single` calls with incrementing angles). If the model
  can't express a pattern, add a primitive to `def_tables.rs` + `methods.rs` the way
  `angle_step`/`loop_delay` were added — or compose with `window()`/loops in content.
- Don't insert `Enemy`/behavior rows directly; use the archetype helpers.
- Don't skip `defs/` and seed from `main/seeds.rs` — that file is for world/item/shared
  seeds and the `TestBoss` movement/pattern showcase (kept as a second example).
- Don't give a phase a mix of infinite emitters and an expected loop — see semantics above.
- Don't invent SpacetimeDB APIs; `server/AGENTS.md`'s rules apply (`ctx.sender()` with
  parens, `ctx.db.table()` method calls, `..row` spreads on update, no panics on expected errors).

## Verification

`cargo check --manifest-path server/spacetimedb/Cargo.toml` is the only automated gate on
this side; publish flow is `server/build.sh` (wipes DB, reseeds — schema changes are hard
cuts). Row-level sanity: `spacetime sql bullethell "SELECT … FROM enemy_template /
repeat_step_def / movement_def"`. Anything past that (patterns looking right, phase pacing,
multiplayer) is verified by the user in-game — describe spawn steps per variant and stop.
