# Shelved: bullet-affecting player abilities

## Context

Brainstormed class ability ideas where the player affects *enemy* bullets, rather than just
dodging them: delete, reflect, split, absorb, slow, curve, reverse, freeze, teleport, attract
toward a spawned blackhole, repel, absorb-and-redirect toward enemies.

Shelved for now — most of them don't fit the current bullet system without a prerequisite
refactor (below). Recorded here so the feasibility analysis doesn't have to be redone from
scratch next time this comes up.

## Current architecture constraint

Enemy bullets are spawned fire-and-forget via `BulletFactory2D.spawn_directional_bullets`
(`client/Scripts/Game/BulletManager.cs`) — no handle is kept after spawn. The
**BlastBullets2D** addon (`client/addons/blastbullets2d`) only exposes runtime manipulation
(disable, homing, curves, teleport, per-bullet timers, orbiting...) through
`spawn_controllable_directional_bullets`, which returns a `DirectionalBullets2D` instance that
must be held and indexed into.

**Every ability on the list shares one prerequisite:** switch enemy bullets to the controllable
spawn variant and keep a lookup (by position/radius, or by `(eventId, pelletIndex)` since the
`SplitMix64` jitter in `BulletManager.cs` is already deterministic per event) so an ability can
find "which live bullets are near this point." This is a one-time refactor, not per-ability
work — build it once and every ability below gets cheaper.

Also relevant: enemy bullets are client-simulated and self-reported (`BulletManager.OnEnemyBulletAreaEntered`
→ `report_hit` reducer in `player/reducers.rs`). There's no per-bullet ID on the wire, only
`SourceStep`. That means purely defensive abilities (delete/slow/freeze/redirect-away) can be
**entirely client-local** — no new reducer needed, consistent with the existing trust model.
Only "this bullet now damages an enemy" abilities need a server round-trip.

## Feasibility ranking

**Cheapest, native to the plugin (once controllable bullets exist):**
- **Delete** — `disable_bullet()`.
- **Teleport** — built-in ("Teleport And Offset Support").
- **Attract toward a spawned blackhole** — this *is* the homing system verbatim (homing
  supports Node2D targets, GlobalPosition targets, even the mouse). Arguably as cheap as
  delete once controllable bullets are wired up.
- **Curve** — native via `BulletCurvesData2D` or Path2D movement patterns.

**Moderate — needs custom logic but the plugin supports the primitives:**
- **Slow** — reassign per-bullet curves/speed data at runtime (`bullet_set_curves_data`).
- **Split** — disable original + spawn N new; reuses the existing Volley/Shotgun pattern-spawn
  shape already in `BulletManager.cs`.
- **Reverse** — same mechanism as split/curve, just flip the angle.

**Higher value, still tractable:**
- **Reflect / Absorb-and-redirect toward enemies** — needs recoloring the bullet (collision
  layer/mask + custom data) from enemy-owned to player-owned. Payoff: when the reflected
  bullet hits an enemy `Area2D`, the existing `report_enemy_hit` reducer
  (`enemy/reducers.rs`) can be called unmodified — it computes damage from the player's own
  stats, not the bullet's, so there's no new server logic required. More valuable than it
  first looks.

**Weakest fits — fighting the library instead of using it:**
- **Freeze** — no built-in pause for a single bullet. Would need a hand-rolled zero-speed
  curve plus a custom resume timer (the plugin's built-in timer helper operates per-multimesh,
  not per-bullet-index).
- **Repel** — homing only pulls toward a target; there's no "push away" analog. Would require
  manually recomputing an outbound angle each activation — bespoke code, not a plugin feature.

## Bottom line

Delete is a safe first pick, but teleport and blackhole-attract are equally cheap once the
controllable-bullets refactor lands, and arguably more interesting for a dodge-focused game.
Freeze and repel are the two ideas that don't ride any existing plugin feature — deprioritize
unless a class concept really needs them.
