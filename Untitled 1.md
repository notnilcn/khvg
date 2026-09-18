# PLAN.md — De-brand Pixelizer3D from ProPixelizer + Stable3D

Scope (confirmed): scrub **all of `godot-pixelizer-3d/`** (addon + demo + tests + docs).
Leave `ProPixelizer/` and `Stable3D9ec4e65a8926V1/` untouched as reference-only.
Keep sole attribution in `CREDITS.md`. Do **breaking but obvious renames**
(`ray_*` → `godrays_*`, `cloud_*` → `clouds_*` style; leave anything with no
obvious alternative alone).

## 1. Inventory (what makes the port obvious today)

**ProPixelizer tells** — all in `godot-pixelizer-3d/`:
- `addons/pixelizer_3d/core/pixel_palette.gd:6` — `Port of ProPixelizer's
  Tools/Palette.cs`; `:167` — `(ProPixelizer recomputed…)`; enum `ColorMethod`
  + consts `RESOLUTION=16`, `DITHER_PATTERN_SIZE=16`, exports
  `source/method/weights/v_conversion_curve/use_dither_pattern/dither_pattern`,
  `bake_image()/bake()`, `ColorSwatches/two_closest/match_entry/color_distance`,
  luma `0.3/0.59/0.11`, `4.0 * weights.x` — mirrors
  `ProPixelizer/Tools/Palette.cs`.
- `addons/pixelizer_3d/core/pixel_dither_pattern.gd:7` — `Port of ProPixelizer's
  Tools/DitherPattern.cs`; x-major `4*(x%4)+(y%4)`, `use_color_a`
  (= `PickColor`), Bayer `n/17` — mirrors `DitherPattern.cs`.
- `addons/pixelizer_3d/shaders/pixelizer_macros.gdshaderinc:73,117,140` —
  `(ProPixelizer ColorGrading.hlsl)`, `(ProPixelizer PixelClipAlpha)`; the
  anchor-clip math (`floor(floor - floor(pivot+0.1))+0.01`, `mod<=0.1`,
  edge-survive), macro-space Bayer, LUT layout `x=r+16b / y=g+16band`,
  `pow(c,1/2.2)` — mirrors `SRP/PixelUtils.hlsl` + `SRP/ColorGrading.hlsl`.
- `addons/pixelizer_3d/README.md:13`, `CREDITS.md:5,17` — only places allowed
  to keep names after rewrite (CREDITS keeps, README gets pointer only).

**Stable3D tells** — `addons/pixelizer_3d/` mirrors
`Stable3D…/Source|Shaders` param-for-param:
- `nodes/pixelizer_manager_3d.gd:41` — `(Stable3D parity…)`; `pixels_per_unit=0.5`,
  `internal_resolution=(960,540)`, `zoom_scale`,
  `get_effective_ortho_width/get_configured_ortho_width/get_units_per_texel/get_units_per_pixel`,
  `camera_snap/snap_anchor/remainder_texels`, `mover_snap/register_mover/
  unregister_mover/_snap_movers` (`frame_pre_draw/post_draw`),
  `auto_pin/auto_pin_target/PIN_ID=255/pin_dilate=2.0/debug_pin_mask/pin_subtree`,
  `outline_palette/outline_threshold=0.02`, `cloud_*` + `Effects.Cloud_Shadows`,
  `ray_*` + `get_god_ray_pass()`, `screen_size`, low-res `texel_offset` upscale.
- `compositor/pixelizer_god_ray_effect.gd:4` +
  `shaders/compute/god_rays.glsl:4,115` — `Stable3D "Volumetric Lighting"`,
  `start/range/power`; `ray_quantum/ray_max_stack/band/near_fade/depth_cutoff/
  surface_fade/distance_falloff/tint` = `StablePixelArtGodRays.ush`
  (`SheetQuantum/SheetMaxStack/…/GodRayCameraNearFade*/GodRayDepthCutoff/
  SheetSurfaceFade/SheetDistanceFalloff`).
- `shaders/cloud_fbm.gdshaderinc` (`Effects.Cloud_Shadows`,
  `cloud_shadow/cloud_noise_at/cloud_coverage_from_noise`,
  `2.17/0.37/0.11/0.65/0.35` detail mix) = Stable god-ray shadow section;
  **contract-shared with `MarchingSquaresTerrain` `mst_terrain.gdshaderinc` —
  must rename in lockstep**.
