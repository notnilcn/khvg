# AGENTS.md — addons/3dpixelart

## What this is

Godot 4.7 addon that renders 3D objects in a pixel-art style —
per-object macro-pixels, outlines, no pixel creep, full-resolution
lights/shadows, palette-LUT grading (ortho-first: orthographic cameras
supported, perspective left as-is).
Pipeline is implemented and **working**; user docs are in `README.md`.
The hard-won Godot 4.7.1 rendering facts are in the "Architecture
invariants" section below and in the `godot-compositor-testing` skill —
read them before touching the render pipeline. Demo scenes live at the
project root: `demo/demo.tscn` (showcase) and
`demo/demo_ortho_pixel_map.tscn` (DemoMap mirror).

## File layout

- `plugin.gd` / `plugin.cfg` — EditorPlugin; all runtime types use
  `class_name`, so the plugin itself does nothing.
- `nodes/pixel_art_pipeline_3d.gd` (`PixelArtPipeline3D`) — pipeline driver, one per scene.
  Creates the metadata SubViewport + camera, owns the shared RD textures,
  attaches the compositor effects, feeds registered materials. Runtime only.
- `nodes/pixel_art_object_3d.gd` (`PixelArtObject3D`) — per-object config
  (pixel size, outline id/color); sets instance uniforms, auto-adds the
  metadata render layer and assigns the material. Its `snap_*` exports are
  intentionally no-ops.
- `nodes/pixel_art_camera_snap.gd` (`PixelArtCameraSnap`) — ortho texel snap
  via camera `h_offset`/`v_offset` (denovodavid port). No transform mutation.
- `core/pixel_palette.gd` (`PixelPalette`, `@tool`) — bakes 256×256 palette
  LUT PNGs via an inspector button. `ColorMethod.NEAREST_LAB` matches in CIE
  Lab (CIE76); bake-time only, LUT format unchanged.
- `core/pixel_dither_pattern.gd` (`PixelDitherPattern`) — 4×4 ordered dither
  matrix (Bayer default).
- `nodes/pixel_art_pinning.gd` (`PixelArtPinning`) — ownership-tracked
  subject pinning (`instance_id → orig outline_id`), auto-pin view-target
  group, `configure_pinned_primitive()`. Pin stencil 255 reserved.
- `core/pixel_outline_palette.gd` (`PixelOutlinePalette`) — 256×2 HDR palette
  resource (row 0 exterior / row 1 interior, alpha = multiply flag), bakes
  `R16G16B16A16_SFLOAT` bytes (16-bit float RGBA).
- `nodes/pixel_art_editor_preview.gd` (`PixelArtEditorPreview`, `@tool`) —
  minimal editor helper; full pipeline stays runtime-only in v1.
- `compositor/` — `outline_pass.gd` (`PixelArtOutlinePass`, PRE_OPAQUE,
  main), `god_ray_pass.gd` (`PixelArtGodRayPass`, POST_SKY, main, additive
  god rays through the cloud layer), `macro_pixel_pass.gd`
  (`PixelArtMacroPixelPass`, POST_SKY, main), `metadata_export_pass.gd`
  (`PixelArtMetadataExportPass`, POST_SKY, metadata SubViewport), plus
  `pixel_art_shared_buffers.gd` (`PixelArtSharedBuffers`, static shared RIDs
  between viewports, incl. 256×2 outline palette).
- `shaders/pixel_art_object.gdshader` — one spatial shader serving both
  cameras; branches on `float(CAMERA_VISIBLE_LAYERS) == metadata_cull_mask`.
  Also hosts sharp-bilinear texture sampling (`use_sharp_bilinear`), mover-snap
  pivot rounding (`use_mover_snap`, VIEWPORT_SIZE lattice), palette composite
  (`pixel_art_outline_palette` + `use_outline_palette` with multiply flags),
  and per-material cloud-shadow code (uniforms pushed by the pipeline, incl.
  tightness/octave-drop/gap-erosion/detail + shared `cloud_fbm.gdshaderinc`).
- `shaders/*.glsl` — compute passes: `copy_color`, `outline_pass`
  (ID edge + FarCurve `max(-H,-V)` + normal-bias fallback),
  `anchor_map`, `apply_anchor_map` (smooth-shift `pixel_shift`, `upscale_aa`
  sharp-bilinear vs nearest, dilated pin-255 probe + `debug_pin_mask`),
  `export_metadata`, `god_rays` (endpoint-lerp march + `cloud_fbm.glslinc`,
  near-fade/depth-cutoff/surface-fade/max-stack/quantum/distance-falloff).
