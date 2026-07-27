# Plan: Relayable enemy-bullet despawns + slash ability

Goal: let players destroy individual enemy bullets with a melee slash (slash damage must exceed
bullet damage), and make **all** bullet despawns — hit or slash — relay to every client.

## Current state (what exists today)

- Server: `tick_enemy_behavior` (`enemy/reducers.rs`) inserts `BulletPatternEvent` rows
  (`enemy/instance_tables.rs`, an **event table**). Clients spawn bullets locally from these rows;
  the server never tracks live bullet positions.
- Client: `Enemy.cs` forwards matching `BulletPatternEvent` inserts to
  `BulletManager.SpawnEnemyBullet` (`client/Scripts/Game/BulletManager.cs`), which calls the
  BlastBullets2D plugin's `spawn_directional_bullets` (fire-and-forget, returns nothing).
  Patterns are deterministic (SplitMix64 seeded by `event_id`), so all clients render identical
  trajectories.
- Enemy bullets collide only with the local player's `BulletCollider` (bullet layer/mask = 2).
  On overlap the plugin auto-disables the bullet (`bullet_max_collision_count` defaults to 1) and
  `BulletManager.OnEnemyBulletAreaEntered` calls `report_hit(source_step)`, which applies damage
  (`player/reducers.rs`). The despawn is **local only** — other clients keep showing the bullet.

## Target protocol

1. **Spawn** (unchanged concept): server inserts `BulletPatternEvent`; clients spawn locally.
   New: every spawned bullet is registered under a unique key so it can be found and killed later.
2. **Despawn request**: when the local player is hit by a bullet or slashes one, the client sends
   the bullet's identity to the server (`report_hit` extended, or new `slash_bullet` reducer).
3. **Relay**: the reducer validates, then inserts a row into a new `BulletDespawnEvent` event
   table. All subscribed clients (including the sender) receive the insert and disable that bullet
   locally. Despawn application must be idempotent (unknown/already-dead key = no-op).

### Bullet identity: `(pattern_event_id, bullet_index)`

- `pattern_event_id` = `BulletPatternEvent.event_id` of the spawn event (unique per pattern fire,
  including repeat-step re-fires).
- `bullet_index` = pellet ordinal `0..count-1` within that event.
- **Simplification: spawn every pattern per-pellet.** Ring and Curtain currently batch all
  pellets into one multimesh; switch them to the same per-pellet `SpawnSingle` loop that
  Volley/Shotgun/Explosion already use. Then every bullet is its own multimesh instance with
  instance-local index 0, one identity scheme covers all patterns, and `BulletData` (the
  per-instance `bullets_custom_data`) can carry the full identity. Trade-off: more multimesh
  instances (a 30-bullet ring = 30 instances); acceptable since the plugin pools instances and 3 of
  5 patterns already do this. If profiling ever shows instance count is a problem, Ring/Curtain can
  go back to batched instances with the instance-local index as `bullet_index` — deterministic on
  all clients, including the Curtain gap skip.

Event-table rows are ephemeral (not persisted), so a later reducer **cannot look up the original
spawn row**. Anything the server needs for validation must either travel with the reducer call or
be resolvable from persistent def tables via `source_step`. We do the latter for damage.

## Server changes

### 1. `enemy/instance_tables.rs`

Add `damage` to `BulletPatternEvent` (denormalized so clients can do the slash damage comparison
without subscribing to step-def tables):

```rust
pub struct BulletPatternEvent {
    // ... existing fields ...
    pub lifetime: f32,
    pub damage: u32,          // NEW
    #[index(btree)] pub chunk_index: i64,
}
```

Add the despawn relay table (+ reason enum for client FX):

```rust
#[derive(SpacetimeType, Clone)]
pub enum DespawnReason { Hit, Slash }

#[table(accessor = bullet_despawn_event, public, event)]
pub struct BulletDespawnEvent {
    #[primary_key] #[auto_inc] pub event_id: u64,
    pub pattern_event_id: u64,
    pub bullet_index: u32,
    #[index(btree)] pub chunk_index: i64, // future AoI scoping; ignored for now
    pub despawned_by: Identity,
    pub reason: DespawnReason,
}
```

### 2. `enemy/methods.rs`

- Extend the `BulletStepEvent` tuple with `damage: u32` and populate it at the three construction
  sites in `tick_sequence` (`s.damage` for Single, `r.damage` for Repeat, per-shot `s.damage`
  for Multi).
- Add a shared damage resolver (extracted from `report_hit`'s match, def tables persist so this
  works in any later reducer):

```rust
pub fn resolve_step_damage(ctx: &ReducerContext, step: &SequenceStepRef) -> Option<u32> {
    match step {
        SequenceStepRef::Single(def_id) =>
            ctx.db.single_step_def().def_id().find(def_id).map(|s| s.damage),
        SequenceStepRef::Repeat(repeat_id) =>
            ctx.db.repeat_step_instance().repeat_id().find(repeat_id).map(|r| r.damage),
        SequenceStepRef::Multi(m) =>
            ctx.db.multi_step_def().def_id().find(&m.def_id)
                .and_then(|d| d.shots.get(m.shot_index as usize).map(|s| s.damage)),
    }
}
```