- `shaders/water.gdshader:5,47` (`StablePixelArtWaterNormal.ush`),
  `water_normal_xy` dirs `d0..d3` + phases
  `0.37/1.71/2.11/2.43/4.03/3.17/1.29` + `clamp ±0.85` =
  `StablePixelArtWaterNormal.ush` verbatim.
- `shaders/point_light_volume.gdshader:4` +
  `nodes/pixelizer_point_light_volume_3d.gd:4`
  (`StablePixelArtStencilPointLight.usf`): `pow(1-d)` pool, closest-approach
  halo, `volume_banded()` = Stable `BandCount/BandTightness` block.
- `nodes/pixelizer_camera_controller_3d.gd` (`yaw_step 45`,
  `min/max_ortho_width 480/3840`, `step_yaw/add_zoom_steps/orbit`) =
  `StableOrthographicCameraControllerComponent.h` (`OrbitSnapAngleDegrees/
  MinimumOrthoWidth/MaximumOrthoWidth/StepOrbitYaw/AddZoomInput`).
- `core/pixelizer_outline_palette.gd` (`exterior_/interior_/multiply`, 256×2
  texture) = `FStableOrthographicOutlineColor`
  (`ExteriorColor/bMultiply…/InteriorColor…`, 256-ID palette).
- `shaders/compute/apply_pixelization.glsl` + `anchor_map.glsl` (5×5 claim
  `pos=floor(ps/1.99)`, nearest-depth wins, `outline_palette`,
  `debug_pin_mask`, id 255) + `pixelizer_upscale.gdshader` (`texel_offset`,
  sharp-bilinear, smooth scroll) = Stable pinning +
  `StableOrthographicPixelArtUpscale.usf`
  (`PinStencil/PinDilate/DebugPinMask/PixelShift/SmoothScroll/UpscaleAA`);
  `anchor_map` claim math additionally echoes ProPixelizer's
  `PixelizationMap.shader`.
- `shaders/sky_clouds.gdshader`, `nodes/pixelizer_sky_clouds_3d.gd`,
  `screen_projection.gdshader:26` (`screen_size=960×540`), firefly/day-night
  nodes, `demo/showcase/ue_assets.gd` (`UE showcase`, `Stable3D showcase`,
  `M_Blue/M_Trunk/M_Leaves/M_Grass`, `UCX_*` comment, `demo/assets/ue/`),
  `demo/demo.gd` + `olddemo.gd` (`Stable3D "MyProject"`, `UE demo/oak`),
  `README` UE-showcase tour + Asset notes, `plugin.cfg` description,
  `AGENTS.md/PLAN.md (this file)/HANDOFF.md/tests/` docs.

Universals to **not** touch (no provenance value): Bayer `n/17`, gamma
`1/2.2`, luma weights, LUT `16/256×256`, `SCREEN_UV` projection idea (credit
already given to Lawrey/Schildt/denovodavid in CREDITS).

## 2. Attribution strategy (CREDITS.md only)

- Rewrite `CREDITS.md` as the single attribution: keep author/title names
  (Elliot Bentine — ProPixelizer; Lorenzo Santa Cruz — Stable 3D PixelArt
  Orthographic) with one line each like "techniques studied and
  reimplemented; no source vendored" — **drop the feature-mapping table**
  (`per-object pixel size, anchor clip, …`, `Stable3D parity`) that makes the
  1:1 traceability obvious.
- `addons/pixelizer_3d/README.md` + root docs: replace the
  `port/reimplementation of ideas from … ProPixelizer … Stable 3D` paragraph
  and `UE showcase` tour language with generic capability prose + one pointer
  line `See CREDITS.md for inspirations.`
- Zero in-code mentions after: no `ProPixelizer/Stable3D/.ush/.usf/Volumetric
  Lighting/StencilPointLight/WaterNormal/MoverSnap/CloudFBM/Bentine/Lorenzo/UE
  showcase/MyProject` outside `CREDITS.md`.

## 3. Rename + constant tables (obvious-style only; skip when no obvious alt)

