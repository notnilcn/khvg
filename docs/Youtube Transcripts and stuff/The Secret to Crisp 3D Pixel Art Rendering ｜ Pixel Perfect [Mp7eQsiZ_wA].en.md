# Crisp 3D Pixel Art Rendering — Point-Sampled Two-Stage Pipeline + Perceptual Palette Quantization

Source: Pixel Perfect, "The Secret to Crisp 3D Pixel Art Rendering" (Unity URP in the video;
translated here to **Godot 4.7 + C#**).

## Core idea

A 3D scene rendered at low resolution only looks like pixel art if two things are true:

1. **No interpolation anywhere in the scale chain.** The GPU's default bilinear filtering
   samples the 4 nearest texels around each target UV and blends them by subpixel offset.
   On a downscaled 3D render this smears hard edges into gradients and introduces colors
   that never existed in the source — destroying the crisp, grid-aligned pixel look.
2. **The output color space is quantized to a limited, hand-chosen palette** — the defining
   aesthetic hook of classic pixel art (NES: 54 colors total, 25 on screen; modern artists
   typically pick 16–32). A low-res render without quantization has pixel-art *shapes* but
   not the pixel-art *feel*.

So the pipeline is: render the 3D scene → **point-sampled downsample** to a fixed low
resolution → **quantize every pixel to the nearest palette color in a perceptual color
space** → **point-sampled upscale** back to the screen.

## Stage 1 & 3: Point-sampled downsample / upscale ("pixel locking")

- **Downsample** the entire scene to a fixed target resolution using **point (nearest)
  filtering**. Every pixel in the low-res buffer then contains the *exact* color of its
  corresponding source texel — pixels are locked into a discrete grid.
- **Upscale** that buffer back to screen size with a **point-clamp sampler**, so every
  texel maps to a uniform block of monitor pixels and the hard edges survive.
- In Unity this was done with a scriptable render pass injected **after solid geometry is
  rendered to the camera target** (before post-processing), stashing the low-res texture in
  a handle for later passes.

### Godot 4.7 equivalent (simplest form)

- Put the 3D world in a **SubViewport** at the target low resolution (e.g. 480×270 or
  320×180 — pick per game; the video gives no number).
- Display it through a **SubViewportContainer** with `Stretch = true`, and set
  `TextureFilter = Nearest` on the container (CanvasItem.TextureFilterEnum.Nearest).
  This gives point-sampled upscale for free.
- Point sampling on the downsample side is implicit: the SubViewport *renders* at low
  resolution rather than filtering a big image down, which is even crisper than the video's
  downsample pass (see "From the comments" — this is the approach the creator agrees is
  valid, and their pipeline can degrade to exactly this).
- Alternative global approach for the whole game: ProjectSettings
  `display/window/stretch/mode = "canvas_items"` + `display/window/size/viewport_width/height`
  set to the low resolution, with `rendering/textures/canvas_textures/default_texture_filter = 0`
  (Nearest). Simpler, but quantization-as-post-process is harder on this path, so the
  SubViewport route is preferred here.

## Stage 2: Palette quantization in CIE Lab

### Why not RGB distance

The naive approach — treat RGB as a unit cube and pick the palette color with the smallest
**Euclidean distance** — is mathematically correct but *perceptually* wrong:

- RGB is **not perceptually uniform**: equal numeric steps ≠ equal perceived changes.
- Human eyes are far **more sensitive to green than blue**, and RGB distances ignore
  luminance sensitivity, chroma compression, and nonlinear hue response.
- In pixel art, local contrast and subtle hue ramps carry disproportionate visual weight,
  so these errors are especially visible.

Fix: convert each pixel to **CIE Lab (CIELAB, 1976)**, a space designed so equal distances
≈ equal perceived differences, then nearest-neighbor search there. Lab coordinates:
**L** = lightness, **a** = green↔magenta, **b** = blue↔yellow.

### Conversion chain (per pixel)

```
sRGB  --(linearize)-->  linear RGB  --(3×3 matrix, D65)-->  CIE XYZ  --(nonlinear f)-->  Lab
```

1. **Linearize** sRGB: `c_lin = c ≤ 0.04045 ? c/12.92 : ((c+0.055)/1.055)^2.4` per channel.
2. **XYZ** via the standard sRGB→XYZ matrix (D65 white).
3. **Lab**: `f(t) = t > (6/29)³ ? t^(1/3) : t/(3·(6/29)²) + 4/29`, with D65 white point
   `(Xn, Yn, Zn) = (0.95047, 1.0, 1.08883)`:
   - `L = 116·f(Y/Yn) − 16`
   - `a = 500·(f(X/Xn) − f(Y/Yn))`
   - `b = 200·(f(Y/Yn) − f(Z/Zn))`
4. Pick `argmin_i distance(pixel_lab, palette_lab[i])` (plain Euclidean in Lab = CIE76;
   CIE94/CIEDE2000 are optional upgrades) and output that palette color.

The math is all public domain ("the math for this is all publicly available") and the
per-pixel work is embarrassingly parallel — a perfect fragment-shader job.

### GDShader implementation (canvas_item, on the SubViewportContainer)

Apply this as a `ShaderMaterial` on the SubViewportContainer. It does **upscale +
quantization in one pass**: the container's Nearest filter point-samples the low-res
texture, and each screen pixel is snapped to the palette.

```glsl
shader_type canvas_item;

// Palette passed from C# as TWO arrays: sRGB colors (for output) and the same
// colors pre-converted to Lab (for distance). Pre-converting on the CPU means
// the shader never converts the palette per-pixel — see "From the comments".
uniform vec3 palette_rgb[32];
uniform vec3 palette_lab[32];
uniform int palette_size = 16;

vec3 srgb_to_linear(vec3 c) {
	vec3 lo = c / 12.92;
	vec3 hi = pow((c + vec3(0.055)) / 1.055, vec3(2.4));
	return mix(lo, hi, step(vec3(0.04045), c));
}

vec3 linear_rgb_to_xyz(vec3 c) {
	// sRGB D65 matrix; GLSL mat3() takes COLUMNS.
	mat3 m = mat3(
		vec3(0.4124564, 0.2126729, 0.0193339),
		vec3(0.3575761, 0.7151522, 0.1191920),
		vec3(0.1804375, 0.0721750, 0.9503041));
	return transpose(m) * c; // row-major matrix * column vector
}

float lab_f(float t) {
	const float d = 6.0 / 29.0;
	return t > d * d * d ? pow(t, 1.0 / 3.0) : t / (3.0 * d * d) + 4.0 / 29.0;
}

vec3 rgb_to_lab(vec3 srgb) {
	vec3 xyz = linear_rgb_to_xyz(srgb_to_linear(srgb));
	vec3 w = vec3(0.95047, 1.0, 1.08883); // D65 white point
	vec3 f = vec3(lab_f(xyz.x / w.x), lab_f(xyz.y / w.y), lab_f(xyz.z / w.z));
	return vec3(116.0 * f.y - 16.0, 500.0 * (f.x - f.y), 200.0 * (f.y - f.z));
}

void fragment() {
	vec3 c = texture(TEXTURE, UV).rgb; // nearest-filtered low-res source
	vec3 lab = rgb_to_lab(c);
	float best = 1e20;
	vec3 best_rgb = palette_rgb[0];
	for (int i = 0; i < palette_size; i++) {
		vec3 d = lab - palette_lab[i];
		float dist = dot(d, d);
		if (dist < best) {
			best = dist;
			best_rgb = palette_rgb[i];
		}
	}
	COLOR = vec4(best_rgb, 1.0);
}
```

### C# side (Godot 4.7)

- Build the scene: `SubViewportContainer` (stretch, Nearest filter) → `SubViewport`
  (low fixed size, `Disable3D = false`, `OwnWorld3D` as needed) → 3D world.
- In a C# script, hold the palette as `Color[]`. Convert each entry to Lab **once**
  (same formulas as above, in C#) whenever the palette changes, then push both arrays:

```csharp
var mat = (ShaderMaterial)container.Material;
mat.SetShaderParameter("palette_rgb", rgbVec3Array); // Vector3[]
mat.SetShaderParameter("palette_lab", labVec3Array);
mat.SetShaderParameter("palette_size", palette.Length);
```

- Because quantization is **pure data on the GPU**, the color identity is no longer baked
  into assets: swap the palette array at runtime (time of day, biome, damage flash,
  gameboy-mode toggle) and the whole image re-tones instantly. This is one of the video's
  headline benefits.

## Suggested implementation order

1. SubViewport at low res + SubViewportContainer (Stretch, Nearest). Verify crisp
   point-sampled output with a flat-shaded test scene (the video's "fruit bowl" check:
   hard boundaries between objects, no color bleeding between meshes, checkerboard
   patterns keep their identity).
2. Add the quantization ShaderMaterial with a small hard-coded palette (16 colors).
3. Move the palette to a C#-driven uniform with CPU-side Lab pre-conversion; add a debug
   hotkey to swap palettes at runtime.
4. (Optional, shipping) Bake the RGB→Lab conversion into a 3D LUT if profiling shows the
   per-pixel conversion is hot — see the LUT discussion below.
5. Future passes the creator teased (not covered here): outlines, stylized lighting,
   reflections — leave room in the pipeline for them.

## From the comments

- **"Just render to a low-res texture with point filtering instead of injecting into the
  pipeline"** — creator agrees this is correct for what's shown, but their injected-pass
  design keeps the **high-resolution source available** for extra clarity techniques that
  decide *which* pixel wins each low-res cell, and it can degrade gracefully (the first
  stage becomes a plain texture copy if they later start at low res). **Included**, and it
  directly supports the SubViewport approach recommended above for our first version: in
  Godot, rendering the SubViewport at low res natively *is* the "render low-res + point
  filter" option. Revisit only if we want the hi-res source for smarter pixel selection.
- **"Wouldn't a LUT be much faster?" (9:49)** — creator agrees: for shipping they'd bake a
  **3D LUT cube** (RGB in → quantized palette color out) instead of per-pixel conversion;
  they chose realtime math for iteration (swap palettes and tweak colors on the fly).
  **Included** as a shipping optimization; keep the realtime version during development.
  In Godot this would be a `Texture3D` sampled in the shader, rebuilt in C# whenever the
  palette changes.
- **"LUTs are bad for discretely-valued functions" (reply to the above)** — a trilinearly
  filtered LUT approximates *smooth* functions well, but palette quantization is a step
  function, so you'd need nearest-filtered LUT sampling, which reintroduces artifacts.
  Better: **use a LUT only for the smooth RGB→Lab conversion** (computed once), keep the
  nearest-palette search exact. **Included** — this is the refined version of the LUT
  idea. Note our shader already sidesteps most of the cost by pre-converting the *palette*
  to Lab on the CPU; the per-pixel scene→Lab conversion is what a LUT would accelerate.

## Open questions

- **Target low resolution**: the video never states one. Choose per game (e.g. 480×270,
  320×180) and decide how non-integer window scales behave (black bars vs. slightly
  uneven texel blocks — the video assumes uniform blocks).
- **Palette size and contents**: 16 vs 32 colors is mentioned as typical; no concrete
  palette is given. This is a game art-direction decision.
- **Quantize before or after downsample?** The video implies quantization runs on the
  low-res buffer (cheaper, and matches "every low-res pixel holds one exact color").
  Verify in-engine that quantizing at screen resolution after point-sampling gives
  identical results (it should, since nearest upscaling duplicates texels).
- **HDR/tonemapping interaction**: the video doesn't address HDR. In Godot, decide whether
  the SubViewport renders HDR and where the sRGB linearization point is; sampling an
  already-tonemapped LDR buffer is the simplest correct path.
- **Do we need the high-res source later?** The creator's future "image clarity"
  techniques use it to pick which pixel represents each low-res cell. If we want those,
  the pipeline needs a full-res render + explicit downsample pass (a `CompositorEffect`
  in Godot) instead of a natively-low-res SubViewport.
- **Dithering** (a classic pixel-art companion to palettes, and mentioned in the video's
  intro imagery) is not covered in this video — presumably a future episode.
