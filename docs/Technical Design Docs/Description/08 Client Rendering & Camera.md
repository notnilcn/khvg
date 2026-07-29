# 08 Client Rendering & Camera

## Assumed knowledge

[[01 Architecture & Sync Model|01]] — this doc assumes the entity/connection boot order (`GameManager` as `Main`, static facade, `Connected` signal) without re-explaining it. [[02 Entity & Component Framework|02]] — every component named below (`CameraRigComponent`, `Camera2DPresenterComponent`, `World3DComponent`) follows the same self-registering `Component`/`Node3DComponent` base and `GetSibling<T>()`/`GetComponent<T>()` lookup pattern that doc introduces; this doc doesn't re-derive it. [[04 Player System|04]] — `LocalPlayer._Ready`/`_PhysicsProcess`/`_ExitTree` are `04`'s scene-lifecycle territory (`join-3`, `move-4`-`move-6`); this doc picks up only the camera- and 3D-model-specific lines inside those same methods.

## The 30-second version

Every camera decision in the game funnels through one component, `CameraRigComponent`: it owns yaw, pitch, and zoom-distance as three plain numbers, reads all camera input (rotate keys, click-drag arcball, cursor-relative snap gestures, scroll-to-zoom), and never touches a camera node directly. Two presenters read that shared state every frame and each drives their own half of the picture — `Camera2DPresenterComponent` writes a `PhantomCamera2D`'s rotation/zoom for the 2D gameplay view, `World3DComponent` writes a `PhantomCamera3D`'s spring-arm rotation/length for a 3D backdrop rendered into an offscreen `SubViewport` and composited behind the 2D scene. Neither presenter decides what's actually on screen, though: the phantom_camera addon's own `PhantomCameraHost` nodes continuously pick whichever registered camera has the highest `Priority`, so switching from the lobby's camera to the local player's, or back again on death, is nothing more than this project flipping a `Priority` int on two plain data nodes and letting the addon's own arbitration do the rest. A `CharacterModel3D` wrapper mirrors the local player's 2D position/rotation into the 3D backdrop every physics tick, purely as a visual — no gameplay state lives in the 3D scene, and no other player or enemy has a 3D counterpart at all.

## System flowchart

```sync
![[99 End-to-End Timeline Flowchart#^camera-1{seamless:true,title:false,marker:01.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-2{seamless:true,title:false,marker:02.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-3{seamless:true,title:false,marker:03.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-4{seamless:true,title:false,marker:04.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-5{seamless:true,title:false,marker:05.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-6{seamless:true,title:false,marker:06.}]]
```

## Main body

### One rig, because input has to live in exactly one place

`CameraRigComponent` (`camera-1`) is deliberately the *only* place in the codebase that reads camera-related `Input` calls. The comment at the top of the file states why it sits directly under `Main` rather than inside the 3D `SubViewport` alongside `World3DComponent`: a node inside a `SubViewportContainer`'s `SubViewport` only receives the input events the container explicitly forwards to it, and mouse-motion is not among them — the file's own comment notes this is exactly the bug that made an earlier arcball implementation, wired inside `World3DComponent`, silently do nothing. Centralizing input in one component outside the viewport sidesteps that entirely: `CameraRigComponent` computes `Yaw`/`PitchDegrees`/`Distance` from keys, drags, and scroll, and every other camera-facing system — `Camera2DPresenterComponent`, `World3DComponent`, and `LocalPlayer._PhysicsProcess`'s own movement-rotation math (`camera-5`) — only ever *reads* those three numbers, never re-derives them from raw input itself.

The rig exposes one derived value beyond the three raw ones: `Zoom2D = ZoomReferenceDistance / Distance`, an inverse relationship because Godot's 2D `Zoom` is "smaller number = more zoomed out" while the 3D spring arm's `Distance` is "bigger number = farther away" — the same physical zoom gesture (scroll wheel) has to produce numbers that move in opposite directions for the two presenters, and this one property is where that inversion happens, once, rather than in each presenter separately.

