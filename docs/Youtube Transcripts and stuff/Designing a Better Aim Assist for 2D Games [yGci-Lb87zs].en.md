# Designing a Better Aim Assist for 2D Games

Source: analysis of aim assist in fixed-perspective 2D games (Hades, Death's Door) plus the
creator's own generalization: **aim assist as a continuous function from input angle to
output angle**, built with monotone cubic interpolation. No code is given in the video —
this is a design/math framework; the C# sketch below is our implementation plan.

## The problem with snap-to-target aim assist

Both reference games **snap the player's aim directly onto a chosen target**:

- **Hades:** filter first, then pick. "Which targets are valid in this context?" (line of
  sight if the weapon needs it, on-screen, not invulnerable, within attack range) → "which
  valid target's direction is closest to the player's input direction?" (also bounded by
  distance and arc-distance thresholds). Snaps can be up to **90°** — very noticeable.
- **Death's Door:** pick first, then filter by context priority. "Which hittable objects
  are closest to the aim direction?" → "which is the player most likely to want to hit?"
  with layered priority: puzzle elements > nearby enemies > faraway enemies. Correction
  capped at **15°** and never visualized, so it feels much subtler.

Failure mode of any snapping system: a **mis-snap is catastrophic**. When the player has a
specific target in mind (e.g. aiming at something slightly out of range) and the assist
yanks aim to a different in-range target, the intention/action mismatch is instantly
frustrating — worse when the choice makes no tactical sense (low-threat over high-threat
target, wasting a big/AOE attack on a weak/lone enemy). Snap-based assist must therefore be
*extremely* robust in target identification, because every failure is highly visible.

## Core idea: aim assist as a function f(x)

Represent both **player input direction** and **avatar orientation** as angles on a 2D
phase graph (input angle on x-axis, output angle on y-axis). Aim assist is a function
`f(x)` mapping input angle → output angle:

- **No assist:** `f(x) = x` (identity) — avatar points exactly where the stick points.
- **Assist:** `f(x) ≠ x` — a warping of input space into orientation space.

### Virtual target size (preimage)

A target subtends some **angular size** from the player's position (shrinks with distance,
grows with physical size). With `f(x) = x`, the range of stick inputs that hit the target
equals its angular size. If we choose `f` to **widen the target region**, a larger range of
inputs maps onto the target — the **preimage of the target region under f**, i.e. the
target's *virtual size from the controller's perspective*.

- Even a physically tiny/far target can keep a **minimum virtual size**, setting a
  **skill floor** for aiming difficulty regardless of target size or distance.

### Slope = sensitivity

- **Low-slope regions** of `f` = reduced sensitivity near targets = easier fine aiming.
- Low-slope regions must be **balanced by high-slope regions elsewhere**, because domain
  and range must both cover the full 360° (the function is a map of the circle to itself).
- **Smoothing the curve** removes the jarring sensitivity jump when sweeping onto a target
  (the core advantage over snapping).
- Design knobs this framing enables:
  - Always flatten the slope at target centers (precision-weapon "sticky" feel).
  - Cap how much virtual size is granted (difficulty settings).
  - "Lock-on feel" only: shape sensitivity without changing hit difficulty at all.
  - Same correction for all targets regardless of size (predictability).
  - **Global assist strength** by blending `f` with the identity:
    `output = Mathf.LerpAngle(input, f(input), assistStrength)`.

### What snapping looks like in this framework

Snap-to-target is a valid `f` too: it produces large virtual targets (good), but also
**large output discontinuities** at decision boundaries and **orientation ranges with an
empty preimage** — directions the player can *never* aim at. Smooth functions relax the
target-identification burden: a wrong weighting is a small error, not a catastrophic snap.

## Constraints for a well-behaved f

1. Domain and range are all angles [0°, 360°) with **circular topology** (wraps seamlessly).
2. Slope never negative (monotonic — no direction reversal) and not above some max.
3. Correction magnitude `|f(x) − x|` capped at a reasonable value (compare Death's Door's 15°).
4. **Perfect aim → zero correction:** if the input angle already points at the target
   center, `f(x) = x` there.
5. Explicitly specify the **sensitivity (derivative) at each target center**, per target.

Constraints 4+5 are "function values and derivatives at a set of points" → classic
interpolation problem. Plain **cubic splines don't guarantee monotonicity** (violates
constraint 2); the video's solution is **monotone cubic interpolation** (Fritsch–Carlson):
specify knots at each target center (value = same angle, derivative = chosen sensitivity),
plus off-target knots to bring slope back up so the full circle is covered.

## Implementation sketch (Godot 4.7, C#)

- **Inputs per frame:** player position, aim input angle (from `Input.GetVector(...)` for
  the right stick, or mouse direction — see open questions), list of candidate targets.
- **Per target compute:**
  - `angleToTarget` and **angular radius** ≈ `Mathf.Atan(targetRadius / distance)`
    (exact for a sphere/circle cross-section; for small angles `targetRadius / distance` is fine).
  - Validity gates as needed (range, line of sight via `PhysicsDirectSpaceState2D.IntersectRay`,
    on-screen via `CanvasItem.IsVisibleInTree()`, invulnerability flags).
- **Build knots:** for each valid target, a knot at `angleToTarget` with chosen derivative
  (e.g. 0.3 for sticky); add "release" knots at `angleToTarget ± (angularRadius + margin)`
  with derivative ≥ 1 to restore coverage. Evaluate the monotone cubic at the input angle,
  blend with identity by assist strength.
- C# implementation of Fritsch–Carlson monotone cubic is ~40 lines (no Godot API needed);
  knot angles must be **unwrapped** (use `Mathf.LerpAngle`/`Mathf.Wrapf` carefully) since
  the function lives on a circle — unwrap all knots and the query angle into a common
  winding before interpolating, then wrap the result back with `Mathf.Wrapf`.
- Build a **debug visualization** (plot `f` over the full circle, or draw corrected vs raw
  aim rays) — the video's whole argument is visual; tuning without it will be painful.

## From the comments

This transcript has **no comments section** — nothing to include or reject.

## Open questions

Questions the video explicitly leaves open (not answered — to decide per game design):

- How to handle **targets occluding each other** when the projectile requires line of sight?
- How to handle **moving targets** when the projectile has finite travel speed (lead
  prediction? aim at predicted angular position?)?
- How to handle targets **entering/leaving range or line of sight mid-aim** (hysteresis?
  fade the knot weights in/out to avoid pops?)?
- Should correction magnitude **scale with how fast the stick input is changing** (less
  assist during fast flicks)?
- Does **mouse vs controller** input need different balancing (mouse is usually unassisted)?

Additional ambiguities for our implementation (not covered in the video):

- Exact knot layout between targets is unspecified — spacing of "release" knots and their
  derivatives determine the high-slope zones and the max-correction cap; needs in-engine tuning.
- Concrete parameter values (sensitivity at target center, margin width, max correction,
  assist-strength blend) are design choices — video gives no numbers beyond the reference
  games' 90° (Hades) and 15° (Death's Door) caps.
- Behavior when **two targets' influence regions overlap** — monotone interpolation handles
  it gracefully if knots are ordered, but the desired gameplay feel (blend vs priority)
  is undecided.
