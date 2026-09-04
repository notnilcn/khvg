# Personality for Procedural Animations via Second-Order Dynamics (f, ζ, r springs)

Source: t3ssel8r — "Giving Personality to Procedural Animations using Math".
Target: **Godot 4.7 + C#**.

## Core idea: keyframe-free "interpolation curves" via a second-order system

Keyframes + easing curves give inertia/anticipation/overshoot, but procedural animation
(IK-driven bodies, camera follow, aim smoothing, UI motion) has no keyframes. The fix:
**filter a raw game-world input `x` through a second-order dynamical system** to produce
an output `y` that tracks `x` but carries a designed motion "personality".

- Guarantee: when `x` stops changing, `y` eventually settles exactly at `x`.
- Real-world mechanical motion is second order (forces act on acceleration, per Newton's
  second law), so this model naturally *feels* physical.
- Unity's `SmoothDamp` is a special case of this system (with critical damping, ζ = 1).

## The equation of motion

```
y + k1·ẏ + k2·ÿ = x + k3·ẋ
```

(dot = time derivative; ẏ velocity, ÿ acceleration; `k1, k2, k3` are gains.)

Solved for acceleration (what the integrator needs):

```
ÿ = (x + k3·ẋ − y − k1·ẏ) / k2
```

`k1/k2/k3` are mathematically convenient but not artist-friendly, so the system is
**re-parameterized** into three intuitive design parameters:

## Design parameters: f, ζ (zeta), r

- **f — natural frequency (Hz):** overall speed of the response. How fast the system
  reacts and how fast it tends to vibrate. **Does not change the shape** of the motion,
  only its time scale.
- **ζ — damping coefficient:** how the system settles.
  - `ζ = 0` → undamped, vibrates forever.
  - `0 < ζ < 1` → underdamped, vibrates/overshoots then settles (less ζ = more wobble).
  - `ζ = 1` → critically damped, fastest settle with no overshoot (Unity SmoothDamp).
  - `ζ > 1` → overdamped, no vibration, slowly drifts toward target.
- **r — initial response:** what happens in the first instant after `x` moves.
  - `r = 0` → takes time to start accelerating from rest.
  - `r > 0` → reacts immediately.
  - `r > 1` → **overshoots** the target (r = 2 is typical for a mechanical connection).
  - `r < 0` → **anticipation**: moves opposite the target briefly before following.

Mapping (with `ω = 2πf`):

```
k1 = ζ / (π f)          = 2ζ / ω
k2 = 1 / (2π f)²        = 1 / ω²
k3 = r·ζ / (2π f)       = r / ω
```

**Debug/tuning tip:** visualize the **step response** (output after one sudden change in
`x`) — it's the standard way to see a system's whole character at a glance.

## Integration: semi-implicit Euler

State: `y` (position estimate) and `yd` (velocity estimate), initialized at t=0.
Per frame, with timestep `T` (in Godot: `(float)delta` from `_Process` / `_PhysicsProcess`):

```
y  += T * yd
yd += T * (x + k3*xd − y − k1*yd) / k2     // uses the UPDATED y — that's the "semi-implicit" part
```

- Same accuracy as Verlet integration for this system, but simpler.
- If the input velocity `ẋ` is unknown, estimate it as `xd = (x − x_prev) / T` (store the
  previous input each frame). Pass `null`/none when unknown and compute internally.

### Reference C# implementation (Godot 4.7)

Generic over `float`, `Vector2`, `Vector3` (anything supporting `+`, `−`, `* float` —
write three small overloads or a tiny math-ops interface; shown for `Vector3`):

```csharp
public sealed class SecondOrderDynamics
{
    private readonly float _k1, _k2, _k3;
    private Vector3 _xp;   // previous input
    private Vector3 _y, _yd; // state: position + velocity

    public SecondOrderDynamics(float f, float zeta, float r, Vector3 x0)
    {
        _k1 = zeta / (Mathf.Pi * f);
        _k2 = 1f / (2f * Mathf.Pi * f * 2f * Mathf.Pi * f);
        _k3 = r * zeta / (2f * Mathf.Pi * f);
        _xp = x0; _y = x0; _yd = Vector3.Zero;
    }

    public Vector3 Update(float T, Vector3 x, Vector3? xd = null)
    {
        Vector3 vel = xd ?? (x - _xp) / T;  // estimate input velocity if not given
        _xp = x;
        _y += T * _yd;
        _yd += T * (x + _k3 * vel - _y - _k1 * _yd) / _k2;
        return _y;
    }
}
```

Call once per frame from `_Process(double delta)` (or `_PhysicsProcess` for gameplay-
coupled motion) and use the returned value for the driven transform.

## The big failure mode: instability at high f / large T

