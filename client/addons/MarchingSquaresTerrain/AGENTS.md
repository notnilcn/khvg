# AGENTS.md — MarchingSquaresTerrain (Yūgen's Terrain Authoring Toolkit)

Godot 4.6 editor plugin (v1.2.4, MIT) for authoring 3D terrain in-editor using the **marching squares** algorithm (Y-axis height variation only — no overhangs). Made by Yūgen, forked from Jackachulian. Plugin entry point: `plugin.cfg` → `editor/marching_squares_terrain_plugin.gd`.

Upstream docs (read these first for feature work):
- `documentation/plugin_quick_guide.md` — user-facing tool overview.
- `documentation/documentation+/internal_tool_system.md` — **the** guide for adding tools/attributes/terrain settings. Follow its exact sequence.
- `documentation/documentation+/code_locations.md` — where specific logic lives (grass/texture mixing, normals, gizmo lines, triplanar mapping, ridge/ledge).
- `documentation/documentation+/code_style_guide.md` — mandatory style for any contribution (summarized below).
- `documentation/documentation+/navmesh_guide.md` — NavigationMesh baking recipe.

## Architecture

### Runtime/algorithm (`algorithm/`, `resources/`, `utils/`)
- `algorithm/terrain/marching_squares_terrain.gd` — `MarchingSquaresTerrain` (`@tool Node3D`), the scene root. Holds the `chunks` dictionary, global settings (`dimensions`, `cell_size`, `blend_mode`, `wall/ridge/ledge_threshold`, `noise_hmap`, `animation_fps`), 15 terrain textures + 6 grass sprites + albedos/scales, and `StorageMode` (**BAKED** = mesh/grass/collision stored on disk, faster load; **RUNTIME** = rebuild everything from heightmaps). Saves chunks on `NOTIFICATION_EDITOR_PRE_SAVE`.
- `algorithm/terrain/marching_squares_terrain_chunk.gd` — `MarchingSquaresTerrainChunk` (`@tool MeshInstance3D`), one grid chunk. Stores `height_map` + cell geometry arrays (color maps, grass mask, wall colors) and builds the mesh via SurfaceTool. `merge_mode` (`Mode` enum / `MERGE_MODE` thresholds) controls how blocky vs round the terrain is — per chunk, settable via the Chunk Management tool. Its `StaticBody3D` collision child is **deleted and recreated on every save** — put groups on the chunk node itself, not the body (group names must start with `navmesh_` to be copied over, see navmesh guide).
- `algorithm/terrain/marching_squares_terrain_cell.gd` — `MarchingSquaresTerrainCell` (RefCounted): per-cell marching squares geometry (corner heights `ay/by/cy/dy`, rotations, floor vs wall).
- `algorithm/terrain/marching_squares_terrain_vertex_color_helper.gd` — computes the `color_0`/`color_1` vec4s per vertex. **These are not colors**: the shader (`resources/shaders/mst_terrain.gdshaderinc`) treats the 8 channels across both vec4s as on/off flags; the pair of "on" channels encodes one of 16 texture slots. Texture idx 0 = rr, 1 = rg, … 15 = aa (see `MarchingSquaresTerrainPlugin._set_vertex_colors`). Slot 15 (aa) = invisible terrain. Only textures 1–6 can have grass. This helper also handles ridge/ledge boundary coloring (`BLEND_EDGE_SENSITIVITY`).
- `algorithm/grass/marching_squares_grass_planter.gd` — `MarchingSquaresGrassPlanter` (`@tool MultiMeshInstance3D`): places grass sprites per cell from the grass mask + ridge/ledge thresholds. Wind/animation lives at the top of `resources/shaders/mst_grass.gdshader` (`animation_fps` 0 = smooth noise wind, >0 = stepped pixel-art motion).
- `resources/mst_data_handler.gd` + `resources/mst_chunk_data.gd` — persistence. Per-terrain data dir: `[SceneDir]/[SceneName]_TerrainData/[NodeName]_[uid]/chunk_X_Y/metadata.res` (compressed `MSTChunkData`). Only dirty chunks are saved; orphaned chunk/terrain dirs are auto-cleaned on save.
- `editor/tools/scripts/marching_squares_geometry_baker.gd` + `resources/shaders/mst_terrain_bake*.gdshader` — optional **runtime texture baking** (`enable_runtime_texture_baking`): bakes the multi-texture terrain into per-polygon atlas textures so the game runs a simple baked shader instead of the 15-texture one.
- `utils/marching_squares_thread_pool.gd` — thin WorkerThreadPool group-task wrapper.
- `mc.tct` — legacy/original chunk script kept for reference; the live chunk script is the one in `algorithm/terrain/`.