Two of the rig's `[Export]` tunables are overridden at the scene-instance level rather than left at their C# defaults — `main.tscn`'s `CameraRigComponent` node sets `PitchMax = 0.0`, not the field's own default of `-10f` — so the actual in-game pitch range is `[-90°, 0°]` (straight-down to level-with-the-horizon), wider than the code alone would suggest. This is a case worth remembering generally: an `[Export]` field's C# default is only the *fallback* value, not necessarily what any given `.tscn` instance actually uses.

### Two presenters, one sign convention stated once

`Camera2DPresenterComponent` (`camera-2`) and `World3DComponent` (`camera-3`) are kept as separate classes on purpose — both files carry an explicit comment warning not to merge them — even though they do structurally the same thing (read the rig, write a phantom camera). The reason they can't simply share code is that a `PhantomCamera2D`'s `RotationOffset` and a `PhantomCamera3D`'s Y-axis rotation disagree about which direction is positive: Godot's 2D space is y-down, so `RotationOffset` increases clockwise, while a 3D rotation about `+Y` increases counter-clockwise when viewed from above. `World3DComponent._Process` negates the rig's `Yaw` for exactly this reason (`-Mathf.RadToDeg(_rig.Yaw)`) — a sign flip that's stated once, in that one line's comment, and nowhere else; `LocalPlayer`'s own 2D→3D position sync (`camera-5`) repeats the identical flip for the same reason.

`World3DComponent` also owns the follow-target wiring (`SetCameraFollowTarget`/`ClearCameraFollowTarget`) rather than `CameraRigComponent` or `LocalPlayer` directly, since the `PhantomCamera3D.FollowTarget` property only exists on the 3D presenter's own pcam — `LocalPlayer._Ready`/`_ExitTree` call through it rather than reaching into the pcam node themselves.

### Priority arbitration: how "which camera is active" is decided

Neither presenter, nor any code in this project, ever assigns to `MainCamera2D` (the actual `Camera2D` node driving the visible frame) directly. Instead, three `PhantomCamera2D` nodes exist at any given time — `LocalPlayerPhantomCamera2D` (a child of `local_player.tscn`, `camera-4`), `MainMenuPhantomCamera2D` (a child of `Main`, used for the lobby and death screens), and nothing else competing for the same host — and a `PhantomCameraHost` node (a plain addon child of `MainCamera2D`) continuously reads every pcam's `Priority` and morphs `MainCamera2D`'s own transform to match whichever pcam currently has the highest value. This project's entire camera-switching logic, in both directions, is nothing more than setting `.Priority` on one of these two plain data nodes: `LobbyComponent.ShowLobby()`/`HideLobby()` toggle `MainMenuPhantomCamera2D.Priority` between 60 and 0 ([[03 Lobby and profile creation|03]]'s `lobby-1` for the first use, `camera-6` for the death/leave case), and `Camera2DPresenterComponent.RegisterCamera`/`UnregisterCamera` do the same for whichever local player's pcam is currently registered.

The 3D side runs an entirely separate instance of the same addon mechanism — its own `PhantomCameraHost`, under `world_3d.tscn`'s `Camera3D` — and the only thing that keeps the two host setups from cross-matching each other's cameras is `host_layers`: the 3D pcam and its host both set `host_layers = 2` in `world_3d.tscn`, while the 2D system's nodes are all left at the addon's own default, layer 1. A `PhantomCameraHost` only ever considers pcams sharing at least one host-layer bit with it, so the 2D and 3D arbitration never see each other's cameras despite running concurrently against the same rig's numbers.

### Camera-relative movement

[[04 Player System|04]]'s `move-4` names this doc as the place "camera-relative input" is explained: `LocalPlayer._PhysicsProcess` takes raw WASD input (`Input.GetVector("Left", "Right", "Up", "Down")`) and rotates it by `cameraRig.Yaw` before turning it into `Velocity` — so "forward" always means "wherever the camera currently faces," not a fixed world axis, and rotating the camera with `Camera_Left`/`Camera_Right` or an arcball drag changes which way W actually moves the player without the player having to re-orient anything themselves. The same physics tick calls `_model3D?.SyncFrom2D(GlobalPosition, Rotation)` (`camera-5`), which is the only place outside `World3DComponent` that repeats the clockwise/counter-clockwise sign flip from the previous section — converting the `CharacterBody2D`'s 2D `(x, y)` position and `Rotation` into the 3D model's `(x, 0, y)` position and negated Y-rotation, every single tick, so the knight model in the backdrop never visibly lags the 2D sprite it's shadowing.