If `f` is too high relative to the frame rate (or a lag spike makes `T` huge), the
integrator's feedback loop compounds errors and the output **explodes to infinity**.
Root cause: the update is a linear feedback system; written in state-space form
`state[n+1] = A · state[n]`, stability requires both **eigenvalues** of the 2×2 state
transition matrix `A` to have magnitude < 1. Solving that inequality gives:

```
T < sqrt(4·k2 + k1²) − k1        →        T_crit = sqrt(4·k2 + k1²) − k1
```

Two remedies from the video (choose per use case):

1. **Sub-stepping (accurate, costs CPU):** compute `T_crit`; if the frame's `T` exceeds
   it, split the update into `ceil(T / T_crit)` smaller steps (each `T/n`, reusing the
   same `x` per sub-step). In the author's Unity code he used a safety factor,
   `T_crit = 0.8 * (sqrt(4*k2 + k1*k1) - k1)`.

2. **Clamp k2 (cheap, slightly unphysical):** instead of shrinking `T`, solve the
   stability inequality for `k2` and clamp it upward each frame, e.g.
   `k2_stable = max(k2, T²/4 + T·k1/2)`. This slows the dynamics down when frames are
   long — not physically exact, but the goal is only to *prevent catastrophic failure*,
   not to be an accurate physics sim.

Related subtlety (mentioned, not fully derived): at high frequency a **frame-to-frame
jitter** can sneak in, caused by *negative* eigenvalues. Constraining against that too
(essentially requiring `k2 ≥ T²/4`) removes the jitter.

## Author's own production variant: pole-zero matching

For cases where accuracy matters (very fast motion), the author recomputes `k1`/`k2`
**per frame from the current timestep using pole-zero matching** instead of clamping.
More accurate for fast movement, but extra per-frame computation — "might not suit
every application". The exact formulas are **not given in the video** (see Open
questions); the clamped/sub-stepped semi-implicit Euler above is what the video fully
specifies and is the right starting point.

## Worked example from the video (tunable reference values)

Robot character, all driven by these springs:

- **Body position:** ζ = 0.5 (underdamped), r = −2 → anticipation dip before fast moves.
  Extra flavor: body is also **tilted toward the target position** to telegraph intent.
- **Body orientation:** ζ = 1, r = 0 → smooth, critically damped turning.
- **Head orientation:** same as body (ζ = 1, r = 0) **but smaller f** → head lags behind
  the body as cheap **secondary motion**.

Pattern to steal: same input stream fed into several springs with different
(f, ζ, r) = instant layered personality (lead/lag, anticipation, overshoot).

## Implementation order (suggested)

1. Implement `SecondOrderDynamics` for `float` first; test by plotting the step
   response (e.g. drive a `Sprite2D`/`Node3D` position, or draw with `_Draw`).
2. Add the stability guard: sub-stepping (accurate) or `k2` clamping (cheap) — do this
   before shipping anything, lag spikes *will* happen.
3. Extend to `Vector2`/`Vector3`; drive one real thing in-game (camera, body offset,
   aim) and tune f/ζ/r live (expose as `[Export]` properties and rebuild the k's when
   they change — they're cheap to recompute).
4. Layer multiple springs off the same input (body vs head, position vs rotation) for
   secondary motion; optionally tilt/rotate toward the target for intent.
5. Optional gameplay hook: modulate (f, ζ, r) by state — awareness, health, status
   effects — so motion itself communicates game state.

## Open questions

- **Pole-zero matching formulas:** the video names the technique but gives no equations.
  If we need its accuracy, we must derive/lookup the matched-Z transform coefficients
  ourselves or find the author's published code (he has referenced it outside the video).
- **The "cable trick":** the cables in the demo use a separate technique "explained on
  Twitter" — not covered here; needs separate research if cables/chains are wanted.
- **Rotations:** the video applies springs to orientation but doesn't specify
  representation. For Godot we must choose: per-axis angle springs (simple, can gimbal),
  or spring on a quaternion/basis with care. Needs an in-engine experiment.
- **Input velocity `ẋ`:** when is it worth passing the true velocity (e.g. from a
  `CharacterBody3D.Velocity`) vs. finite-differencing the input? Finite difference
  amplifies jittery inputs; true velocity is smoother but couples to gameplay code.
- **Where to tick:** `_Process` (visual smoothness, variable delta) vs `_PhysicsProcess`
  (fixed delta, more stable integration, but motion quantized to physics rate). Probably
  `_Process` for cosmetic layers, `_PhysicsProcess` for gameplay-relevant tracking.
- **Which game systems get springs:** the video suggests camera, character bodies/heads,
  and state-driven parameter modulation, but the actual mapping to our game (which
  nodes, which parameter sets, which states) is a design decision still to make.
