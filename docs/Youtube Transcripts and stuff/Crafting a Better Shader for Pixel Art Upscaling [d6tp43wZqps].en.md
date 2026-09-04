# Pixel Art Upscaling: Anti-Aliased "Sharp Bilinear" Shader

Source video: "Crafting a Better Shader for Pixel Art Upscaling" (t3ssel8r-style sharp
bilinear, building on Cole Cecil's 2017 blog post). Goal: display pixel art at **arbitrary
scale, rotation, offset, and distortion** without nearest-neighbor's uneven-pixel aliasing
and without bilinear's full blur. No extra draw calls, VRAM, texture samples, or pipeline
changes — just a fragment shader that manipulates UVs before a single bilinear sample.

## The problem

- **Nearest-neighbor** keeps pixels sharp but produces distracting aliasing whenever the
  texel-to-pixel ratio is not a whole number, or when the texture is stretched/rotated —
  especially visible in motion (unevenly sized pixels "crawl").
- **Bilinear / mip / anisotropic filtering** (all hardware) removes the aliasing but smears
  everything, which is wrong for pixel art.
- Wanted: **just enough filtering to kill aliasing while keeping the original sharpness.**

### Terminology

- **Texel** — one square element of the texture. **Pixel** — one square element of the
  render target (screen).
- UVs are interpolated across each polygon from its vertices; at each screen pixel the
  interpolated UV typically falls *between* texels.
- Bilinear fact the whole method exploits: **if a pixel's UV lands exactly on a texel
  center, bilinear returns that texel's exact color.** So we can freely move sample points
  as long as we snap them to texel centers when we want sharpness.

## Baseline method (Cole Cecil, uniform scaling only)

Manipulate UVs in the fragment shader before the bilinear sample:

1. Scale UV (0–1) by the texture size to get **texel coordinates**.
2. Snap texel coordinates to the **nearest texel center** (sharp sample).
3. Add a small term that interpolates from one texel center to the next **over a narrow
   band**, whose width is driven by the ratio between pixel size and texel size.
4. Convert back to UVs and sample with the hardware **bilinear** sampler.

At integer ratios with pixel-perfect placement, the evaluated points dodge all
interpolation bands → perfectly sharp upscale. At fractional ratios, only the boundary
pixels blend, exactly enough to hide the uneven pixel sizes. Extends to non-uniform
stretch by making the ratio a `vec2`. **Does not extend to rotation/perspective**, where
texel size and shape vary across the image — that's what the rest of the video fixes.

## Key reframe: bilinear = box filter

Think of texels as solid-colored cells. A bilinear sample at a point equals the **average
color inside a one-texel-sized box** at that point. So the bilinear sampler computes box
averages anywhere in the texture, with box size fixed at 1 texel.

- Ideal anti-aliasing = average texture color inside each **pixel** (a pixel-sized box
  filter in texture space).
- You can **stretch a box along an axis without changing its contained average**, as long
  as you don't cross a texel boundary. So: stretch the pixel-sized box until it's
  texel-sized, then read its average with one bilinear sample.
- The baseline shader was already doing exactly this; the uniform "pixel-per-texel ratio"
  was just the box size in disguise.

## Generalized method (rotation / perspective / arbitrary distortion)

When the texture is rotated or perspective-distorted, the pixel's footprint in texture
space is no longer an axis-aligned rectangle, so the stretch trick can't be applied
exactly. **Compromise: use the axis-aligned bounding box (AABB) of the distorted pixel** —
the smallest box that still covers the whole pixel, and the largest box the bilinear
sampler can validly filter.

### Deformation gradient and box size

- The pixel is a unit square in screen space. Its texture-space deformation is described
  by the 2×2 **deformation gradient** (Jacobian of UV over XY): `du/dx, du/dy, dv/dx,
  dv/dy`.
- Transforming the unit square's corners `(0,0),(1,0),(1,1),(0,1)` by that matrix and
  taking max−min per axis gives the AABB size:
  - width = `|du/dx| + |du/dy|`, height = `|dv/dx| + |dv/dy|`
- On the GPU this is one built-in call: `fwidth(UV)` = `abs(dFdx(UV)) + abs(dFdy(UV))`.
  Multiply by texture size to get the box size in texels. No uniforms needed — the shader
  now derives the filter amount per-pixel from screen-space derivatives, so it handles
  rotation, perspective, and any vertex-level distortion automatically.

### Box size constraints

- **Clamp to at most 1 texel** — the stretch trick assumes no texel boundary crossing.
- **Clamp to above 0** (e.g. `1e-5`) to avoid division by zero.

### Minification: mipmaps + anisotropic filtering

If the required filter amount is larger than 1 texel (texture shrunk on screen), the box
clamp means the shader alone can't fully de-alias — that's what **mipmapping and
anisotropic filtering** are for. But because we rewrote the UVs, the GPU's automatic
mip-level calculation (based on derivatives of the *final* UVs) is unreliable. Fix: sample
with an **explicit gradient** variant, passing the derivatives of the *original* UVs.

### AABB overestimates → quadratic (smoothstep) weighting

The AABB of a rotated pixel overestimates the area to blur. Two compensations:

- Use a slightly undersized box, or
- **(preferred) weight the box center higher than the edges.** Choosing a **quadratic
  averaging window** makes the transition across a texel boundary a **cubic curve**, which
  is exactly `smoothstep` — cheap and built-in. This is why the final shader uses
  `smoothstep` rather than a linear ramp for the interpolation band.

### Transparency: use premultiplied alpha

With **straight alpha**, fully transparent texels have undefined color, so any box average
touching one is undefined; engines guess transparent colors from opaque neighbors, which
fails on pixel art where a transparent texel borders several differently colored opaque
texels (visible fringe). **Premultiplied alpha** gives transparent texels well-defined
color, bypasses the issue entirely, and is otherwise mathematically equivalent. Use it for
any sprite sampled through this shader.

## GDShader implementation (Godot 4.7)

### 2D / canvas_item (fullscreen upscale pass or individual sprites)

```glsl
shader_type canvas_item;

// Sample bilinearly; shader does the "sharp" part itself.
uniform sampler2D tex : filter_linear, repeat_enable;

void fragment() {
    vec2 texture_size = vec2(textureSize(tex, 0));
    // AABB of this screen pixel in texel space, clamped to (0, 1 texel].
    vec2 box_size = clamp(fwidth(UV) * texture_size, 1e-5, 1.0);
    // Texel coordinates, shifted so the box maps onto the interpolation band.
    vec2 tx = UV * texture_size - 0.5 * box_size;
    // Quadratic window -> cubic transition across texel boundaries.
    vec2 tx_offset = smoothstep(vec2(1.0) - box_size, vec2(1.0), fract(tx));
    // Snap to texel center + offset, convert back to UVs.
    vec2 uv = (floor(tx) + 0.5 + tx_offset) / texture_size;
    // Explicit gradients from the ORIGINAL UV so mip/aniso selection stays correct.
    COLOR = textureGrad(tex, uv, dFdx(UV), dFdy(UV));
}
```

Notes for Godot specifically:

- `fwidth`, `dFdx`, `dFdy`, and `textureGrad` all exist in the Godot 4.7 shading language
  (verified in local docs `shader_functions.html`).
- Set the sampler hint to `filter_linear` (override the canvas item's texture filter).
  Use `filter_linear_mipmap_anisotropic` when minification matters, and enable **mipmap
  generation** on the texture's import.
- If sampling the node's own `TEXTURE` instead of a uniform, use
  `TEXTURE_PIXEL_SIZE` for `/ texture_size`.
- For transparent sprites add `render_mode blend_premul_alpha;` and use premultiplied
  alpha assets (CanvasItemMaterial blend mode `Premultiplied Alpha` — `BLEND_MODE_PREMULT_ALPHA`).

### Spatial variant (3D, e.g. pixel-art textures on MeshInstance3D)

```glsl
shader_type spatial;
render_mode blend_premul_alpha; // if transparent

uniform sampler2D albedo : filter_linear_mipmap_anisotropic, repeat_enable;

void fragment() {
    vec2 texture_size = vec2(textureSize(albedo, 0));
    vec2 box_size = clamp(fwidth(UV) * texture_size, 1e-5, 1.0);
    vec2 tx = UV * texture_size - 0.5 * box_size;
    vec2 tx_offset = smoothstep(vec2(1.0) - box_size, vec2(1.0), fract(tx));
    vec2 uv = (floor(tx) + 0.5 + tx_offset) / texture_size;
    vec4 c = textureGrad(albedo, uv, dFdx(UV), dFdy(UV));
    ALBEDO = c.rgb;
    ALPHA = c.a;
}
```

This is where the `fwidth` generalization pays off: perspective distortion on 3D surfaces
is handled per-pixel with no extra work.

## C# / scene setup (Godot 4.7)

Creator's own usage: upscale the game's **internal render resolution** to the display
resolution, plus UI/text drawn directly to display. Works optimally at integer ratios but
also handles fractional ones (e.g. windowed mode).

Whole-game upscale setup:

1. Render the game world into a **SubViewport** at the internal (low) resolution.
2. Display it through a **TextureRect** (or Sprite2D) stretched to the window size, with a
   `ShaderMaterial` using the canvas_item shader above, sampling the SubViewport's
   texture (`filter_linear`, `repeat_disable` for a fullscreen quad).
3. Window stretch mode: set `ProjectSettings` `display/window/stretch/mode = "disabled"`
   (or `"canvas_items"`) and size the TextureRect yourself in C# — in a root script's
   `_Process(double delta)` or on `GetTree().Root.SizeChanged`, set
   `textureRect.Size = GetViewport().GetVisibleRect().Size`.
4. For sprites drawn into the low-res world, keep nearest filtering there; the upscale
   pass does the anti-aliasing. For sprites that need the effect individually (rotated
   Sprite2D, UI at native resolution), put the same shader on them directly with
   `blend_premul_alpha`.

Tunables (from the video):

- Box clamp range: `1e-5 … 1.0` texels (both constraints are required, not optional).
- Weighting window: quadratic via `smoothstep` (preferred over undersizing the box).
- Filtering: bilinear + mipmaps + anisotropic for minification beyond 1 texel.

## Implementation order

1. Drop the canvas_item shader on a TextureRect over a low-res SubViewport; verify sharp
   output at integer scale, smooth-but-crisp output at fractional scale (e.g. 1.5×).
2. Enable mipmaps + anisotropic on the source texture import and confirm `textureGrad`
   keeps minified output stable (no shimmer when zooming out).
3. Switch transparent assets to premultiplied alpha and check edges for fringing.
4. (If needed) port to `spatial` for 3D surfaces with pixel-art textures.

## Open questions

- **Viewport texture mipmaps**: for the fullscreen upscale path the source is a
  SubViewport texture; whether/when Godot 4.7 generates mipmaps for viewport textures
  needs verifying in-engine (mostly irrelevant if the pass only magnifies — box stays
  under 1 texel).
- **Half-texel / pixel-perfect alignment**: the video notes integer ratios land sharp
  "when placed pixel-perfect on screen". Whether our SubViewport→TextureRect placement
  preserves that at integer scales (sub-pixel offsets in the container) should be checked.
- **Scope**: apply per-sprite, fullscreen upscale, or both? Per-sprite handles rotation of
  individual sprites; fullscreen handles the windowed-mode fractional-ratio case. The
  creator used fullscreen + UI only.
- **Premultiplied alpha pipeline**: Godot import/export of premultiplied sprites (and
  whether our art source needs re-export) needs checking; mixing straight-alpha and
  premul-alpha materials in one canvas may reorder blending.
- **Performance on target hardware**: `textureGrad`/`fwidth` are cheap but the effect is
  per-pixel; fine for a single upscale pass, worth profiling if applied to many 3D
  materials.
- **Texture wrapping**: the shader assumes `repeat_enable` behaves at atlas edges; with
  sprite atlases the texel-snap can bleed across atlas boundaries unless each region pads.
