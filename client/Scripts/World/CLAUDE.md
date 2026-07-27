# client/Scripts/World/CLAUDE.md

Guidance for the world-rendering and camera scripts. See the root and `client/CLAUDE.md` for
project-wide context.

## Files here

- `CameraRig.cs` — plain `Node`, singleton (`CameraRig.Instance`). **The one source of camera
  truth**: owns `Yaw`/`PitchDegrees`/`Distance` and all camera input (keyboard yaw, zoom, arcball
  drag, cursor-relative snapping). Lives in the main scene tree, deliberately *outside* the 3D
  `SubViewport` — a node inside a `SubViewportContainer`'s `SubViewport` never receives forwarded
  mouse-motion events, which is why an earlier version of the arcball control (previously wired
  inside `WorldRenderer3D`) silently did nothing.
- `WorldRenderer3D.cs` — `Node3D`; root script of `Scenes/world_3d.tscn`, inside the 3D
  `SubViewport`. Singleton (`WorldRenderer3D.Instance`). Every frame, reads `CameraRig.Instance` and
  writes the `PhantomCamera3D`'s rotation (`SetThirdPersonRotationDegrees`) and `SpringLength`. Holds
  no camera state or input of its own. Exposes `SetCameraFollowTarget(Node3D)` /
  `ClearCameraFollowTarget()`, used by `LocalPlayer` to point the 3D camera at its `CharacterModel3D`
  (`client/Scripts/Players/CharacterModel3D.cs`) — the *only* 3D-rendered entity; remote players and
  enemies stay 2D-only.
- `CameraController2D.cs` — the 2D counterpart, in the main scene tree (not the SubViewport). Every
  frame, reads `CameraRig.Instance` and writes `RotationOffset`/`Zoom` onto the registered
  `PhantomCamera2D`. Also holds no camera state or input of its own — it does **not** receive
  anything from `WorldRenderer3D` directly; both presenters read independently from `CameraRig`.
- `HexGridOverlay2D.cs` / `HexGridOverlay3D.cs` — debug grid overlays.

`WorldRenderer3D` and `CameraController2D` are deliberately separate scripts, both kept as thin
per-frame readers of `CameraRig`. Do not merge them, and do not wire one to read from the other —
`CameraRig` is the only allowed source of truth for camera state.

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
- `CameraController2D.UnregisterCamera()` exists but nothing calls it (not even `LocalPlayer._ExitTree`,
  which only clears the 3D follow target). In practice this doesn't visibly break anything today — the
  registered `PhantomCamera2D` is a child of the `LocalPlayer` node and gets freed along with it, and
  `GameManager.ShowLobby()` reclaims camera priority for the main-menu camera independently on death —
  but `CameraController2D._localPlayerPCam2D` is left dangling on a freed node between death and the
  next `RegisterCamera` call. Worth fixing opportunistically rather than assuming it's load-bearing.