ProPixelizer side (comments scrubbed regardless):

| Old                                                              | New (obvious)                                                       | Notes                                                                                                 |
| ---------------------------------------------------------------- | ------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `PixelPalette`                                                   | `GradePalette`                                                      | class + file `pixel_palette.gd` → `grade_palette.gd`, `PixelizerPaletteBaker` msg                     |
| `ColorMethod` / `NEAREST_RGB…`                                   | `MatchMetric` / keep values (already generic)                       | only rename enum + `method` → `match_metric` if clean; keep `weights`, `v_conversion_curve` (generic) |
| `PixelDitherPattern` / `values` / `use_color_a` / `dither_index` | `DitherMatrix` / keep `values` / `pick_first` / keep `dither_index` | `bayer_4x4()` → `ordered_4x4()`                                                                       |
| `match_entry/two_closest/color_distance`                         | `solve_entry/closest_pair/color_gap`                                | internal only                                                                                         |
| macro comments                                                   | rewrite as "anchor lattice / grade LUT" prose                       | keep `PIXELIZER_*`, `pixelizer_*` uniforms (own brand), keep biases unless §4 changes them            |

Stable side (representative; apply to manager + effect + shader + demo together):

| Old | New |
|---|---|
| `ray_steps/ray_max_distance/ray_intensity/ray_decay/ray_quantize_bands/ray_dust_strength/ray_near_fade_*/ray_depth_cutoff/ray_surface_fade/ray_max_stack/ray_quantum/ray_distance_falloff/ray_tint/ray_time_override/rays_enabled/god_rays_enabled` | `godrays_steps/godrays_max_distance/godrays_intensity/godrays_decay/godrays_quantize_bands/godrays_dust_strength/godrays_near_fade_*/godrays_depth_cutoff/godrays_surface_fade/godrays_max_stack/godrays_quantum/godrays_distance_falloff/godrays_tint/godrays_time_override/godrays_enabled` |
| `PixelizerGodRayEffect`, `get_god_ray_pass()`, `god_rays.glsl` | `PixelizerGodraysEffect`, `get_godrays_pass()`, `godrays.glsl` (+ 240B `PARAMS_SIZE` stays 240B, field order unchanged) |
| `cloud_*` / `Effects.Cloud_Shadows` / `cloud_fbm.gdshaderinc` / `cloud_shadow/cloud_noise_at/cloud_coverage_from_noise` | `clouds_*` / `Effects.Clouds_Shadows` / `clouds_fbm.gdshaderinc` / `clouds_shadow/clouds_noise_at/clouds_coverage_from_noise` (**+ same rename in MST include**) |
| `mover_snap/register_mover/unregister_mover/get_mover_count/_snap_movers/_restore_movers/_movers` | `motion_snap/register_motion/unregister_motion/get_motion_count/…` (manager + applier + object option key) |
| `auto_pin/auto_pin_target/pin_dilate/debug_pin_mask/PIN_ID/pin_subtree` | `auto_subject_pin/subject_pin_target/subject_pin_dilate/debug_subject_pin_mask/SUBJECT_PIN_ID/pin_subject_subtree` |
| `exterior_/interior_` (outline palette) | `outer_/inner_` |
| `internal_resolution` / `screen_size` / `remainder_texels` / `texel_offset` / `snap_anchor` / `camera_snap` | `render_resolution` / `canvas_size` / `snap_residue` / `display_shift` / `snap_reference` / `camera_lattice_snap` |
| `band_count/band_tightness/halo_*/falloff/light_radius/mask_by_scene_depth/depth_bias` (+ `volume_banded`) | `step_count/step_softness/glow_*/decay/light_reach/hide_behind_walls/wall_slack` (+ `stepped_falloff`) |
| `water_normal_xy/normal_scale/normal_scroll` | `water_ripple_xy/ripple_scale/ripple_scroll` (+ rotate `d0..d3` ~5°, nudge phases in §4) |
| `orbit_sensitivity/smoothing/yaw_step_degrees/min/max_ortho_width/step_yaw/add_zoom_steps` | `orbit_gain/orbit_damping/yaw_increment_degrees/min/max_frame_width/step_yaw/add_zoom_steps` (keep method stems; only prefix values change if needed) |
| `ue_assets.gd` / `demo/assets/ue/` / `UE.*` consts / `M_Blue/M_Trunk…` comments | `showcase_assets.gd` / `demo/assets/showcase/` / `SHOWCASE_*` / generic `blue/stone/trunk/leaf/grass` material comments |
| Keep as-is (no obvious alt): `pixel_size`, `outline_id`, `outline_color`, `palette/dither/grid_mode`, `zoom_scale`, `drive_camera_size`, `depth_occlusion`, class_names `PixelizerManager3D/Applier3D/Object3D`, group `pixelizer_manager` | — |