- `shaders/cloud_fbm.glslinc` / `cloud_fbm.gdshaderinc` — shared
  procedural cloud FBM (hash 1664525/1013904223, rot 0.825/0.564).
- `../../demo/` content (project root, not part of the addon —
  game/demo content, not pipeline): `water.gdshader` (Pixel Art Oceans
  layers 1–5 example; transparent, renders after the pixelization passes,
  so not itself pixelated) + 4-sine `water_normal_xy()` slope normal
  (dirs 0.94/0.34, -0.42/0.90, 0.17/0.98, 0.76/-0.64, *.32 clamp ±0.85),
  `demo_lighting_rig.tscn` (sun + 0.09 moon fill + Sky + fixed-exposure
  env + fog), `stencil_point_light.gdshader` (radial `pow(1-d)` +
  view-ray halo + BandCount/Tightness),
  `demo_firefly_spawner.gd` (jitter/boundary SmoothStep(0.65,1.0)/tri-sine
  hover/separation, 0.05 s timer, single-player),
  `demo_ortho_pixel_camera.gd` (orbit dominant-axis lock, yaw-step 45°,
  zoom→`camera.size`, yaw-only move), `demo_debug_hud.gd` (status +
  crosshair), `demo_ortho_map.gd` + `demo_ortho_pixel_map.tscn`
  (DemoMap mirror), `demo.gd` + `demo.tscn` (main showcase).

## Architecture invariants (do not redesign)

- **One shader, two cameras.** The metadata camera culls only layer 20
  (`1 << 19`); the material branches on `CAMERA_VISIBLE_LAYERS`. Same
  instances render in both viewports — no mirrored geometry.