### 3. `enemy/reducers.rs`

- `tick_enemy_behavior`: destructure the new tuple field and pass `damage` into the
  `BulletPatternEvent` insert.
- New reducer:

```rust
#[spacetimedb::reducer]
pub fn slash_bullet(ctx: &ReducerContext, pattern_event_id: u64, bullet_index: u32,
                    chunk_index: i64, attack: SequenceStepRef) -> Result<(), String> {
    let player = require_in_world(ctx)?;
    let bullet_damage = resolve_step_damage(ctx, &attack).ok_or("Attack step not found.")?;
    let slash_damage = compute_player_damage(ctx, player.profile_id);
    if slash_damage > bullet_damage {
        ctx.db.bullet_despawn_event().insert(BulletDespawnEvent {
            event_id: 0, pattern_event_id, bullet_index, chunk_index,
            despawned_by: ctx.sender(), reason: DespawnReason::Slash,
        });
    }
    Ok(()) // failed check is a silent no-op, not an error
}
```

The server is authoritative on the `>` comparison (`compute_player_damage` from
`player/methods.rs` = weapon damage × (1 + strength × 0.002)); any client-side check is only an
optimization to avoid reducer spam.

### 4. `player/reducers.rs` — extend `report_hit`

New signature: `report_hit(ctx, attack: SequenceStepRef, pattern_event_id: u64,
bullet_index: u32, chunk_index: i64)`. Replace the inline damage match with
`resolve_step_damage`, keep the existing HP/death logic unchanged, and insert the relay row in the
same transaction (atomic with the damage):

```rust
ctx.db.bullet_despawn_event().insert(BulletDespawnEvent {
    event_id: 0, pattern_event_id, bullet_index, chunk_index,
    despawned_by: ctx.sender(), reason: DespawnReason::Hit,
});
```

Note: this makes a bullet that hits **any** player disappear for **everyone** (bullets become
shared-world objects). Remote players never collide with enemy bullets locally, so this changes
only visuals, not damage — but it is a deliberate behavior change.

### 5. Publish + regenerate bindings

```bash
spacetime publish <db-name> --module-path server/spacetimedb          # additive; no --clear-database needed
spacetime generate --lang csharp --out-dir client/Scripts/module_bindings --module-path server/spacetimedb
```

Do not hand-edit `client/Scripts/module_bindings`.

## Client changes

### 6. `client/Scripts/Game/BulletData.cs`

```csharp
public partial class BulletData : Resource
{
    public SequenceStepRef SourceStep = null!;
    public ulong EventId;       // BulletPatternEvent.EventId
    public uint PelletIndex;    // bullet_index in the despawn protocol
    public uint Damage;         // from the event; used by the slash check
}
```

### 7. `client/Scripts/Game/BulletManager.cs`

- **Controllable spawns (enemy only).** Replace `spawn_directional_bullets` with
  `spawn_controllable_directional_bullets` for `EnemyBulletSpawner` and keep the returned
  instance. Player bullets are untouched (no despawn relay needed).
  ```csharp
  var inst = blastBullets.Call("spawn_controllable_directional_bullets", EnemyBulletSpawner).AsGodotObject();
  inst.Set("bullet_max_collision_count", 0); // never auto-disable; we decide when a bullet dies
  ```
  Setting max collisions to 0 is required: the default (1) would auto-disable a bullet that
  overlaps the slash hitbox even when the slash is too weak to destroy it.
- **Per-pellet spawning.** Rewrite `SpawnRing`/`SpawnCurtain` as loops over `SpawnSingle` (like
  `SpawnVolley`/`SpawnShotgun`/`SpawnExplosion` already do; Curtain keeps skipping `GapIndex` so
  ordinals stay aligned across clients). `SpawnSingle` sets a fresh `BulletData`
  (`EventId`/`PelletIndex`/`Damage`/`SourceStep`) on the spawner before each spawn call.
- **Registry.**
  ```csharp
  private readonly Dictionary<(ulong EventId, uint PelletIndex), (GodotObject Inst, BulletData Data)> activeEnemyBullets = [];
  ```
  Populate after every controllable spawn. Because the plugin auto-pools a multimesh once all its
  bullets die and may **reuse** it for a later spawn, guard every disable with a reference check:
  ```csharp
  bool Matches(GodotObject inst, BulletData data) =>
      GodotObject.IsInstanceValid(inst) &&
      ReferenceEquals(inst.Get("bullets_custom_data").AsGodotObject(), data);
  ```
  Lazily sweep stale entries on each `SpawnEnemyBullet` (cheap; spawn rate is modest).
- **`DespawnTracked(data)`** helper: remove the registry entry and, if `Matches`, call
  `inst.Call("disable_bullet", 0)`.