### The 3D backdrop: what's actually built, and what actually renders

`world_3d.tscn` — a `DirectionalLight3D`, a `WorldEnvironment` (a flat procedural sky), the debug `HexGridOverlay3DComponent` ([[03 World & Hex Grid|03]]'s territory, not re-covered here), and the `World3DComponent`/`PhantomCamera3D`/`Camera3D` trio described above — is instanced once, inside `main.tscn`'s `3D/SubViewportContainer/SubViewport`. `LocalPlayer._Ready` parents a `CharacterModel3D` (wrapping the exported `KnightScene`, `Assets/3D/characters/Knight.fbx`) directly under it the moment a local player spawns, and no other entity — no `RemotePlayer`, no `Enemy` — ever gets one; the 3D scene exists purely to give the local player's own character a 3D presence behind the 2D game, not to mirror the multiplayer world in 3D.

Whether any of this is currently visible during ordinary play is a separate question from whether it runs — see Known gaps below. The presenter logic (`camera-3`, `camera-5`) executes every frame regardless, updating a `PhantomCamera3D` and a `CharacterModel3D` that both genuinely exist in the tree; what happens to that work downstream, once it reaches the `SubViewport`'s texture, depends on scene-level flags this doc's code-read turned up as likely still mid-development.

## Known gaps / stubs

- **The 3D backdrop may not currently render at all.** `main.tscn`'s `"3D"` `CanvasLayer` — the parent of the `SubViewportContainer` holding `world_3d.tscn` — is set `visible = false`, and its child `SubViewport` is configured `render_target_update_mode = 0` (Godot's `UPDATE_DISABLED`, meaning the viewport never redraws its texture, not even once). Nothing in the C# code sets either flag back — no toggle, no key bind, no code path anywhere references the `"3D"` node or its `SubViewport` at all. The whole presenter chain this doc documents (`World3DComponent`, `CharacterModel3D`, `PhantomCamera3D`) is fully wired and runs its per-frame update logic regardless, but as configured today the backdrop it's driving appears to be invisible and non-updating in ordinary play — this reads as an in-progress feature (the git history's "added 3d" commit) left switched off at the scene level rather than a finished, visible backdrop.
- **`World3DComponent.DefaultModelScale` is declared but never read.** It's an `[Export] float`, defaulting to `50f`, with no getter/consumer anywhere in the codebase. `CharacterModel3D`'s own constructor has an independent `scale = 50f` default parameter, and `LocalPlayer._Ready` calls that constructor without passing a scale at all — so the model's actual scale comes from `CharacterModel3D`'s own hardcoded default, not from `World3DComponent`'s exported field, even though both happen to currently agree on `50f`. Changing the exported field in the editor would silently do nothing.
- **`local_player.tscn` has a dead `Camera2D` child node** named `Camera` (`visible = false`, `zoom = (0.75, 0.75)`), left over from before the `PhantomCamera2D`-based system replaced it. No script anywhere in the client fetches a node named `"Camera"`, so it's inert scene clutter, not a fallback or an alternate camera path.
- **The debug hex-grid overlays' own toggle gaps** (`HexGridOverlayComponent`'s dead `DrawHudText` call, neither 2D nor 3D overlay having an in-game visibility toggle) are [[03 World & Hex Grid|03]]'s "Known gaps" to own — they live in `Components/Camera/` alongside this doc's subject matter but were read and documented in that earlier session.

## Where to go next

[[09 Admin, Debug & World Lifecycle]] covers `DebugOverlay`, the other always-present screen-space UI this doc's `MainCamera2D`/`PhantomCameraHost` setup renders underneath — and the admin/content tooling that runs independently of anything this doc describes. [[07 Combat & Damage Math|07]] is worth re-reading alongside `camera-6` above: the same `OnLocalPlayerDelete` event that ends a combat death's row cleanup is what this doc's camera hand-off reacts to.
