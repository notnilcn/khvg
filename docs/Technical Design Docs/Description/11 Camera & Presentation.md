# 11 Camera & Presentation

## Assumed knowledge

- [[04 Lobby & Profiles]] — lobby-2/lobby-7's phantom-camera priority flip (menu camera at 60 while the lobby is open, 0 once hidden); this doc is the other half of that mechanism.
- [[05 Joining the World]] — join-4's `LocalPlayer._Ready` (the spawn moment this doc's registration and 3D-model setup hang off) and join-3's game wave, which delivers the `MapConfig` row both hex overlays consume.
- [[06 Movement & Position Sync]] — `LocalPlayer._PhysicsProcess` (WASD and body rotation; here only its camera-yaw coupling matters) and move-6's `RemoteVisualComponent` Walk/Idle coverage.
- [[07 Terrain & World Streaming]] — the `MapConfig` row's origin, and the server hex math (`spiral_chunk_index`, `to_lower_res`) that both overlays port to C#.
- [[08 Enemies & AI]] — enemy-4/enemy-5's sim ranges, the consumers of this doc's `CAMERA_VIEW_RADIUS`.
- [[02 The Component Framework]] — entity/component registration (including across the SubViewport ancestor walk) and the nine unreferenced duplicate component scenes.
- [[03 Boot & Connection]] — boot-1's `PhantomCameraManager` autoload, which makes the phantom-camera addon tick.
- [[01 Roadmap]] — purpose, audience, and the linking conventions used throughout.
- [[00 End-to-End Timeline Flowchart]] — the runtime spine; this doc's steps are the `camera` section.
- Maintainer references for orientation (not restated here): [[CLAUDE.md]] at the repo root, plus the `CLAUDE.md` files in `client/` and `server/`.

## The 30-second version

The camera is **one shared rig with two presenters**, switched by phantom-camera priority. All camera input lands in `CameraRigComponent` (a component of the `Main` entity), which owns the only copy of yaw, pitch, and zoom-distance; it owns no camera. The 2D presenter (`Camera2DPresenterComponent`) copies yaw/zoom each frame into the local player's `PhantomCamera2D`, and the phantom-camera host on the real `MainCamera2D` tracks whichever phantom camera has the highest priority — the lobby's menu camera and the player's camera swap control purely by flipping priorities between 60 and 0. The 3D backdrop is a separate world: a transparent `SubViewport` inside a full-screen `CanvasLayer` renders `world_3d.tscn` *over* the 2D world, containing a third-person `PhantomCamera3D` driven by the same rig (yaw negated) and a scale-50 3D knight model of the local player — and only the local player — synced from the 2D body every physics frame. Two hex overlays draw the chunk grid from the shared `MapConfig` row: the 2D one is hidden-but-subscribed, the 3D one doubles as the visible ground. The server never sees any of this; it just hard-codes the assumed view size (`CAMERA_VIEW_RADIUS` = 240 world units from a 360 px viewport at 0.75 zoom) to size enemy simulation ranges.

## Flowcharts

- [[flowcharts/main-camera.canvas]] — the composed camera flow (the five `Camera` components and their scene wiring, `world_3d.tscn`, the player scripts, and the server `main` module holding the camera constants).
![[flowcharts/main-camera.canvas]]
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Camera_subfolder/Camera_subfolder.canvas]] — deep dive: the five camera components (`CameraRigComponent`, `Camera2DPresenterComponent`, `World3DComponent`, `HexGridOverlayComponent`, `HexGridOverlay3DComponent`).
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/world_3d_codefile/world_3d_codefile.canvas]] — deep dive: `world_3d.tscn`, the 3D backdrop scene.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Players_subfolder/CharacterModel3D_codefile/CharacterModel3D_codefile.canvas]] — deep dive: `CharacterModel3D`, the 2D→3D model bridge.

## System flowchart

```sync
![[00 End-to-End Timeline Flowchart#^camera-1{seamless:true,title:false,marker:01.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-2{seamless:true,title:false,marker:02.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-3{seamless:true,title:false,marker:03.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-4{seamless:true,title:false,marker:04.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-5{seamless:true,title:false,marker:05.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-6{seamless:true,title:false,marker:06.}]]
```

```sync
![[00 End-to-End Timeline Flowchart#^camera-7{seamless:true,title:false,marker:07.}]]
```