### Editor (`editor/`)
- `marching_squares_terrain_plugin.gd` — `MarchingSquaresTerrainPlugin` (EditorPlugin): input handling (`_forward_3d_gui_input` → `handle_mouse`), brush raycasts, the `TerrainToolMode` enum (BRUSH, LEVEL, SMOOTH, BRIDGE, GRASS_MASK, VERTEX_PAINTING, DEBUG_BRUSH, CHUNK_MANAGEMENT, TERRAIN_SETTINGS), the `draw_pattern()` dispatch, and all undo/redo actions (`draw_*_pattern_action`). New tool behavior is coded here.
- `marching_squares_ui.gd`, `tools/scripts/marching_squares_toolbar.gd`, `marching_squares_tool_attributes.gd` — the dock UI. Attributes are declared as Dictionary entries in `marching_squares_tool_attributes_list.gd` (name/type/label/default; types in `SettingType` enum) and enabled per-tool via boolean exports in `marching_squares_tool_attribute_settings.gd`.
- `tools/*.tres` — the tools themselves: `MarchingSquaresTool` resources. New tools also need a preload entry in `tools/scripts/marching_squares_toolbox.gd`.
- `gizmos/` + `marching_squares_terrain_gizmo_plugin.gd` — editor gizmos (brush visual, chunk outlines).
- `tools/scripts/marching_squares_texture_*.gd`, `marching_squares_quick_paint.gd` — texture presets (swappable palettes, exported to `resources/texture_presets/`) and quick paints (`resources/quick_paints/`, global ones in `quick_paints/global/`).

## Code style (required — from `code_style_guide.md`)
- `extends` before `class_name`; class names prefixed `MarchingSquares` or `MST` (prefer `MST` for long names).
- snake_case members; private functions get a leading `_` (variables do not).
- **Tabs** in GDScript (the opposite in gdshader files). Two blank lines between functions and after the variable block; `#region` names lowercase.
- Variable typing with a space before the colon: `var foo : float` (functions/dictionaries use normal `:` convention).
- Hidden terrain exports use `@export_custom(PROPERTY_HINT_NONE, "", PROPERTY_USAGE_STORAGE)` with a setter that pushes the value wherever it's needed (e.g. shader param) instead of call chains.
- `##` doc comments only for editor-visible docs; all comments start with a capital.

## Gotchas
- Everything is `@tool` — scripts run in the editor. Guard editor-only APIs and be careful with scene-tree assumptions at load.
- Vertex "colors" are texture indices (see above) — never treat them as tint colors.
- Terrain settings are stored on the `MarchingSquaresTerrain` node via storage-only exports and edited through the plugin's Terrain Settings tool, not the default inspector.
- The plugin UI reads attribute dictionaries top-to-bottom — order in `MarchingSquaresToolAttributesList` defines UI order.
- Brush interactions: SHIFT+LMB keeps drawing, SHIFT+scroll resizes, ALT/ESC/RMB deselects; CTRL samples (level height / chunk select).

## Testing / verification
There is no test suite. Verification = opening a project that has the plugin enabled in the Godot editor and exercising the tools. This checkout (`addons/` at the workspace root) has **no `project.godot`** — it's a bare addons folder; the same addon also exists under `main/client/addons/MarchingSquaresTerrain` (a Godot 4.7 C# project). 4.6→4.7 compatibility was audited against the official 4.7 docs (`code_examples/godot-docs-html-stable/`) and is clean: no breaking-change or behavior-change APIs are used, no deprecated APIs are used (the plugin docks via `add_control_to_container`, not the deprecated `add_control_to_dock`), and all engine virtuals return explicitly on every path. A bundled Godot 4.7.1 mono binary lives at `C:/Users/Clinton/g/Godot/`; see the `godot-compositor-testing` skill for headless import/parse checks (e.g. `Godot-stable_mono_win64_console.exe --headless --import --path <project>`) before claiming a script change parses.
