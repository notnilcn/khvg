# 3D Pixel Art

A Godot 4 addon that renders 3D objects in a pixelated 2D-sprite art style:
chunky macro-pixels glued to each object, per-object outlines, no pixel creep,
full-resolution real-time lights and shadows, and palette-LUT color grading.

It is a port of the Unity URP asset **ProPixelizer** by Elliot Bentine.

## Requirements

- Godot **4.7+**
- **Forward+ or Mobile** renderer (uses `CompositorEffect`s; the Compatibility
  renderer is not supported)
- MSAA 3D **disabled** (`rendering/anti_aliasing/quality/msaa_3d = 0`)
- `scaling_3d_scale` = **1.0**

`PixelArtPipeline3D` prints warnings at runtime when these are violated.

## Installation

1. Copy the `addons/3dpixelart/` folder into your project.
2. Enable the plugin: **Project → Project Settings → Plugins →
   "3D Pixel Art"**.

All runtime classes are registered via `class_name`, so enabling the plugin is
optional for pure runtime use — but keep it enabled so the editor picks
everything up cleanly.

## Quick start

1. **Add a `PixelArtPipeline3D` node** anywhere in your scene (one per scene). It
   picks up the viewport's current camera automatically, or assign
   `camera` explicitly. It builds the whole pipeline for you: a metadata
   SubViewport + camera, and two compositor effects on your camera.
2. **Add a `PixelArtObject3D`** as a child of each `MeshInstance3D` you want
   pixelized. It automatically adds the metadata render layer to the target
   and assigns the pixel-art material. Set `pixel_size` (1–5) per object.
3. Done — run the scene. Shadows, occlusion and lights stay full-resolution;
   only the pixelized objects get macro-pixels and outlines.

Optional: for an **orthographic** camera, add a `PixelArtCameraSnap` node to
eliminate pixel creep when the camera moves.

A working example is in `demo/demo.tscn` (project root).

## Nodes

### `PixelArtPipeline3D` (one per scene, runtime only)

Drives the entire render pipeline for one camera.

| Property | Description |
|---|---|
| `camera` | Camera that renders the pixelized scene (default: current camera). Its cull mask **must include** the metadata layer. |
| `metadata_layer` (1–20, default 20) | Render layer marking pixelized objects. The metadata camera renders only this layer. |
| `export_metadata_depth` (default on) | Export metadata depth for correct ordering where pixelized objects overlap. Off = cheaper, minor artifacts at overlaps. |
| **Outlines** | |
| `depth_test_intersections` | Suppress outlines where objects intersect. |
| `depth_test_threshold` | Depth-test suppression threshold (default 0.0001). |
| `use_normal_edge_detection` | Detect creases from view-space normals. |
| `normal_edge_detection_sensitivity` | Higher = less sensitive crease detection (default 3.5). |
| **Color Grading** | |
| `global_palette_lut` | Optional palette LUT (baked by `PixelPalette`) applied to all pixelized pixels after lighting. |
| **Debug** | |
| `debug_view` | 0 = off, 1 = anchor map, 2 = metadata buffer, 3 = metadata depth. |

Methods: `register_material(material)` / `unregister_material(material)`
(called automatically by `PixelArtObject3D`),
`save_shared_textures_debug(color_path, depth_path)` (dumps the shared
metadata textures as PNGs).

### `PixelArtObject3D` (child of a `MeshInstance3D`)

Per-object configuration. All of its parameters are applied as
*instance* shader uniforms, so one material serves every object.

| Property | Description |
|---|---|
| `target` | The `GeometryInstance3D` to pixelize (default: parent). |
| **Pixelization** | |
| `pixel_size` (1–5, default 3) | Macro-pixel size in screen pixels. |
| `use_object_position` (default on) | Anchor the pixel grid to the object's pivot (recommended — grid follows the object). |
| `pixel_grid_origin` | World-space grid origin when `use_object_position` is off. |
| **Outline** | |
| `outline_id` (0–254) | Adjacent pixels with different IDs get an outline. |
| `use_random_uid` | Random outline ID at runtime. |
| `use_root_uid` / `use_root_color` | Inherit outline ID / color from the topmost `PixelArtObject3D` ancestor, so a hierarchy reads as one object. |
| `outline_color` | Outline color; alpha = opacity. |
| `edge_highlight_color` | Crease highlight: < 0.5 darkens, > 0.5 lightens, exactly 0.5 disables. |

The **Snapping** group (`snap_position`, `snap_euler_angles`, …) is currently
**inert** — object render snapping was intentionally not ported (see
"Limitations").

### `PixelArtCameraSnap`

Snaps the camera *projection* (via `h_offset`/`v_offset`) to the world-space
texel grid so pixelized objects don't creep when an **orthographic** camera
moves. Never touches the camera transform.

- `mode = FIXED_PIXEL_SIZE` (default): set `pixel_size` (world
  units per screen pixel); the camera ortho size is derived from it.
- `mode = FROM_CAMERA_SIZE`: keeps your camera ortho size and derives the
  pixel size from it.

Perspective cameras are not helped by snapping (a warning is printed once).

### `PixelPalette` + `PixelDitherPattern` (resources, `@tool`)

Bake a 256×256 palette LUT for color grading:

1. Create a `PixelPalette` resource, assign a `source` texture (every distinct
   pixel is a palette swatch), choose a `method` (HSV nearest is the default),
   optionally assign a `PixelDitherPattern` (defaults to Bayer 4×4) for
   dithered gradients.
2. Press **Bake LUT** in the inspector — the PNG is written to `output_path`.
3. Assign the baked LUT to `PixelArtPipeline3D.global_palette_lut` (post-lighting,
   scene-wide) or to a material's `palette_lut` with `use_color_grading`
   (per-object, pre-lighting).

## The material

`shaders/pixel_art_object.gdshader` is assigned automatically by
`PixelArtObject3D`. Useful surface uniforms: `albedo_texture`, `base_color`,
`lighting_ramp` (toon ramp), `normal_map`, `emission_texture`,
`alpha_clip_threshold` + `use_dithering` (Bayer screen-door transparency),
`palette_lut` + `use_color_grading`. Per-object pixel/outline settings come
from instance uniforms — don't set them on the material directly; use
`PixelArtObject3D`.

## How it works (short version)

Each pixelized object renders twice: once into the main camera and once into a
metadata SubViewport (render layer 20 only), where its view-space normal,
outline ID and pixel size are packed into a 16-bit buffer. Compositor effects
on the main camera then run outline detection (ID edges + normal creases) and
pixelization (5×5 nearest-anchor map → anchor replication with an occlusion
fix). The material keeps only one anchor fragment per macro-block and reads
its outline from the shared outlines texture. See `AGENTS.md` for details.

## Limitations

- One `PixelArtPipeline3D` per scene; runtime only (not `@tool`).
- Transparent pixelized objects composite against un-pixelated depth
  (pixelated depth is not written back to the camera depth target).
- Object render snapping (`PixelArtObject3D.snap_*` exports) is not driven:
  snap/restore of transforms around rendering races with the threaded
  rendering server. Camera projection snapping (`PixelArtCameraSnap`) plus
  pivot-anchored pixel grids already prevent creep, so it isn't needed.
- Editing a `.glsl` compute shader requires a **reimport** (Project → Tools →
  reimport, or restart the editor) or stale SPIR-V is used.

## Credits

- Original asset: **ProPixelizer** by Elliot Bentine (Unity URP).
- Camera texel snapping ported from denovodavid's `Camera3DTexelSnapped`.
- Godot 4 port of both.