## Main body

### One rig, two presenters

```sync
![[00 End-to-End Timeline Flowchart#^camera-1{seamless:true,title:false,marker:01.}]]
```

The split is a deliberate invariant, stated twice in the code: `CameraRigComponent.cs`'s header says both presenters read the rig and "neither owns state and neither writes back", and `World3DComponent.cs` opens with "Do NOT try to combine World3DComponent with Camera2DPresenterComponent." The reason is that the two presenters write to *different kinds of camera with different conventions* — the 2D one wants a `RotationOffset` in clockwise y-down radians and a `Vector2` zoom, the 3D one wants spring-arm Euler angles in degrees and a length — so any merged class would be two code paths behind one interface anyway. Keeping the state in a third, camera-less component means the 2D and 3D views can never drift apart: there is exactly one `Yaw` in the process.

The rig's placement is load-bearing. It registers with the `GameManager` entity in the main scene tree ([[02 The Component Framework]]'s ancestor walk), and the header comment explains why it can't live next to the 3D presenter: a node inside a `SubViewport` only receives input the `SubViewportContainer` forwards, and mouse motion never arrives — the old arcball, wired inside `World3DComponent`, "silently did nothing". `World3DComponent` itself *can* live in the SubViewport because it only reads state (the ancestor walk crosses the viewport boundary); it just can't listen.

### Camera input: yaw, zoom, arcball, cursor snaps

```sync
![[00 End-to-End Timeline Flowchart#^camera-2{seamless:true,title:false,marker:02.}]]
```

Two Godot input idioms are worth unpacking. **Mouse capture** (`Input.MouseModeEnum.Captured`) hides the cursor and keeps delivering relative motion even at screen edges — that's what makes drag-to-rotate viable — and on release the rig warps the cursor back to where the drag started, so the mouse doesn't "travel" while you orbit. The **cursor snaps** are the unusual feature: instead of dragging the world under a fixed cursor, one keypress computes the player→cursor angle in canvas space (`GetGlobalTransformWithCanvas().Origin.AngleToPoint(mouse)`), adds it to yaw so that direction becomes screen-north (or east/west), and warps the cursor to the matching screen edge at the same distance — the view rotates to meet the cursor, and the cursor's on-screen meaning is preserved. The `+ 5π/2 mod 2π` in the angle math converts `AngleToPoint`'s signed angle (0 = east) into an unsigned angle measured from north. Holding the snap key keeps a slower manual rotation active (`MouseRotateSensitivity`, rad/px) until release.

