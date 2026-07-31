# 08 Add an Enemy Bullet Pattern (new `PatternType` variant)

Enemy attacks are built from five pattern types (`Ring`, `Volley`, `Curtain`, `Shotgun`, `Explosion`) — reused freely in any step def ([[07 Add an Enemy|07]]). A *sixth* shape — a spiral, an aimed beam, a bouncing wall — is a code change in two places: the server enum (schema + payload) and the client dispatcher that expands the event into actual bullets. The server **never simulates bullets**; it just forwards the pattern inside a fire-once `BulletPatternEvent` row, so no server logic branches on your variant. Background: [[06 Enemy AI & Bullet Patterns|06]].

## 1. Add the params struct and variant (server)

In `enemy/def_tables.rs` ([[enemy/def_tables.rs##pub enum PatternType|PatternType]]):

```rust
#[derive(SpacetimeType, Clone)] pub struct SpiralParams { pub speed: f32, pub count: u32, pub arms: u32, pub twist: f32 }

pub enum PatternType {
    Ring(RingParams),
    Volley(VolleyParams),
    Curtain(CurtainParams),
    Shotgun(ShotgunParams),
    Explosion(ExplosionParams),
    Spiral(SpiralParams),   // new
}
```

Keep the params *shape-descriptive* (count/speed/geometry), not *stateful* — the same params row is read by every client expanding the event. The server compiles with no further changes; the variant flows through step defs → `BulletPatternEvent` untouched.

## 2. Regenerate bindings and publish

```bash
cd server && bash build.sh
```

This regenerates `PatternType.g.cs` (a C# tagged union with a `PatternType.Spiral(var p)` case) and reseeds.

## 3. Expand the pattern (client)

In `BulletSpawnerComponent.cs`, add a branch to the dispatcher ([[BulletSpawnerComponent.cs##public void SpawnEnemyBullet|SpawnEnemyBullet]]):

```csharp
else if (bulletPattern.PatternType is PatternType.Spiral(var spiral))
    SpawnSpiral(origin, spiral, baseAngle, bulletPattern.EventId, bulletPattern.Lifetime);
```

Then write `SpawnSpiral` following the existing spawn methods ([[BulletSpawnerComponent.cs##private void SpawnRing|SpawnRing]] is the closest template). The contract:

- `origin` and `baseAngle` arrive pre-resolved (origin includes the step's x/y offset; `baseAngle` includes target aim + `base_angle_offset`). Your method only arranges bullets around them.
- Set speed via `SetSpeed(speed)` for uniform-speed batches, then set `transforms` (one `Transform2D` per bullet: firing angle + origin) and call `BlastBullets.Call("spawn_directional_bullets", EnemyBulletSpawner)` once — like `SpawnRing`/`SpawnCurtain`.
- For per-bullet speed/lifetime variance, loop `SpawnSingle(origin, angle, speed, lifetime)` like `SpawnVolley`/`SpawnShotgun` — and derive the variance from `Jitter(eventId, i, stream, magnitude)`, **not** `GD.Rand*`: the SplitMix64 hash seeded by `event_id` is what keeps every client's pellets identical ([[BulletSpawnerComponent.cs##private static float Jitter|Jitter]]).
- `max_life_time`, texture, and `bullets_custom_data` (the hit-routing `BulletData`) are already set by `SpawnEnemyBullet` before your branch runs. Don't reset `bullets_custom_data` — without it, hits can't be routed back to `report_hit`.

Damage needs no client work: a bullet landing on a player reports the step reference, and the server re-resolves damage from the def row ([[07 Combat & Damage Math|07]]).

## 4. Use it

Reference `PatternType::Spiral(SpiralParams {...})` from any step def — seed (`seq_single`/`seq_repeat`/`multi_shot`, [[07 Add an Enemy|07]]) or runtime upsert ([[00 Index & The Build Loop|00]]). One variant, unlimited step defs with different params.

## 5. Verify

- `dotnet build client/khvg.csproj` — catches binding drift.
- Admin-spawn a template using the new pattern (`spacetime call --server local bullethell spawn_enemy ...`) near your player, and check: bullets render in the intended shape; getting hit actually reduces HP (the `BulletData` routing); a second connected client sees the *same* trajectories (the deterministic jitter).

## Gotchas

- **Determinism is a hard requirement**, not a nicety — bullet positions are computed independently by every client from the same event row. Any nondeterministic source (`GD.Rand*`, wall-clock, frame timing) desyncs where a bullet "is" for each player, and hits are reported by *each* client for their own player. Use the `Jitter` helpers.
- **Angles are radians** in `PatternType` params (converted client-side), but `base_angle_offset` in step defs is degrees — see [[07 Add an Enemy|07]].
- **Pattern expansion is instantaneous** — one event = one batch of transforms. A pattern that evolves over time (e.g. a rotating spiral stream) is expressed as a `seq_repeat` step firing the pattern repeatedly, not as one event that keeps emitting.
- Don't touch the generated `module_bindings/Types/PatternType.g.cs`.
- Texture/orientation rules for the bullet sprite are in [[01 Add a Texture or Sprite|01]] (`default` animation, first frame, arrow-angle offset).
