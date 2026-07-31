# 09 Add a Movement Behavior (new `MovementBehavior` variant)

Phases pick their movement from the `MovementBehavior` enum — `Stationary`, `Wander`, `Chase`, `Flee` today ([[enemy/def_tables.rs##pub enum MovementBehavior|MovementBehavior]]). A fifth — orbiting the target, patrolling waypoints, leaping — is a server-only code change: the variant, plus one match arm. **No client work** — enemies move server-side in the 100ms `tick_enemy_behavior` and clients just interpolate the resulting `Enemy` rows. Background: [[06 Enemy AI & Bullet Patterns|06]].

## 1. Add the variant (server)

In `enemy/def_tables.rs`, with a params struct if it has knobs (follow `FleeParams`):

```rust
#[derive(SpacetimeType, Clone)] pub struct OrbitParams {
    pub speed: f32,
    pub radius: f32,
    pub active_duration: f32,
    pub pause_duration: f32,
}

pub enum MovementBehavior {
    Stationary,
    Wander(WanderParams),
    Chase(ChaseParams),
    Flee(FleeParams),
    Orbit(OrbitParams),   // new
}
```

Reusing the `active_duration`/`pause_duration` duty-cycle pair is encouraged — the shared `cycle_phase` helper ([[enemy/methods.rs##fn cycle_phase|cycle_phase]]) already implements it and every existing behavior uses it.

## 2. Implement the behavior (server)

Add an arm to the match in `apply_movement` ([[enemy/methods.rs##pub fn apply_movement|apply_movement]]). The contract, read off the existing arms:

- Signature context: you get `enemy` (current x/y), `behavior` (aggro target, stored velocity), and `phase` (which carries `move_cycle_started_at` for the duty cycle). Return `(new_x, new_y, velocity_x, velocity_y)`; return the `idle` tuple to stand still.
- Advance by `p.speed * BEHAVIOR_TICK_DT` — the fixed tick is 100ms, never measure real time.
- Find the aggro target's position via `behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id))` — and handle `None` (no target) by idling, like `Chase`/`Flee`.
- **Determinism:** any "randomness" must come from `splitmix64`/`hash_to_unit` seeded by stored ids (see `Wander`'s direction hash, [[enemy/methods.rs##MovementBehavior::Wander|wander arm]]) — never from a real RNG or clock. Reducers are deterministic transactions.

```rust
MovementBehavior::Orbit(p) => {
    let (active, _) = cycle_phase(ctx, phase.move_cycle_started_at, p.active_duration, p.pause_duration);
    if !active { return idle; }
    match behavior.aggro_target.and_then(|id| find_player_pos_by_id(ctx, id)) {
        Some((px, py)) => {
            let dx = enemy.x - px;
            let dy = enemy.y - py;
            let angle = dy.atan2(dx) + p.speed * dt / p.radius.max(1.0);  // advance along the circle
            let nx = angle.cos();
            let ny = angle.sin();
            (px + nx * p.radius, py + ny * p.radius, nx, ny)
        }
        None => idle,
    }
}
```

You don't handle torus wrap or chunk reindexing — the tick applies those to your returned position ([[enemy/reducers.rs##pub fn tick_enemy_behavior|tick_enemy_behavior]]).

## 3. Build, then use it

```bash
cd server && bash build.sh
```

Reference the variant from any phase's movement — seed via `make_phase(ctx, ..., MovementBehavior::Orbit(...), ...)` ([[07 Add an Enemy|07]]) or a runtime `upsert_movement_def` ([[00 Index & The Build Loop|00]]). `cargo check` will refuse to compile until the match arm exists — the enum is exhaustively matched, which is your guarantee that step 2 is the only server touch point.

## 4. Verify

- `dotnet build client/khvg.csproj` (bindings changed).
- Admin-spawn an enemy whose phase uses the new behavior next to your player; aggro it (walk close — entering sim range triggers `recompute_aggro`). Watch the movement pattern; kill it and confirm XP/death still work (they're movement-agnostic).
- Edge cases to eyeball: behavior with no aggro target (should idle), and crossing a map seam (wrap is applied after your arm returns — orbit/chase across a seam boundary may briefly steer oddly; that's inherent to per-tick target steering, shared with `Chase`).

## Gotchas

- **Movement only runs inside `move_sim_factor` range** of a connected player — off-screen enemies freeze mid-pattern ([[06 Enemy AI & Bullet Patterns|06]]). Design behaviors that tolerate being paused and resumed, not ones needing continuous integration.
- **Stored velocity is cosmetic-ish:** `velocity_x/y` you return is persisted on `EnemyBehavior` but mainly serves client extrapolation of remote entities; don't rely on it as state for your behavior. For real per-enemy state you'd need a new column on `EnemyBehavior`/`EnemyPhase` (schema change — heavier, still fine, just note `build_enemy_behavior` must initialize it).
- **The duty cycle is per phase, not per behavior** — `move_cycle_started_at` lives on `EnemyPhase`; switching phases resets the cycle.
- `EnemyTarget` (Idle/AggroTarget) is a *separate* enum for aim, not movement — adding a movement variant doesn't touch `resolve_step_target`.