Because movement input is rotated by the same yaw (camera-2's last beat), rotating the camera never changes what "press W" does relative to the screen — it only changes which world direction that is. The body `Rotation = yaw` assignment doubles as the sprite's facing and the 3D model's heading (camera-5), so all three stay locked together.

### The 2D presenter and the priority handoff

```sync
![[00 End-to-End Timeline Flowchart#^camera-3{seamless:true,title:false,marker:03.}]]
```

A **phantom camera** (the PhantomCamera addon, kept ticking by boot-1's `PhantomCameraManager` autoload) is a virtual camera: it holds position/rotation/zoom intent but renders nothing itself. A `PhantomCameraHost` node attached to a real camera continuously copies the state of the highest-`Priority` phantom camera in its layer set, tweening over the pcam's `tween_resource` duration. The upshot is that *which camera is live* is just an integer comparison, and this game drives every camera transition with it: the menu pcam sits at 60 while the lobby is open (lobby-2) and drops to 0 on join (lobby-7), while the player's pcam goes 0 → 60 in `RegisterCamera` — both tweens have duration 0, so the handoff is a hard cut, not a pan.

The player pcam being a child of `local_player.tscn` (rather than `main.tscn`) is what makes respawn/leave work without any camera bookkeeping in the spawner: the camera is born with the player, registered in `_Ready`, and dies with it. The two `IsInstanceValid` guards in `UnregisterCamera`/`_Process` exist because C# wrappers outlive freed Godot nodes — on death the pcam can be gone before the presenter runs again, and touching it would throw `ObjectDisposedException`.

One harmless dead switch: the pcam sets `rotate_with_target = true`, which would add the follow target's rotation on top of `RotationOffset` — but the follow target is the `DamageReceivingComponent` child, whose *local* rotation never changes (`LocalPlayer` rotates its own body, not the child), so the flag contributes 0 and every degree of camera rotation comes from the presenter's `RotationOffset` write.

### The 3D backdrop: a second world composited on top

```sync
![[00 End-to-End Timeline Flowchart#^camera-4{seamless:true,title:false,marker:04.}]]
```

The 2D/3D mixing is standard Godot machinery, three nodes deep. A `SubViewport` is an off-screen render target with its own scene tree; a `SubViewportContainer` displays that texture as a UI element (here stretched full-rect, `stretch_shrink = 3`, so the 384×216 render is upscaled to the window); and a `CanvasLayer` draws UI-space content on its own layer above the world canvas — which is what puts the 3D render *on top of* the 2D world rather than behind it. With `transparent_bg = true` the viewport clears to transparent, so only actual 3D geometry (the knight, the hex tiles) occludes the 2D scene below. `handle_input_locally = false` is why camera-1's rig can't live in there: the viewport simply doesn't process input.

Inside, `world_3d.tscn` is a complete miniature 3D scene — light, sky environment, camera — whose `PhantomCamera3D` runs `follow_mode` 6 (third person: an internal `SpringArm3D` positioned at the follow target, rotated and extended by the presenter). The two phantom cameras never compete because of **host layers**: the 2D host listens on the default layer, the 3D pcam and host are on layer 2, and each host only considers pcams whose layers intersect its own. The negated yaw (camera-4) is the entire 2D↔3D convention bridge, and the code's claim that the sign convention "is stated once, here, and nowhere else" is accurate — `CharacterModel3D.SyncFrom2D` repeats the same negation for the model with its own local comment (camera-5).

### The local player's 3D model

```sync
![[00 End-to-End Timeline Flowchart#^camera-5{seamless:true,title:false,marker:05.}]]
```

`CharacterModel3D` is a handle class rather than a component because the thing it manages can't participate in the component framework: it lives in the SubViewport's tree, parented under `World3DComponent`, while its owner (`LocalPlayer`) lives in the main tree. So `LocalPlayer` news it up, pumps it from `_PhysicsProcess`, and `Dispose()`s it from `_ExitTree` — manual lifetime management exactly where the framework can't reach. The model's *only* inputs are position and yaw; animation, scale, and facing never change after construction. Note the 3D model is a pure visual echo: gameplay (collision, hit detection, position sync) all happens on the 2D `CharacterBody2D`, and the 2D `AnimatedSprite2D` stays visible under the 3D render — the model just draws over it.

### Hex grid overlays: one hidden, one doubling as the ground

```sync
![[00 End-to-End Timeline Flowchart#^camera-6{seamless:true,title:false,marker:06.}]]
```

Both overlays exist to make the server's hex addressing visible — every terrain tile, AOI query, and spawn region is keyed by the chunk coordinates these grids draw — and both re-implement the server's hex math in C# (`HexToWorld`/`WorldToHex`/`ToLowerRes`/`HexSpiralIndex`), so they drift in lockstep risk with [[07 Terrain & World Streaming]]'s stale-`hex_grid_overlay.gd`-comment gap. Their `MapConfigBinder` children are how they learn the grid dimensions: the same single `MapConfig` row (join-3) feeds terrain-7's `TerrainComponent`, the 2D overlay, and the 3D overlay, three binders over one table.

The 3D overlay is doing double duty: labeled "debug overlay" in its docstring, it is in fact the only ground the 3D backdrop has — the tinted KayKit tiles under the knight *are* its output, and its `ShowTileLabels` export defaults to true, so the hex/chunk coordinate `Label3D`s are part of the shipped view. Tiles refresh only when the player has moved more than one hex-width since the last refresh (`_lastRefreshPos` hysteresis in `_Process`), so walking within a hex costs nothing.

### What the server assumes about the camera

```sync
![[00 End-to-End Timeline Flowchart#^camera-7{seamless:true,title:false,marker:07.}]]
```

The constants exist so enemy *template data* can be resolution-independent: a template says "start simulating at 3× the view radius" rather than "at 720 units", so retuning the client camera would in principle only require updating these two numbers. The chain to keep straight is `CLIENT_VIEWPORT_HEIGHT_PX / CLIENT_CAMERA_ZOOM / 2` → `CAMERA_VIEW_RADIUS` → `move/attack_sim_factor ×` that → gated by a `SIMULATION_CHUNK_RINGS` chunk pre-filter (`has_player_in_simulation_range`, enemy-4) that must cover the largest product. The mismatch between the assumed 0.75 zoom and the live rig is in Known gaps.

### Sprite presentation for players

The 2D sprite side of presentation is small because earlier docs own most of it. The local player's sprite is dressed by [[LocalPlayerProfileComponent.cs##public partial class LocalPlayerProfileComponent : Component|LocalPlayerProfileComponent]]: its `LocalPlayerActiveProfileBinder` child (wired in [[local_player.tscn##[node name="LocalPlayerActiveProfileBinder" type="Node" parent="LocalPlayerProfileComponent"]|local_player.tscn]], replay on) feeds [[LocalPlayerProfileComponent.cs##private void OnProfileRowInserted()|OnProfileRowInserted]] the active `PlayerProfile` row, which resolves the profile's `TextureId` through equip-1's catalog (`GameManager.GetResPath`) and swaps the `AnimatedSprite2D`'s `SpriteFrames`; Walk/Idle selection is move-doc territory (`LocalPlayer._PhysicsProcess` plays by input). Profile *updates* deliberately don't re-apply the texture — a comment in `OnProfileRowUpdated` says the pre-refactor code didn't either. Remote players do the same thing once at spawn through `RemoteVisualComponent.SetTexture` (move-6) — and, unlike the local player, they get no 3D counterpart at all.

## Known gaps / stubs

- **Leftover debug print.** [[Camera2DPresenterComponent.cs##public void RegisterCamera(Node2D pcamNode)|RegisterCamera]] ends with a `GD.Print` of the registered pcam's offset and zoom — fires once per join/respawn.
- **The 2D hex overlay is hidden but fully live.** The [[main.tscn##[node name="HexGridOverlayComponent" type="Node2D" parent="."]|HexGridOverlayComponent]] in `main.tscn` is `visible = false`, which suppresses only drawing: its `MapConfigBinder` is still wired and replaying (so `OnMapConfigRow` still runs) and its `_Process` still polls the camera every frame. It's a debug tool left off, not dead code — flip `visible` in the editor to use it. (Its docstring still cites `hex_grid_overlay_component.tscn` as the wiring site — one of the nine unreferenced duplicate component scenes; the live wiring is inline in `main.tscn`, see [[02 The Component Framework]] → Known gaps. The `camera_rig_component.tscn` and `camera_2d_presenter_component.tscn` scenes are duplicates of the same kind.)
- **Disabled legacy camera.** The [[local_player.tscn##[node name="Camera" type="Camera2D" parent="."]|`Camera` (Camera2D) child]] in `local_player.tscn` is `visible = false`, superseded by the phantom-camera setup — but its `zoom = 0.75` is still the value the server's camera constants encode (next item), so it isn't quite dead weight.
- **Server camera constants are stale relative to the live rig.** [[server/spacetimedb/src/main/global.rs##pub const CLIENT_CAMERA_ZOOM|CLIENT_CAMERA_ZOOM]] = 0.75 matches the disabled legacy camera, while the rig's defaults map to zoom 1.0 and the player can zoom from 6× down to 0.25× at will. `CAMERA_VIEW_RADIUS` (240) is therefore a fixed guess that routinely misestimates the real visible area — consequential only for *when* enemies start/stop simulating (enemy-4/enemy-5), since the AOI subscription radii use their own chunk constants.
- **Dead export.** `World3DComponent.DefaultModelScale` (50) is never read — the 3D model's scale 50 comes from `CharacterModel3D`'s constructor default, and `LocalPlayer` doesn't override it. Two sources of the same magic number.
- **Debug labels ship visible.** `HexGridOverlay3DComponent.ShowTileLabels` defaults to true and `world_3d.tscn` doesn't override it, so every 3D ground tile carries a floating hex/chunk/spiral-index `Label3D` in normal play.

## Where to go next

The debug tooling that sits beside this system — the `DebugOverlay` canvas, the admin reducers (including the world-regeneration reducers that rewrite the `MapConfig` these overlays read) — is [[12 Admin & Debug]]. What happens to the camera registration and the 3D model when the player leaves or dies — the `_ExitTree` unwinding this doc introduced — is [[13 Disconnect & Teardown]].
