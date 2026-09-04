# Pixel Art Planar Water (depth, refraction, waves, foam, reflections)

Source video: "How I Created Pixel Art Oceans | Pixel Perfect" (the creator works in Unity
— this note translates the technique to **Godot 4.7 + C#**, shaders kept as GDShader).

## Core idea: one layered planar-water shader, not a simulation

The water is **planar, not volumetric** — a single flat plane with everything faked in the
shader. Real-time fluid sim / dynamic geometry is explicitly rejected as too expensive and
pointless at low render resolution (detail would be lost when quantized down anyway). The
whole effect is a **stack of composited layers**, each controlling one aspect of the look:

1. Scene color (what's under the water) at the base
2. Depth-based water color gradient (shallow → deep)
3. Refraction (screen-texture distortion)
4. Shoreline foam band (depth intersection)
5. Scrolling detail textures + procedural surface foam (whitecaps)
6. Planar reflections on top

Because every later layer feeds through the same distortion/compositing path, swapping
textures and blend weights yields completely different looks (stormy ocean vs. tropical
lagoon) from the same shader. **The water must be included in the pipeline's color
quantization / pixelation post-process** — the continuous depth gradient would otherwise
break the pixel-art aesthetic.

## Scene setup

- A `MeshInstance3D` with a `PlaneMesh` as the water surface. For the vertex waves (below)
  it needs to be subdivided (`SubdivideWidth`/`SubdivideDepth`) — coarse is fine, the
  video stresses the displacement is macro-level, not micro.
- A `ShaderMaterial` with the water spatial shader (transparent).
- A second `Camera3D` + `SubViewport` for the reflection pass (see Reflections).

## Layer 1: depth-based water color ("depth fade")

Physically: the further light travels through water, the more it is absorbed/scattered, so
deeper objects look darker and lose color. Faked **without ray tracing** by comparing the
scene depth (geometry under the water) against the water surface depth:

- Compute **depth_fade** = normalized underwater distance, `0` at the surface, `1` at
  max depth.
- `water_color = lerp(shallow_color, deep_color, depth_fade)` (a plain lerp — the video
  blends between two tunable water colors).

GDShader (Godot 4.7 standard depth-linearization snippet):

```glsl
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;

uniform vec4 shallow_color : source_color = vec4(0.25, 0.55, 0.65, 0.5);
uniform vec4 deep_color    : source_color = vec4(0.02, 0.10, 0.25, 0.95);
uniform float depth_fade_distance = 4.0; // world units to reach full "deep"
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

void fragment() {
    float depth_raw = texture(depth_texture, SCREEN_UV).r;
    vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
    vec3 scene_pos = upos.xyz / upos.w;                 // view space
    float depth_fade = clamp(VERTEX.z - scene_pos.z, 0.0, depth_fade_distance)
                       / depth_fade_distance;           // view space: -z forward, so surface (-VERTEX.z) minus scene (-scene_pos.z) = underwater distance; sign per your projection
    vec4 water_col = mix(shallow_color, deep_color, depth_fade);
    ...
}
```

(Exact sign conventions depend on handedness in view space; verify in-engine — `VERTEX.z`
and `scene_pos.z` are both negative in front of the camera, so the underwater distance is
`scene_pos.z - VERTEX.z` with both negative, i.e. `-(scene_pos.z) - -(VERTEX.z)`. Tune once
visually.)

## Layer 2: refraction via screen-texture distortion

Instead of simulating ripple geometry, **distort where we sample the screen color**:

- Generate/sample an **animated noise texture**, use it as a 2D offset on `SCREEN_UV` when
  reading the screen texture (`hint_screen_texture`).
- **Multiply the offset by depth_fade** — objects right at the surface barely distort,
  deeper objects get the extreme wavy effect.
- Implement it as a **color modifier before compositing**, so every later layer
  (including reflections) automatically goes through the same distortion.

```glsl
uniform sampler2D screen_texture : hint_screen_texture, filter_nearest;
uniform sampler2D noise_tex : filter_nearest, repeat_enable;
uniform float refraction_strength = 0.03;
uniform vec2 noise_scroll = vec2(0.02, 0.015);

// in fragment(), after depth_fade:
vec2 noise_uv = SCREEN_UV + noise_scroll * TIME;
vec2 distort = (texture(noise_tex, noise_uv).rg * 2.0 - 1.0)
               * refraction_strength * depth_fade;
vec3 under_color = texture(screen_texture, SCREEN_UV + distort).rgb;
```

Use `filter_nearest` on both textures to keep the pixel-art crispness.

## Layer 3: macro vertex waves (vertex shader)

Micro geometry distortion was ruled out for cost, but **coarse subdivision + vertex-shader
animation is nearly free**. The specific wave formula:

- Wave A: standard sine displacement using time, speed, direction.
- Wave B: same parameters, **rotated 90° around the Y axis**.
- **Displace by A × B** (multiply, not add) — this produces an "ebb and flow", call-and-
  response motion that reads as a good stylized/realistic balance.

```glsl
uniform float wave_speed = 1.0;
uniform float wave_frequency = 0.5;
uniform float wave_amplitude = 0.15;
uniform vec2 wave_dir = vec2(1.0, 0.0);

void vertex() {
    vec2 dir_a = normalize(wave_dir);
    vec2 dir_b = vec2(-dir_a.y, dir_a.x);              // 90° rotation around Y
    float a = sin(dot(VERTEX.xz, dir_a) * wave_frequency + TIME * wave_speed);
    float b = sin(dot(VERTEX.xz, dir_b) * wave_frequency + TIME * wave_speed);
    VERTEX.y += a * b * wave_amplitude;
}
```

Motion matters beyond looks: the video notes static planar water feels out of place next to
other dynamic elements (e.g. wind) — movement is the biggest "this is a liquid" cue.

## Layer 4: shoreline foam (depth intersection mask)

- Compare **two depth values**: the depth of the ground beneath the water vs. the depth of
  the water surface itself (the same comparison as depth fade, but thresholded near zero).
- Where they are close → pixel is at the geometry intersection (shore) → foam mask.
- Use the mask to composite a **foam texture**; the shoreline band is typically **one
  (pixel-art) pixel thick**, so the texture can be kept very simple.
- Deliberate choice: the creator prefers the shoreline foam **breaking up and being
  imperfect** (threshold + noise) over a clean painted line — they considered reusing their
  outline-detection tech for shore foam but rejected it because imperfection reads as more
  natural.

## Layer 5: procedural surface foam (whitecaps)

- Sample a noise texture, apply a **threshold** ("how much foam you want"), composite on
  top as white highlights.
- **Animate the noise** (scroll and/or time-driven) to get shifting whitecaps.
- Conceptually this is "painting highlights onto the water" — foam can also read warm when
  lit, so the foam color is a tunable.

```glsl
uniform sampler2D foam_noise : filter_nearest, repeat_enable;
uniform float foam_threshold = 0.75;
uniform vec4 foam_color : source_color = vec4(1.0, 0.98, 0.9, 1.0);
uniform vec2 foam_scroll = vec2(0.01, 0.02);

float foam_n = texture(foam_noise, UV * 8.0 + foam_scroll * TIME).r;
float foam_mask = step(foam_threshold, foam_n);        // hard step = pixel-art friendly
```

A hard `step` (instead of `smoothstep`) keeps foam edges quantized and on-style.

## Layer 6: planar reflections

Alternatives considered and rejected: **SSR** (cheap but limited to on-screen content),
**cubemaps** (blurred approximation), **ray tracing** (too expensive). Chosen: **planar
reflections** — treat the water surface as a mathematical mirror and render the scene from
a mirrored camera.

### The math

- Plane equation: `Ax + By + Cz + D = 0`, where `(A,B,C)` = the plane's normal and
  `D = -dot(normal, point_on_plane)`.
- Reflection of a point across the plane: `x' = x - 2 * (dot(n, x) + d) * n` (Householder
  reflection; as a matrix, `R = I - 2nn^T` plus a translation term for off-origin planes).

### Implementation in Godot (C#)

- Add a `SubViewport` containing its own `Camera3D` ("reflection camera"), rendered
  **before** the main pass; sample its `ViewportTexture` in the water shader.
- Every frame (or on main-camera movement), copy the main camera transform, apply the
  reflection, push onto the reflection camera:

```csharp
public partial class PlanarReflection : Node3D
{
    [Export] public Camera3D MainCamera;
    [Export] public Camera3D ReflectionCamera;   // inside a SubViewport
    [Export] public Node3D WaterSurface;          // plane: position + up normal

    public override void _Process(double delta)
    {
        Vector3 n = WaterSurface.GlobalTransform.Basis.Y.Normalized();
        float d = -n.Dot(WaterSurface.GlobalPosition);

        Transform3D t = MainCamera.GlobalTransform;
        Vector3 pos = t.Origin - 2f * (n.Dot(t.Origin) + d) * n;
        // Mirror each basis vector across the plane the same way
        Basis b = t.Basis;
        Vector3 bx = b.X - 2f * n.Dot(b.X) * n;
        Vector3 by = b.Y - 2f * n.Dot(b.Y) * n;
        Vector3 bz = b.Z - 2f * n.Dot(b.Z) * n;
        ReflectionCamera.GlobalTransform = new Transform3D(new Basis(bx, by, bz), pos);
        ReflectionCamera.Fov = MainCamera.Fov;
        ReflectionCamera.Near = MainCamera.Near;
        ReflectionCamera.Far = MainCamera.Far;
    }
}
```

- **Performance ("lightweight pipeline" equivalent):** the video uses a separate cheap
  render pipeline for the reflection camera. Godot equivalents: render the `SubViewport`
  at **reduced resolution** (also on-style for pixel art), restrict what it sees with
  `Camera3D.CullMask` / visual layers (exclude particles, water itself, small props),
  disable shadows/MSAA on that viewport.
- **Handedness gotcha:** mirroring flips triangle winding — geometry in the reflection
  pass will be inside-out unless compensated (e.g. `cull_front` on materials in the
  reflection pass, or flipping via the projection). Verify in-engine.
- **Oblique near-plane clipping:** the video mentions oblique projection so underwater
  geometry doesn't leak into the reflection and objects line up with their real-world
  counterparts. Godot 4.7 does **not** expose an oblique frustum on `Camera3D`; common
  workaround is clipping below the water plane in the shaders of reflection-visible
  materials (discard when `world_pos.y` is on the wrong side of the plane), or a
  clip-distance via a global shader uniform. This is the least-directly-portable piece —
  see Open questions.
- **Sampling:** project the reflection texture using `SCREEN_UV` in the water shader and
  **blend with the water color based on viewing angle** (grazing angle → more reflection,
  looking straight down → more water/depth color). A simple Fresnel on the plane normal
  works. Because this is applied as just another texture layer, it inherits the refraction
  distortion for free.

```glsl
uniform sampler2D reflection_tex : filter_nearest;
uniform float reflection_strength = 0.5;

float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.0);
vec3 refl = texture(reflection_tex, SCREEN_UV + distort).rgb;
vec3 col = mix(water_col.rgb, refl, clamp(fresnel * reflection_strength, 0.0, 1.0));
```

## Practical notes / tunables

- Tunable parameters the video calls out: shallow/deep colors, depth fade distance,
  refraction strength, wave speed/frequency/amplitude/direction, foam threshold, foam
  color, reflection blend by angle, and which noise/detail textures are fed in.
- Keep all samplers at `filter_nearest` and let the final image pass through the existing
  **pixelation + color quantization** post-process — the continuous depth gradient in
  particular must be quantized or it breaks the aesthetic.
- The video mentions (in a prior episode's pipeline) post-process outlines and a limited
  palette; the water is built to live inside that system, not bypass it.
- Creator's own follow-up ideas (not implemented): tying water into gameplay — buoyancy,
  physics interactions.

## From the comments

This transcript file contains no comments section, so there is nothing to include or
reject.

## Open questions

- **Oblique projection substitute:** Godot 4.7 has no oblique-frustum camera API. Decide
  between (a) shader-side clip plane on reflection-visible materials (needs a global
  uniform + touching those materials), or (b) accepting minor artifacts below the water
  line. Must be verified in-engine with our pipeline.
- **Winding/culling of the mirrored reflection camera:** whether our materials need
  `cull_front` variants in the reflection viewport, or a projection flip, is engine-
  specific — test and pick one.
- **Which existing post-process stage owns quantization**, and whether the water shader
  renders early enough to be included (transparent-pass ordering vs. the outline/
  pixelation compositor effects in our project).
- **Exact wave/foam/refraction parameter values** are not given in the video — the ones in
  this note are reasonable starting points, to be tuned visually at our render resolution.
- **Depth fade sign convention** in the Godot view-space reconstruction needs a one-time
  visual check (both candidate signs are noted in the snippet).
- Do we need **reflections at all** at our target render scale? It's the most expensive
  layer (second render pass); the video itself treats it as the final, optional-feeling
  polish layer. Game-design call.
- Gameplay integration (buoyancy, swimming, ripple triggers from entities) is explicitly
  out of scope of the video — separate design decision if we want it.
