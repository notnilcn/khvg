# Pixel-Perfect Camera: Sub-Pixel Movement via Snap + Loss Reintroduction

## The problem

The game renders a **low-resolution pixel scene** to a **high-resolution output**.
Two rendering styles exist for pixel art games:

- **Pixel perfect** (our bucket): the scene is rendered to a low-res grid, and pixels can
  only exist in cells of that grid. The camera pipeline is the arbiter translating between
  the low-res source and the high-res output.
- **Sub-texel**: assets are pixel art but move with normal floating-point transforms —
  these games don't have this problem at all.

With a moving camera, free (floating-point) camera movement causes texels to **cross grid
boundaries non-uniformly**, producing shimmer and a visible **squash-and-stretch** of
sprites — the image loses coherency with the original pixel art.

## Step 1 — Snap the camera to the pixel grid

The straightforward fix: lock the camera to the grid.

- Compute the **world-space size of one low-res texel** (how much world distance one
  output pixel of the low-res buffer covers).
- Round the camera position to a multiple of that value every frame.

For an orthographic 3D camera in Godot 4.7 / C#:

```csharp
// camera = Camera3D with Projection = Orthogonal, rendering into a low-res SubViewport
float texelWorldSize = camera.Size / lowResHeight;  // world units per low-res texel (Y)

Vector3 p = followTarget.GlobalPosition;
Vector3 snapped = new Vector3(
    Mathf.Round(p.X / texelWorldSize) * texelWorldSize,
    Mathf.Round(p.Y / texelWorldSize) * texelWorldSize,
    Mathf.Round(p.Z / texelWorldSize) * texelWorldSize
);
cameraRig.GlobalPosition = snapped;
```

(For a 2D `Camera2D` the same idea applies — texel world size = 1 world unit if the world
is authored at low-res scale and only the upscale is high-res.)

**Result:** shimmer is gone, but the camera now moves in **discrete blocky steps** — and
the lower the internal resolution, the worse it feels. Unacceptable for gameplay.

## Step 2 — Reintroduce the lost sub-pixel offset (the core trick)

Rounding throws away a sub-texel remainder. The key insight: **capture that remainder and
re-apply it after the low-res render**, where movement happens high-res → high-res and is
therefore perfectly smooth.

### The algorithm

1. Keep the logical camera at its true floating-point position.
2. Compute the snapped position (Step 1) and store the **loss vector**:

   ```
   loss = truePosition - snappedPosition   // per axis, |loss| < 1 texel
   ```

3. Render the world with the **snapped** camera into a dedicated low-res render texture
   (in Godot: a `SubViewport` with `Size = lowResolution`,
   `RenderTargetUpdateMode = SubViewport.UpdateMode.Always`, containing the scene and its
   snapped `Camera3D`/`Camera2D`). All post-processing happens here, on the main pass.
4. Set up a **secondary camera** whose only job is to render that texture as a raw image:
   a quad/plane displaying the `ViewportTexture`, sitting **perpendicular to the view
   direction**, rendered at full output resolution.
   - 3D: a second `SubViewport` (or the root viewport) with its own camera and a
     `MeshInstance3D` `QuadMesh` textured with the first viewport's `ViewportTexture`.
   - 2D equivalent: a `TextureRect`/`Sprite2D` in a `CanvasLayer` showing the
     `ViewportTexture`, with nearest-neighbor filtering.
   - **Disable all extra render passes and post-processing on this second pass** — it just
     blits the image; the first pass already applied everything.
5. Every frame, **offset the quad's transform by the stored loss vector** (scaled into the
   second pass's units). The low-res image slides smoothly under the high-res output while
   the world render itself stays grid-locked.

Each frame the displayed image = render camera position **minus** the discrepancy lost to
rounding — so the view moves smoothly anywhere in the world while the render camera only
ever sits on discrete grid boundaries.

### Result

- Smooth sub-pixel camera motion with a fully grid-aligned render — no shimmer, no jitter,
  no blocky stepping.
- Caveat stated in the video: not 100% shimmer-free, because surfaces **not perpendicular
  to the camera** still alias slightly. But game feel is dramatically improved.

## Rotation — accepted as unsolvable

Rotation **cannot** be made pixel perfect. Intuition from the video: treat each pixel as a
projection onto the surface of a sphere; its position is some cosine of the view vector,
and those cosine values can't all live at fixed grid intervals simultaneously. Doing the
same high-res-offset trick for rotation would require a spherical or cube map of the scene
(~6 extra cameras/render targets) — too expensive and compromising.

So: implement a standard rotation (trivial — a rotation matrix or built-in call, e.g.
rotating the camera rig), **accepting jitter**, and optionally mask it with one of:

- **Small blur** — average neighboring pixels to hide artifacts. Works, but adds a smudgy
  look that departs from the crisp aesthetic. (Controversial.)
- **Global screen-space dither** — the eye has a harder time noticing pixel crawl in a sea
  of differing values. Effective, but gives a slightly washed-out look while rotating.
- **Steer into the jitter** — since the internal resolution is just a parameter of this
  camera system, **increase the pixelation (lower the render resolution) while rotating**
  so jittering areas alias into a deliberate stylized effect. Best when the game's tone
  matches a lo-fi look. Can be combined with other effects.

## Implementation order (Godot 4.7, C#)

1. Low-res `SubViewport` (nearest filtering) containing the world + camera; upscale to the
   window. Verify static scene is crisp.
2. Camera follow with free movement — observe shimmer (baseline).
3. Add grid snapping (`texelWorldSize`, `Mathf.Round` per axis) — observe blocky stepping.
4. Store `loss = true - snapped`; add the second blit pass (`ViewportTexture` on a
   quad/`TextureRect` perpendicular to the view, no post-processing) and offset it by
   `loss` — verify smooth, stable motion.
5. Wire the internal resolution as a tunable parameter; if rotation is needed, pick one of
   the masking strategies above.

## From the comments

No comments section is included in this transcript file — nothing to incorporate.

## Open questions

- **Exact numbers are never given**: the video doesn't state its internal resolution,
  upscale factor, blur radius/strength, or dither pattern/strength. All need in-engine
  tuning.
- **Which axes to snap**: presumably only the two axes in the camera's view plane
  (movement along the view direction doesn't cause texel crawl). For an orthographic
  top-down/3/4 camera this is straightforward; verify per-axis for perspective cameras
  (texel world size varies with depth — the video used an orthographic-style setup).
- **Loss scaling into the second pass**: the offset must be applied in the second pass's
  units (high-res pixels or its world units), i.e. `loss` converted through the upscale
  factor. Sign direction (+/-) needs to be verified in-engine — get it wrong and motion
  doubles instead of canceling.
- **Post-processing ordering**: all effects must live on the low-res pass (or deliberately
  on the high-res blit for full-res effects like the dither mask) — decide per effect.
- **Rotation strategy is a game-design choice**: blur vs. dither vs. dynamic pixelation
  (or no rotation at all) depends on the game's tone; the video explicitly leaves it open.
- **Interaction with physics/smoothing**: snapping happens at render time only; game logic
  keeps using the true floating-point position. Verify this doesn't fight camera smoothing
  or physics interpolation in our setup.