- **`OnEnemyBulletAreaEntered`** now branches on what was overlapped:
  - Slash hitbox (see 8; identify via group, e.g. `((Node)hitTargetArea).IsInGroup("SlashHitbox")`)
    → forward to the slash script (or invoke a callback it registered); it does the damage check.
  - Otherwise (player `BulletCollider`) → `DespawnTracked(data)` immediately (this replaces the
    old plugin auto-disable), then
    `Conn.Reducers.ReportHit(data.SourceStep, data.EventId, data.PelletIndex, chunkIndex)`.
- **Despawn relay handler.** Subscribe in `_Ready` (conn null-check, same pattern as `Enemy.cs`):
  ```csharp
  conn.Db.BulletDespawnEvent.OnInsert += (_, e) => {
      if (activeEnemyBullets.TryGetValue((e.PatternEventId, e.BulletIndex), out var entry))
          DespawnTracked(entry.Data);
  };
  ```
  Idempotent by construction: duplicate despawns and despawns for never-spawned bullets (late
  joiner) are registry misses. The sender's own echo is likewise a no-op. Unsubscribe in
  `_ExitTree`.

### 8. Slash ability — new `client/Scripts/Players/Local/LocalPlayerSlash.cs`

Node on the local player next to `LocalPlayerCombat`.

- **Input:** add a `Slash` action to `project.godot` (suggest right mouse button). On press with a
  weapon equipped (and a short cooldown, ~0.5s), enable the hitbox for ~0.15s and play the slash
  visual.
- **Hitbox:** plain `Area2D` created in `_Ready` (same inline style as
  `LocalPlayerCombat.SpawnZone`): `CollisionLayer = 2` (so enemy bullets, mask 2, detect it),
  `CollisionMask = 0`, `Monitoring = false`, `Monitorable` toggled with `SetDeferred`, small
  rectangle/circle shape placed in front of the player toward the aim direction. Add it to group
  `SlashHitbox`.
- **On bullet overlap** (routed from `BulletManager.OnEnemyBulletAreaEntered`): optional
  client-side pre-check replicating the server formula — `_weapon.Damage * (1f + strength *
  0.002f)` using `LocalPlayerStats` — purely to skip doomed reducer calls; then if it passes:
  ```csharp
  BulletManager.Instance.DespawnTracked(data);                 // optimistic local kill
  Conn.Reducers.SlashBullet(data.EventId, data.PelletIndex, chunkIndex, data.SourceStep);
  ```
  If the check fails, do nothing — the bullet correctly survives (`bullet_max_collision_count = 0`
  keeps it alive). If the server disagrees with the optimistic kill (stale stats), there is no
  correction message; the window is one RTT and self-heals on the next spawn. Acceptable.
- `chunkIndex` comes from the `BulletPatternEvent` row; simplest is to stash it in `BulletData`
  too when spawning.

### 9. `client/Scripts/Game/GameManager.cs`

Add `.AddQuery(q => q.From.BulletDespawnEvent())` to `SubscribeGame()` (whole-table subscription,
same as `BulletPatternEvent` — AoI filtering stays out of scope, `chunk_index` is there when it
becomes needed).

## Edge cases & guarantees

- **Ordering:** spawn and despawn arrive in server commit order, so a client never applies a
  despawn before the matching spawn. A client that subscribes between the two just misses a
  registry key → no-op.
- **Duplicates:** two players slashing the same bullet in the same tick produce two relay rows;
  the second is a no-op everywhere. No server-side dedup possible (event rows are ephemeral) and
  none needed.
- **Determinism preserved:** pellet ordinals derive from `event_id`-seeded loops that are
  identical on every client, so `(event_id, bullet_index)` names the same visual bullet everywhere.
- **Pooled instance reuse:** the `bullets_custom_data` reference check prevents a late despawn
  from killing an unrelated bullet in a reused multimesh.

## Verification

1. `spacetime publish` succeeds (module compiles); regenerate bindings; Godot C# build clean.
2. Two clients on the same enemy: patterns render identically (determinism sanity check).
3. Client A walks into a bullet → bullet vanishes on A **and** B; A loses HP (existing
   `report_hit` path still works).
4. Slash with weapon damage > bullet damage → bullet destroyed on both clients; with a weaker
   weapon → bullet survives on both.
5. Let bullets expire naturally → no console errors; registry sweep removes stale entries (check
   via a slash right after a heavy ring pattern: no wrong bullet dies).
6. Rapid double-slash on one bullet → single disappearance, no errors.

## Docs to update after implementation

- `server/CLAUDE.md` module map: `BulletDespawnEvent` under `enemy/instance_tables.rs`,
  `slash_bullet` under `enemy/reducers.rs`, new `report_hit` signature under `player/reducers.rs`,
  `resolve_step_damage` under `enemy/methods.rs`.
- `client/CLAUDE.md`: `BulletManager.cs` entry (controllable spawns + despawn relay), new
  `LocalPlayerSlash.cs` entry.
