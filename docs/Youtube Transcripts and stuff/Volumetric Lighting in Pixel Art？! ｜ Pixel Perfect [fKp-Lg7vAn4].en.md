# Volumetric Lighting in Pixel Art (Cloud Shadows + God Rays)

Source video is Unity-based; this note translates the technique to **Godot 4.7 + C#**, with
shaders kept as GDShader (usable from C# unchanged). Two systems are covered:

1. **World-space cloud shadows** — a scrolling noise mask projected from a virtual cloud
   plane onto the scene, giving rolling light/shade contrast that makes the world feel alive.
2. **Volumetric god rays** — screen-space ray marching that reuses the same cloud projection
   at each step to find which air has a clear view of the sun, rendered on the low-resolution
   pixel-art grid.

## 1. Cloud coverage mask

**Why not light cookies:** cookies (projective textures) are tied to the light's transform.
If the light moves or the camera rotates, the illusion that clouds exist physically above
the ground breaks. Fix: project through a **world-space reference** instead.

### Noise texture

- Perlin noise, controlled by two parameters: **scale** and **threshold**.
- The threshold clamps values on either side to their nearest extreme → binary
  cloud / not-cloud mask.
- In Godot: generate with `FastNoiseLite` (`NoiseType = Perlin`, `Frequency` ≈ scale) into a
  `NoiseTexture2D`, or bake once to a static `ImageTexture` from C# and pass it to shaders.
  Sampling a texture in-shader is far cheaper than evaluating noise per pixel.

### World-space projection (the key step)

- Define a **virtual ceiling**: a mathematical plane at a fixed height `cloudHeight`.
- **Reconstruct each pixel's world position** by sampling the depth texture and unprojecting
  through the inverse view-projection matrices. In a Godot screen-space (post-process)
  shader this is the documented pattern:

```glsl
uniform sampler2D depth_texture : hint_depth_texture, repeat_disable, filter_nearest;

vec3 reconstruct_world(vec2 screen_uv) {
    float depth = texture(depth_texture, screen_uv).r;
    vec4 upos = INV_PROJECTION_MATRIX * vec4(screen_uv * 2.0 - 1.0, depth, 1.0);
    vec3 view_pos = upos.xyz / upos.w;
    return (INV_VIEW_MATRIX * vec4(view_pos, 1.0)).xyz;
}
```

- **Ray–plane intersection:** from the reconstructed ground position `p`, cast a ray along
  the sun direction `d` (normalized `DirectionalLight3D` forward vector, negated so it
  points *toward* the sun) and solve the linear equation for distance `t`:

```
t   = (cloudHeight - p.y) / d.y
hit = p + d * t
```

- Drop the vertical component and use `hit.xz` as UVs into the noise texture:

```glsl
vec2 cloud_uv = hit.xz * cloudScale + windDir * TIME * windSpeed;
float coverage = step(threshold, texture(cloud_noise, cloud_uv).r);
```

- The time-based UV offset gives the rolling cloud effect.
- Guard the divide: when `d.y` ≈ 0 (sun near horizon) or ≤ 0 (sun below horizon), fall back
  to full shadow or a large `t`.

### Feeding the mask into lighting

In the video the mask is injected into the pipeline's **shadow caster** so every object
samples it via default shadow calculations (static and dynamic alike). **Godot does not let
you inject a custom mask into the built-in shadow map**, so pick one of:

- **Per-material sampling (closest to the video):** in each toon/spatial shader, compute the
  fragment's world position (`(INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz`), do the same
  cloud-plane projection, and multiply the mask into the light/shadow term. No depth
  reconstruction needed here — the vertex already knows its world position. Only the
  post-process god-ray pass needs the depth reconstruction.
- **Post-process darkening (simpler, global):** one full-screen pass that reconstructs world
  position from depth (code above), samples the mask, and multiplies the screen texture.
  Cheaper to set up (one shader), but it darkens *after* lighting, so it also affects
  emissive/sky unless masked, and won't integrate with per-material toon banding.

### Stepped banding (pixel-art stylization)

Instead of blurring noise edges (which breaks the pixel-art look), apply **stepped banding**:
round the noise value to fixed intervals when it is read:

```glsl
float bands = 4.0; // e.g. 3 shadow levels + full light
float n = texture(cloud_noise, cloud_uv).r;
float stepped = floor(n * bands) / (bands - 1.0);
float coverage = step(threshold, stepped);
```

As the threshold is adjusted, different banded regions coalesce, giving partial coverage
with crisp quantized edges that quantize well into a limited palette. Expose parameters
(noise scale, threshold, band count, wind direction/speed, cloud height) in a C#
`[Export]` settings `Resource` to mirror the video's "pixel art settings class".

## 2. Volumetric god rays (ray marching)

Shadows say where light doesn't reach the floor; god rays convey light interacting with the
air between camera and object. The video deliberately chose a **physically grounded ray
march** over the cheaper card-based (billboard) approximation, because it leverages the
low-resolution pixel-art grid.

### Algorithm (screen space, one full-screen pass)

1. Reconstruct the pixel's world position from depth (same code as above).
2. March along the vector **camera → world position** in a fixed number of steps.
3. **At each step, repeat the cloud-plane projection** (ray from the marched point toward
   the sun, intersect cloud plane, sample the cloud mask). Steps with a clear view of the
   sun accumulate light.
4. Accumulated value = god-ray intensity for that pixel; composite additively over the scene.

```glsl
const int STEPS = 16; // tune: perf vs. coherence
vec3 ray = world_pos - camera_pos;
float accum = 0.0;
for (int i = 0; i < STEPS; i++) {
    vec3 p = camera_pos + ray * (float(i) / float(STEPS - 1));
    float t = (cloudHeight - p.y) / sunDir.y;
    vec2 uv = (p + sunDir * t).xz * cloudScale + windDir * TIME * windSpeed;
    accum += 1.0 - step(threshold, texture(cloud_noise, uv).r); // 1 where sun is visible
}
accum /= float(STEPS);
```

- Unlike the cloud *shadows* (a projection of the ground only), this mask is representative
  of coverage for the **whole volume between ground and sky**.
- The creator has a **decay parameter** so rays fade the farther light travels (from his
  comment reply) — multiply each step's contribution by a falloff of distance marched.
- Run this pass on the **low-res render target** (the pixel-art grid) so the rays are
  inherently chunky and match the aesthetic.

### The noise/blur trade-off (creator is undecided)

- Step count too high → hefty performance hit; too low → incoherent, noisy samples.
- Common fix is **smoothing/blurring** neighboring pixels; it hides sampling flaws and gives
  homogeneous light flooding, but deviates significantly from the pixel-art aesthetic.
- The creator is explicitly **uncertain which way to go** and asked viewers for opinions —
  treat blur-vs-crisp as an open art-direction choice (see Open questions).

## From the comments

- **Endpoint-lerp march optimization (creator-endorsed — implement this):** instead of
  recomputing the plane intersection at every march step, compute just two UVs — the
  projection of the camera position and of the world position — and sample along
  `mix(camUV, worldUV, marchIndex)`. All points along the march fall on a line, so the UVs
  interpolate linearly. Creator: *"This is a great optimisation."* Note this assumes the
  projected UVs vary linearly along the march, which holds when the march segment is short
  relative to its distance to the plane — verify visually.
- **Directional blur along the light ray (recommended over Gaussian):** keep the sample
  count low and smear along the light direction ("fake motion blur"). Filaments keep sharp
  cross-edges but smooth along the ray; cheap. In Godot, a second small screen-space pass
  sampling along the projected sun direction. Included — directly addresses the creator's
  blur-vs-crisp dilemma better than omnidirectional blur.
- **Quantize the god rays after smoothing, aligned with the cloud band steps:** keep the
  blur's de-noising, then re-step the result to the same 3–4 bands as the shadows so the
  palette stays limited. Several commenters suggested this; one adds a **step-weighting
  function** (e.g. log-spaced steps, or an AnimationCurve — in Godot a `Curve` resource) for
  artistic control of band spacing. **Creator's caveat (unresolved):** his decay parameter
  makes rays fade with distance, so he's unsure quantizing the alpha will have the usual
  unifying effect. Include the idea; flag the decay interaction as untested.
- **"Dustiness" layer:** add a second, very slow, high-threshold noise sampled to modulate
  god-ray *intensity* per point — sells particulate atmosphere for one extra texture sample.
  Included — cheap and stylistically compatible.
- **Fractal (fBm) noise:** stack a few scales/thresholds of Perlin for more detailed cloud
  blobs. Trivial in Godot — set `FractalOctaves` on `FastNoiseLite`. Included as an option;
  note fBm softens edges, which may fight the stepped-banding look.
- **Shadows as a multiplier on the toon banding:** rather than compositing shadows
  separately from diffuse banding, feed the shadow (and cloud mask) in as a subtraction
  factor that warps/intensifies the toon bands — avoids ugly intersections where band edges
  cross shadow edges on curved surfaces. Trade-off: shadow shapes become less crisply
  defined (seen only secondarily). Included — relevant since our pipeline also quantizes
  shadows into bands; whether crisp cloud-shadow edges matter is an art call.
- **Frustum-shaped voxel volume (Wronski, Siggraph 2014 — "Volumetric Fog: Unified Compute
  Shader-based Solution"; what Unreal does):** compute lighting into a low-res 3D texture in
  frustum space, then march through it with trilinear sampling. **Rejected for now** — much
  heavier (needs `CompositorEffect` compute + 3D texture, and trilinear filtering works
  against the quantized look), but recorded as the reference if the 2D march ever hits its
  quality ceiling.
- **Adaptive/frustum-space step sizes:** march in frustum space (~one sample per screen
  pixel) with heuristics to skip samples. Noted but rejected — extra complexity that fights
  the low-res aesthetic; the endpoint-lerp optimization captures most of the savings.
- Rejected without further note: pure praise, "I like the blur"/"I like the pixelated look"
  opinions with no technique (they only confirm the art-direction split), and Patreon/outro
  chatter.

## Godot 4.7 implementation notes

- **Where the passes live:** simplest route is a screen-space shader on a full-screen
  `ColorRect` over a `SubViewportContainer` (the standard low-res pixel-art setup), reading
  `hint_screen_texture` / `hint_depth_texture`. If more control is needed (half-res buffer,
  separable blur passes), use a `CompositorEffect` (`EffectCallbackType =
  EFFECT_CALLBACK_TYPE_POST_OPAQUE`) driven from a C# script — compute shaders from C# work
  fine via `RenderingDevice`.
- **Getting the sun direction:** from C#, take the scene's `DirectionalLight3D`, use
  `-light.GlobalTransform.Basis.Z` (forward), set it as a shader global
  (`RenderingServer.GlobalShaderParameterSet`) or per-material uniform each frame if it
  animates.
- **Sky pixels:** depth ≈ 1 for sky; clamp the march to a max distance (or treat sky as
  fully sunlit) so rays still appear over the horizon line.
- **TIME and low-res:** the wind offset uses shader `TIME`; on a low-res target the noise
  texture filtering should be `filter_nearest` to stay pixel-snapped.

## Open questions

- **No concrete parameter values are given in the video.** Cloud plane height, noise scale,
  threshold, band count, wind speed, march step count, and decay rate must all be tuned
  in-engine. Start with STEPS = 16–32 on the low-res buffer, bands = 4, threshold ≈ 0.5.
- **Which integration path for cloud shadows:** per-material sampling (integrates with toon
  banding, but every shader needs the code) vs. one post-process darkening pass (simpler,
  but sits on top of lighting/emissive). Depends on how our toon material is structured.
- **Blur vs. crisp god rays:** the creator explicitly left this undecided. Candidates:
  (a) directional blur along the sun direction, (b) blur + re-quantize to the shadow bands
  (with the creator's decay-alpha caveat), (c) raw low-sample rays leaning into the noise.
  Needs an art-direction decision and in-engine comparison.
- **Does the endpoint-lerp optimization hold at our scale?** The linear UV interpolation is
  exact only in the limit; with tall scenes/steep sun angles the projected UVs may curve.
  Verify against the per-step reference implementation before committing.
- **Interaction with existing quantization/upscale passes:** where in the post chain the
  god-ray pass should sit (before/after color quantization and the pixel-art upscaler) is
  untested — quantizing after god rays preserves the palette but may crush the ray gradient.
- **Performance budget:** ray marching every pixel on the low-res target still costs
  STEPS × pixel-count texture samples; if the game also runs other compositor effects,
  measure and consider half-res rays + directional blur.
- **Sun near/below horizon:** behavior when `sunDir.y` approaches 0 (grazing light) is
  unspecified — clamp, fade rays out, or switch off below a threshold?
