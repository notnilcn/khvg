---
name: godot-compositor-testing
description: Test and debug Godot 4.7 projects (especially RenderingDevice/CompositorEffect rendering addons) from the CLI on this machine — headless import/parse checks, windowed runs with scripted screenshot capture, reimport rules for .glsl vs .gdshader, and handling stuck Godot processes.
when_to_use: When you need to run, import-check, screenshot, or debug a Godot project on this Windows machine via the bundled Godot 4.7.1 binary — particularly GPU-dependent work (CompositorEffect, compute shaders, spatial shaders) that cannot run headless.
---

# Godot CLI testing on this machine

## The binary

- `Godot/Godot-stable_mono_win64_console.exe` — Godot **4.7.1.stable.mono** (use the `_console`
  variant: stdout/stderr go to the terminal). GPU: AMD Radeon 860M, Vulkan, Forward+ works.
- There is also `Godot/Godot-stable_mono_win64.exe` (no console — avoid for testing).

## The two commands you need

```bash
# 1. Import + parse check (no GPU needed). Catches GDScript parse errors and
#    .gdshader/.glsl IMPORT errors. Does NOT run the game, does NOT compile spatial
#    shaders at runtime, and RenderingDevice is null in headless mode.
"Godot/Godot-stable_mono_win64_console.exe" --headless --editor --quit --path <project_dir>

# 2. Windowed run (real GPU). Add `-- --capture` to pass user args to the game:
cd <project_dir> && "<abs_path>/Godot-stable_mono_win64_console.exe" --path . -- --capture
```

Rules of thumb:

- **After adding/editing a `.glsl` compute shader you MUST run command 1 first** — the SPIRV
  is baked at import; a stale cache causes "requires (N) bytes of push constant, supplied (M)"
  errors or silently running the old shader. `.gdshader` (Godot shading language) files do NOT
  need this; they compile at game start.
- GDScript parse errors appear as `SCRIPT ERROR: Parse Error ...` in BOTH commands. A broken
  main-scene script means the game window opens and never quits (scene runs without script).
- Shader compile errors appear as `SHADER ERROR: ...` with a source excerpt at game start.
- Log lines from the engine contain NUL bytes on Windows console: filter with
  `tr -d '\0' < log.txt | grep ...`.

## Scripted capture pattern (use instead of eyeballing a window)

Give the demo scene a capture mode so runs are deterministic and self-terminating:

```gdscript
var _frames := 0

func _process(_delta):
    _frames += 1
    if "--capture" in OS.get_cmdline_user_args() and _frames == 120:
        _capture()
    if _frames > 600:               # hard fallback: never hang forever
        get_tree().quit()

func _capture():
    var img := get_viewport().get_texture().get_image()
    print("screenshot saved: ", img.save_png("res://demo/screenshot.png"))
    get_tree().quit()
```

- `OS.get_cmdline_user_args()` = args after `--` on the command line.
- For render textures that only exist on the render thread, save them via
  `RenderingServer.call_on_render_thread(func(): ...texture_get_data(...)...save_png(...))`.
- Read the PNG back with the ReadMediaFile tool to verify visually.
- To print one-time info from a CompositorEffect (render thread), guard with a static or
  member flag and `print(...)` — it works fine from the render thread.

## Killing stuck Godot processes

Windowed Godot runs can hang (broken main scene, an error loop, or a closed pipe). Check and kill:

```bash
ps aux | grep -i godot | grep -v grep
kill <pid>
```

Prefer foreground Bash with a timeout for game runs; if a run gets backgrounded, do not wait on
it blindly — check its log file (`capture_log.txt`) for progress and kill by pid if wedged.

## RenderingDevice / CompositorEffect gotchas (Godot 4.7.1, learned the hard way)

- **RenderingDevice is not main-thread-safe.** Create/free RD textures and call
  `texture_get_data` inside `RenderingServer.call_on_render_thread(...)`. Main-thread-created
  textures show up as "Texture (binding: N, index 0) is not a valid texture" or read back zeros.
- Effect skeleton that works: `_init()` sets `effect_callback_type` and does
  `rd = RenderingServer.get_rendering_device()` (null in headless/Compatibility — bail out!),
  then `RenderingServer.call_on_render_thread(_initialize_compute)` to compile shaders and
  create samplers/pipelines. Free with `rd.free_rid(shader)` in `NOTIFICATION_PREDELETE`
  (frees dependent pipelines too; deferred until frame end, thread-safe).
- Canonical dispatch: `UniformSetCacheRD.get_cache(shader, 0, [RDUniforms...])` →
  `compute_list_begin/bind_compute_pipeline/bind_uniform_set/set_push_constant/dispatch/end`.
  Push constants: PackedFloat32Array, multiple of 4 floats, GLSL side must match byte size.
- Scene color layer is `RGBA16F` and binds as `image2D` storage; it is NOT a valid
  `texture_copy` source (no CAN_COPY_FROM) — snapshot it with a small copy compute shader.
- Guard every `_render_callback` with `if p_callback_type != EXPECTED: return`.
- CompositorEffects run on Camera3D-attached compositors in SubViewports too (verified 4.7.1).
- Scene-buffer layers (`get_color_layer`/`get_depth_layer`) DO read correctly in effects
  (SubViewports included), as long as the geometry rendered before the callback. If reads
  look "empty"/"zeros", suspect render ORDER first: a spatial shader that writes `ALPHA`
  without `ALPHA_SCISSOR_THRESHOLD`/`ALPHA_HASH_SCALE` sorts into the TRANSPARENT pass
  (plain `discard` does not count as alpha clipping) and lands after POST_SKY effects.
  Scissor/hash fixes the sorting but the engine then forces output alpha to 1.0.
  `access_resolved_color/depth` flags only matter under MSAA.
- `RenderingServer.frame_pre_draw`/`frame_post_draw` do NOT bracket GPU rendering with a
  threaded rendering server (root vs SubViewports render at different times relative to
  them) — never mutate scene transforms around rendering; use persistent camera
  `h_offset`/`v_offset` for projection snapping instead.

## Editor-vs-headless behavior differences

- `@tool` scripts and `EditorInterface` singleton only exist with `--editor`.
- Headless: `RenderingServer.get_rendering_device()` returns null; window size/scaling differ.
- Opening the project in the editor auto-reimports; CLI game runs do not.