- **Never write ALPHA in the metadata branch.** Writing ALPHA sorts the
  material into the transparent pass, which renders after the POST_SKY
  compositor effects (the original "all zeros" bug). Pixel size + outline id
  are packed into G instead: `G = (id + pixelSize * 256) / 4096`, white
  background decodes to size 16 = "not pixelized". Metadata color texture is
  `R16G16B16A16_UNORM` (11 payload bits don't survive 8-bit).
- **Never snap/restore transforms around rendering.**
  `RenderingServer.frame_pre_draw/post_draw` do not bracket GPU rendering
  with a threaded rendering server — the two viewports see different
  transforms (measured 1px anchor-grid phase mismatch). Use persistent
  projection offsets (`PixelArtCameraSnap`) instead. Mover snap is
  pivot-rounding in-shader (`use_mover_snap`, VIEWPORT_SIZE lattice), never
  transform mutation.
- **Pin 255 reserved.** Outline ID 255 marks pinned subjects (stencil 255,
  masked `Shift=0` with `pin_dilate` 3×3 probe); never author it as a
  regular `outline_id` (`use_random_uid` never emits it).
- **Reversed-Z**: near = 1.0, far = 0.0, "nearest" = largest raw depth. The
  pixelization map kernel depends on this.
- The engine color layer (`RGBA16F`) cannot be a `texture_copy` source —
  copy it via compute (`copy_color.glsl`).
- `apply_anchor_map` push constants are 4 vec4s (grading/debug/shift/pin =
  16 floats); `god_rays` uniform buffer is mat4 + 10 vec4s (224 bytes). Keep
  GLSL and GDScript sizes in sync or dispatches silently use stale shaders
  (reimport `.glsl` after editing).

## Gotchas

- RenderingDevice is not main-thread-safe: create/free RD textures via
  `RenderingServer.call_on_render_thread(...)` (see
  `PixelArtPipeline3D._create_textures_rt`). On viewport resize the shared
  RIDs in `PixelArtSharedBuffers` are invalidated BEFORE the old textures are
  freed, so compositor passes early-return instead of building uniform sets
  with just-freed RIDs ("Texture (binding: N) is not a valid texture" spam) —
  keep that ordering.
- **The god-ray pass must precede the macro-pixel pass in the compositor
  effects array** — same-callback-type (POST_SKY) effects run in array order,
  and the rays must be in the color layer before `copy_color` so they get
  pixelated. If that ordering ever breaks (rays visible un-pixelated in a
  capture), dispatch the ray compute inside
  `PixelArtMacroPixelPass._render_callback` between the copy and apply
  dispatches instead.
- **The pipeline pushes cloud uniforms to registered materials** (on export
  change and on `register_material`) and pushes `cloud_sun_dir` to materials
  + the god-ray pass every frame in `_process` when clouds are enabled. Keep
  these pushes in sync when adding cloud uniforms. Non-pixelized materials
  (terrain, water) register via `register_cloud_material` (the
  `_cloud_materials` list) to receive ONLY the cloud uniforms — their shaders
  must implement the coverage themselves under the same uniform names.
  Reference implementation: `cloud_coverage_from_noise()` +
  `cloud_noise_combined()` in `shaders/pixel_art_object.gdshader`. (No
  in-repo `register_cloud_material` consumers yet; nothing else implements
  this — do not cite external files as references.)
- **`PixelArtPipeline3D.camera_snap` must be assigned** for measured
  units-per-pixel and smooth scroll: without it `get_units_per_pixel()`
  falls back to `pixels_per_unit × zoom_scale` and `remainder_texels`
  stays zero, so `smooth_scroll` silently does nothing. Wire it in scene
  setup code (see `demo/demo.gd` `_build_camera`).
- **Cloud-shadow coverage is the fraction of sun blocked** (0 = clear, 1 =
  overcast): `smoothstep(threshold - softness, threshold, n)` over the raw
  noise, quantized into `cloud_shadow_levels` when
  `cloud_shadow_banding_enabled` (hard contour rings vs smooth penumbra).
  Materials darken by `1 - coverage * cloud_shadow_strength`. (The pre-fix
  code did `mix(1.0, coverage, strength)` on a binary step — that darkened
  the cloud GAPS, inverted from the god-ray/sky convention.) `cloud_bands`
  now feeds only the sky sheet / god-ray gap quantization.
- **Editing a `.glsl` compute shader requires a reimport** or stale SPIR-V is
  used. `.gdshader` changes are picked up on game start.
- Requires Forward+ or Mobile; MSAA off; `scaling_3d_scale` 1.0 BILINEAR;
  TAA off (`apply_recommended_settings()` enforces all four when
  `apply_recommended` is true, otherwise warnings are printed).
- `PixelArtPipeline3D` must see the camera before `_setup()`; it retries lazily in
  `_process` — keep that.

## Testing

Use the `godot-compositor-testing` skill. Binary:
`Godot/Godot-stable_mono_win64_console.exe` (4.7.1 mono, Forward+).

- Parse/import check: `godot --headless --editor --quit --path code_examples/3dpixelart`
- Run demo + auto-screenshot: `cd code_examples/3dpixelart && godot --path . -- --capture`
  (writes `demo/screenshot.png` + metadata debug dumps, quits at frame 120);
  `--debug-view=N` selects the pixelization debug view (1=map, 2=metadata,
  3=metadata depth). `--plain` skips the showcase content (LUT grading,
  clouds/god rays, textured box, pond) and renders the original baseline
  scene — use it as the pixel-creep/outline regression capture.
  `--debug-overlay` prints `get_debug_status_line()` at capture
  (internal IxI, upscale mode, units/px, orthoW, remainder, grid-phase,
  snap-delta, pin, outlines).
- Run ortho map: `godot --path . demo/demo_ortho_pixel_map.tscn -- --capture`
  (writes `demo/demo_ortho_pixel_map.png`, quits at frame 120).

Verify pixelization visually via the screenshot; `debug_view = 2` shows the
packed metadata buffer.

## Known loose ends

- `PixelArtPipeline3D` syncs the metadata camera in `_process` at default
  process priority 0 — if something updates the main camera later in the frame
  (e.g. a camera host at process_priority 300), the metadata render runs one
  frame behind and moving objects dissolve into their anchor-dot grid. Raise
  the pipeline node's `process_priority` above whatever drives the camera.
- Transparent pixelized objects composite against un-pixelated depth
  (pixelated depth write-back not ported). Corollary: pixelized objects write
  depth only at anchor fragments, so ANY transparent surface between the
  camera and a pixelized object draws over it (the object "dissolves" into
  anchor dots) — keep such surfaces opaque/alpha-scissor.
- `save_shared_textures_debug` depth row-length guard is approximate.
