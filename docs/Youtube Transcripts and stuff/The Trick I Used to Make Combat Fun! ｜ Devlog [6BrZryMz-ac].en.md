# New Enemy Movement System

## Core idea: context-based steering (direction weights)

The system is an evolution of classic boids/steering behaviors, fixing their biggest flaw:
**opposing vectors cancel each other out.** E.g. an enemy wants to flee the player but a wall
is directly behind it — flee vector + wall-avoidance vector sum to ~zero, and the enemy just
stands there frozen ("dumbfounded by the existence of this wall").

Instead of summing vectors into a single resultant, the AI keeps **a record of how desirable
each of several discrete directions is** (a weight per direction), and picks the best direction
that is actually available. When the most-desired direction is obstructed, it falls back to
the *next best* option (e.g. stepping slightly to the side and continuing along that path).

### Evaluating directions

- The AI considers a fixed set of directions around itself (visualized in the video as a
  radial gizmo: one line per candidate direction — green = desirable, longer = more
  desirable; red = no desire to move that way).
- Desirability of a candidate direction toward a goal is computed with a **dot product**
  between the (normalized) candidate direction and the (normalized) desired movement vector.
  Dot product of two normalized vectors = cos(angle): `1` = dead-on, `0` = perpendicular,
  `-1` = opposite. Normalized to the 0–1 range, this is the direction's weight.
- To move toward a target: pick the highest-weight **unobstructed** direction and move.

### Shaping and combining weights ("weighting the weights")

The real power comes from shaping the raw dot-product weights and combining multiple weight
sets to produce richer behaviors:

- **Combat approach + strafe:** One weight set drives the enemy toward its target, but the
  weights are *scaled down as the enemy gets closer*, so other behaviors take over at close
  range. Once within strafing range, a **shaping function is applied to the dot product that
  favors sideways movement** over forward/backward — the enemy starts circling (strafing
  around) its target while keeping distance. This produces engaging "move in and out of
  combat, strafe, keep safe distance" behavior instead of a beeline.
  - Gotcha from the video: pure circling made enemies form perfect concentric circles and
    bounce off each other ("Newton's cradle of skellies"). Fix: **offset the following
    distance slightly per-enemy** so orbits don't coincide.

- **Separation without jitter:** Naively moving directly away from nearby entities caused a
  tug-of-war — neighbors push each other back and forth, producing jittery rapid direction
  flips. Fix: **apply a shaping function to the separation logic too**, so enemies prefer to
  move away from neighbors *at a slight angle* rather than directly away. This removes the
  oscillation.

- **Obstacle avoidance is emergent:** because blocked directions get low/zero weight, the AI
  naturally picks the next-best open direction and slides along walls instead of freezing
  (but see the addendum — this is not pathfinding).

## Wandering behavior

Separate from combat movement, idle wandering was reworked for natural feel:

- **Old (bad):** pick a random point near spawn, move to it, repick when reached or
  obstructed. Result: erratic, sporadic direction changes that feel unnatural.
- **New:** enemies **meander** — move in a direction whose heading **gradually sways left and
  right over time**, driven by **OpenSimplex noise** applied to the direction.
  - Why noise: the randomization is **smooth** (no abrupt turns), and noise values are
    **weighted toward 0**, so the enemy mostly travels straight for stretches, occasionally
    curves, then pulls back to straight — a natural wandering gait.
- **Leash to spawn:** to keep wanderers near their spawn point, the wander direction is
  **weighted back toward spawn whenever they stray too far**, causing a gradual turn-around.
  This is *not* a hard boundary: the pull starts at a short radius (~2 tiles in the video)
  and its strength scales with distance from spawn — weak at first, stronger the farther out.
  Because it's smoothed over distance, there's no visible leash point and it looks natural.

## Practical notes

- The creator built a **debug gizmo** that draws the per-direction weights (green/red lines)
  — invaluable for tuning; plan an equivalent visualization when implementing.
- Main tuning pain points were jitter (fixed by shaping separation at an angle) and orbit
  clumping (fixed by offsetting follow distances).
- Movement integrates with the combat state machine; the movement system supplies "how to
  move," state machine supplies "what the goal is" (approach, strafe, flee, wander).
- Bonus outcome: combining this movement with a faction/targeting system (enemies able to
  target each other) produced emergent battle-royale behavior with no extra tuning.

## Godot 4.7 / C# mapping

- **Candidate directions:** a fixed array of N unit vectors evenly spaced around the enemy
  (the video never states N — see open questions). Generate once:
  ```csharp
  var dirs = new Vector2[DirectionCount];
  for (int i = 0; i < DirectionCount; i++)
      dirs[i] = Vector2.Right.Rotated(Mathf.Tau * i / DirectionCount);
  ```