File/class renames update `class_name`, `preload/load("res://…")`, `plugin.cfg`,
`.tscn` node scripts, `project.godot` stays untouched otherwise.

## 4. Parameter/constant drift (look-preserving, forces re-baseline)

Change **addon-chosen** defaults only, slightly, and re-baseline captures:
anchor biases `0.1/0.01` → `0.12/0.015`; `outline_threshold 0.02→0.025`;
`subject_pin_dilate 2.0→2.5`; cloud `threshold 0.6→0.55`, `height 400→380`
(manager) — keep shader math; godrays `steps 24→28`, `max_distance 600→650`,
`intensity 0.5→0.55`, `decay 2.0→1.8`, `quantize 4→5`, `dust 0.15→0.12`,
`max_stack 10→12`; water `foam_amount 0.4→0.45`, `cap_threshold 0.90→0.88`,
`depth_levels 4→5`; light `decay 2.0→1.8`, `step_count 4→5`; camera
`smoothing 8→7`, `orbit_gain 0.15→0.12`; noise `seed 7→11`. Do **not** alter
layout-critical numbers (LUT 16/256, push-constant 20-float/80B apply block,
godrays 240B buffer, id 255 value, `META_*` scales unless renamed jointly).

## 5. Execution order

1. **Contracts audit** (read-only): grep main-client usages
   (`global_palette_lut`, `get_god_ray_pass` + `Set(ray_*)`,
   `register_mover/pixelize_node`, `screen_size`) and MST include surface;
   freeze rename list + alias/shim decision per "breaking but obvious"
   (no compat aliases — update demo/tests/scenes in-pass; flag main-client
   as follow-up).
2. **Docs/attribution**: rewrite `CREDITS.md` (sole credit), trim `README.md`
   ×2, `plugin.cfg` description (`pinning` → `subject pinning`, drop
   `stable`), `demo/` + `tests/` + `AGENTS.md/PLAN.md (this file)/HANDOFF.md`
   prose.
3. **ProPixelizer scrub**: `pixel_palette.gd`, `pixel_dither_pattern.gd`,
   `palette_baker.gd`, `pixelizer_macros.gdshaderinc`,
   `pixelizer_object.gdshader` comments + renames.
4. **Stable scrub**: manager → godrays effect + `godrays.glsl` → `clouds_fbm`
   (+MST) → water → point-light → camera controller → outline palette →
   apply/anchor/upscale → sky-clouds/screen-projection/firefly/day-night →
   `ue_assets` + demo scripts + `demo/assets/ue/` move.
5. **Constant drift** (§4) in one pass so captures change once.
6. **Verify**: `headless --editor --quit` after every `.glsl` edit; windowed
   `-- --capture`; `tests/run_captures.py` then `--update-references` + PIL
   diff review; LSP diagnostics; final banned-term grep
   (`ProPixelizer|Stable3D|\.ush|\.usf|Volumetric Lighting|StencilPointLight|
   WaterNormal|MoverSnap|CloudFBM|Bentine|Santa Cruz|UE showcase|MyProject`)
   must hit **only** `CREDITS.md` (+ untouched reference folders).

## 6. Risks

- Breaking renames invalidate `demo/*.tscn`, test logs, and the **main
  client** (`PixelArtPipelineComponent` probing + `BulletsPixelApplier`) —
  main-client update is out of scope here and must be a named follow-up.
- `clouds_*` rename must stay byte-consistent across manager ↔ factory ↔
  registered materials ↔ godrays buffer ↔ MST terrain, else silent
  mis-shading.
- Capture references will legitimately diff after §4 — re-baseline
  deliberately, don't chase pixel-zero.
