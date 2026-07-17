# client/Scripts/World/CLAUDE.md

Guidance for the world-rendering and camera scripts. See the root and `client/CLAUDE.md` for
project-wide context.

## Files here

- `WorldRenderer3D.cs` — `Node3D`; root script of `Scenes/world_3d.tscn`. Singleton
  (`WorldRenderer3D.Instance`). Owns the `PhantomCamera3D` (fetched in `_Ready` via
  `GetNode<Node3D>("PhantomCamera3D").AsPhantomCamera3D()`), handles camera yaw/pitch/zoom input,
  and exposes `SetCameraFollowTarget(Node3D)` / `ClearCameraFollowTarget()`.
- `CameraController2D.cs` — the 2D counterpart. Receives the yaw offset from `WorldRenderer3D`'s
  arcball input via `SetRotationOffset(...)` so 2D movement stays aligned with the 3D camera.
- `HexGridOverlay2D.cs` / `HexGridOverlay3D.cs` — debug grid overlays.

`WorldRenderer3D` and `CameraController2D` are deliberately separate scripts. Do not merge them.

## Other phantom_camera gotchas

- `follow_mode` is **get-only** on the C# wrapper (`PhantomCamera3D.FollowMode`). It can only be set
  in the `.tscn`. `set_follow_target()` silently early-returns when `follow_mode` is `NONE` or
  `GROUP` — so if the scene isn't configured for third person, assigning `FollowTarget` from C#
  no-ops with **no error**, and the camera sits at the origin.
- The C# `EraseFollowTarget(Node3D)` maps to GDScript `erase_follow_targets` (plural — the
  group-mode one), *not* the singular `erase_follow_target`. Use
  `pcam.Node3D.Call("erase_follow_target")` to clear a single target.
- `set_follow_target()` connects to the target's `tree_exiting`, so the target must already be in
  the scene tree when assigned.
- Freeing the follow target is recoverable but fails quietly: `_should_follow` goes false and the
  camera silently freezes at its last position. Any code that respawns the followed model must
  reassign `follow_target`.