- **Weight per direction toward a goal:**
  ```csharp
  float dot = dirs[i].Dot((goalPos - enemyPos).Normalized()); // -1..1
  float weight = (dot + 1f) * 0.5f;                           // 0..1
  ```
  Then apply the behavior's shaping function to `weight` (approach falloff by distance,
  sideways-favoring curve for strafe, angled preference for separation) and combine the
  weight sets before picking the best unobstructed index.
- **Obstruction test per direction:** raycast with
  `GetWorld2D().DirectSpaceState.IntersectRay(...)` (C#:
  `PhysicsRayQueryParameters2D.Create(origin, origin + dirs[i] * probeLength, mask)`).
  Blocked directions get weight ~0 so the next-best open direction wins.
- **Wander noise:** "OpenSimplex" in the video = `FastNoiseLite` in Godot 4.x:
  ```csharp
  var noise = new FastNoiseLite {
      NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
      Frequency = 0.05f, // tune — low frequency = slow sway
  };
  // in _PhysicsProcess: sway heading over time, noise output is ~-1..1 weighted toward 0
  float turn = noise.GetNoise1D((float)Time.GetTicksMsec() / 1000f + noiseOffset);
  heading = heading.Rotated(turn * maxTurnRate * (float)delta);
  ```
  Give each enemy a random `noiseOffset` (and/or its own `FastNoiseLite.Seed`) so they
  don't sway in sync.
- **Spawn leash:** blend the wander heading toward `spawnPos - enemyPos` with a strength
  that is 0 inside the inner radius (~2 tiles in the video) and ramps up with distance
  beyond it (e.g. `Mathf.Clamp((dist - innerRadius) / outerRadius, 0, 1)`).
- **Debug gizmo:** a `Node2D` child overriding `_Draw()` that calls `DrawLine` per
  direction — length ∝ weight, color lerped red→green — with `QueueRedraw()` each
  physics frame while debugging.

## Creator's addendum (scope)

> NOTE: I'm not using this as a replacement for path finding. The purpose of this is to
> control how the AI behaves in its local environment. ie avoid clumping together, strafe the
> player, flee from the player but don't get stuck on walls. NOT get from point a to point b.
>
> The fact that it can avoid simple obstacles is an emergent behavior that came about
> naturally from this system, but not the primary way of avoiding them. Without proper path
> finding, the ai will get stuck if there is a wall between it and its target. However, you
> can combine this with a pathfinder, if you have it seek along the path instead of directly
> at its target.

Implications for our implementation:

- This is a **local steering layer**, not navigation. Long-range travel still needs
  pathfinding (or is simply out of scope if enemies only act near their spawn/aggro area).
- The "seek" target can be swapped: seek the player directly, or seek along a path if/when a
  pathfinder exists.
- Expected behaviors to implement: approach, strafe/circle at range, flee without wall-lock,
  separation (anti-clumping), and smooth noise-driven wandering with a soft spawn leash.

## From the comments

The transcript file contains no comments section, so there is nothing to include.

## Open questions

- **Number of candidate directions (N):** never stated. The gizmo in the video shows a
  radial fan of lines; 8–16 is the typical range for context steering. Fewer = cheaper but
  coarser wall-sliding; verify in-engine.
- **Shaping function curves:** the video says *what* each shaper does (falloff with
  distance, favor sideways, separate at an angle) but never gives the actual functions.
  Candidates to try: `Curve` resources sampled by dot product / distance so they can be
  tuned visually in the editor.
- **Strafe range and follow-distance offset:** the desired orbit radius, the radius at
  which approach weights start fading, and the per-enemy distance offset magnitude are all
  unspecified — tune with the gizmo.
- **How weight sets are combined:** sum, max, or weighted blend is never stated when
  merging approach + strafe + separation + obstacle sets. Sum is the common choice, but
  the combination operator changes behavior significantly.
- **Heading smoothing:** does the enemy snap to the winning direction each tick or rotate
  toward it at a limited turn rate? Not covered; likely needed to fully kill jitter.
- **Obstruction probe details:** ray length, collision mask, and whether a blocked ray
  zeroes the weight or just dampens it are unspecified.
- **Wander noise parameters:** frequency, max turn rate, and whether the 0-weighted
  distribution of Simplex noise alone is enough or needs an additional bias — tune per
  enemy type.
- **Leash radii:** inner radius is given (~2 tiles) but the outer falloff distance is not.
- **2D vs 3D:** the video's game is 2D; the note's C# mapping above assumes `Vector2` /
  `PhysicsDirectSpaceState2D`. If our game is 3D, use `Vector3` directions on the XZ plane
  and `GetWorld3D().DirectSpaceState`.
