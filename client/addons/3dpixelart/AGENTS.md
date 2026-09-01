# AGENTS.md — addons/3dpixelart

## What this is

Godot 4.7 addon: a port of the Unity asset **ProPixelizer** (Elliot Bentine)
that renders 3D objects in a pixel-art style — per-object macro-pixels,
outlines, no pixel creep, full-res lights/shadows, palette-LUT grading.
Pipeline is implemented and **working**; user docs are in `README.md`.
The hard-won Godot 4.7.1 rendering facts are in the "Architecture
invariants" section below and in the `godot-compositor-testing` skill —
read them before touching the render pipeline. The demo scene is
`demo/demo.tscn` (project root).

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
  LUT PNGs via an inspector button.
- `core/pixel_dither_pattern.gd` (`PixelDitherPattern`) — 4×4 ordered dither
  matrix (Bayer default).
- `compositor/` — `PixelArtOutlinePass` (PRE_OPAQUE, main),
  `PixelArtMacroPixelPass` (POST_SKY, main), and the
  metadata export effect (POST_SKY, SubViewport), plus `PixelArtSharedBuffers`
  (shared RIDs between viewports).
- `shaders/pixel_art_object.gdshader` — one spatial shader serving both
  cameras; branches on `float(CAMERA_VISIBLE_LAYERS) == metadata_cull_mask`.
- `shaders/*.glsl` — compute passes: `copy_color`, `outline_pass`,
  `anchor_map`, `apply_anchor_map`, `export_metadata`.

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
  projection offsets (`PixelArtCameraSnap`) instead. Object render snapping
  is deliberately not ported for the same reason.
- **Reversed-Z**: near = 1.0, far = 0.0, "nearest" = largest raw depth. The
  pixelization map kernel depends on this.
- The engine color layer (`RGBA16F`) cannot be a `texture_copy` source —
  copy it via compute (`copy_color.glsl`).

## Gotchas

- RenderingDevice is not main-thread-safe: create/free RD textures via
  `RenderingServer.call_on_render_thread(...)` (see
  `PixelArtPipeline3D._create_textures_rt`).
- **Editing a `.glsl` compute shader requires a reimport** or stale SPIR-V is
  used. `.gdshader` changes are picked up on game start.
- Requires Forward+ or Mobile; MSAA off; `scaling_3d_scale` 1.0.
- `PixelArtPipeline3D` must see the camera before `_setup()`; it retries lazily in
  `_process` — keep that.

## Testing

Use the `godot-compositor-testing` skill. Binary:
`Godot/Godot-stable_mono_win64_console.exe` (4.7.1 mono, Forward+).

- Parse/import check: `godot --headless --editor --quit --path code_examples/3dpixelart`
- Run demo + auto-screenshot: `cd code_examples/3dpixelart && godot --path . -- --capture`
  (writes `demo/screenshot.png` + metadata debug dumps, quits at frame 120);
  `--debug-view=N` selects the pixelization debug view (1=map, 2=metadata,
  3=metadata depth).

Verify pixelization visually via the screenshot; `debug_view = 2` shows the
packed metadata buffer.

## Known loose ends

- Benign "Texture (binding: 5) not a valid texture" errors on quit.
- `PixelPalette` LUT baker is untested.
- Transparent pixelized objects composite against un-pixelated depth
  (pixelated depth write-back not ported).
- `save_shared_textures_debug` depth row-length guard is approximate.
