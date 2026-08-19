# 11 Camera & Presentation

## Assumed knowledge

- [[02 The Component Framework]] — what a `*Component` is, how components register with their entity, and how a `TableBinderComponent` re-exposes server table rows as Godot signals.
- [[03 Boot & Connection]] — how `game.tscn` (the gameplay scene, root `game`) is reached and what the `GameManager` entity is.
- [[05 Joining the World]] — how the `LocalPlayer` entity is spawned and how the subscription waves deliver profile rows.
- [[06 Movement & Position Sync]] — `InterpolationComponent` and its `Moving` flag, which drive remote Walk/Idle animation here.
- [[07 Terrain & World Streaming]] — the `MapConfig` table (hex outer radius, chunk radius, world dimensions) and the server's hex math, which the overlays re-derive client-side.

## The 30-second version

One component owns the camera: [[client/Scripts/Components/Camera/CameraRigComponent.cs|CameraRigComponent]] keeps the canonical yaw, pitch, and zoom-distance and handles every camera input. Two *presenters* read that state every frame and each drive their own PhantomCamera — [[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs|Camera2DPresenterComponent]] for the 2D gameplay view, [[client/Scripts/Components/Camera/World3DComponent.cs|World3DComponent]] for an optional 3D backdrop rendered inside a `SubViewport`. Because neither presenter holds state, the two views can never disagree. Around that core sit the presentation mirrors: 3D character models and 3D enemy bullets that copy 2D transforms into the backdrop viewport, two debug hex-grid overlays that re-derive the server's hex math, and the row-driven sprite pipeline that turns server `texture_id`s into `SpriteFrames` for the local player, remote players, enemies, and drops. The camera even reaches into the simulation: the server derives enemy aggro/attack ranges from constants that encode the client's viewport height and zoom.

## Flowcharts

- [[flowcharts/main-camera.canvas]] — the composed flow for this system (composed from `flows.json`; may not exist until the next regeneration run).
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Camera_subfolder/Camera_subfolder.canvas]] — the whole `Scripts/Components/Camera/` folder: rig, both presenters, both hex-grid overlays.
- [[flowcharts/Subflowcharts/client_subfolder/Scripts_subfolder/Components_subfolder/Camera_subfolder/CameraRigComponent_codefile/CameraRigComponent_codefile.canvas]] — deep dive into the rig's input handling and canonical state.
- [[flowcharts/Subflowcharts/client_subfolder/Scenes_subfolder/world_3d_codefile/world_3d_codefile.canvas]] — the `world_3d.tscn` backdrop scene: 3D presenter, hex tiles, bullet mirror, PhantomCamera3D.

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

## The rig: one camera state, zero camera authority

`CameraRigComponent` (declared inline as a child of the `game.tscn` root, next to `DebugOverlay`) is a plain data-and-input component: three properties — `Yaw`, `PitchDegrees`, `Distance` — plus tuning `[Export]`s. It writes to no camera node anywhere. The presenters poll it in their own `_Process`, so the rig never needs to know which views exist; adding a third view would mean adding a third reader, not touching the rig. `Zoom2D` is *derived*, not stored: `ZoomReferenceDistance / Distance` ([[client/Scripts/Components/Camera/CameraRigComponent.cs#Zoom2D#1|Zoom2D]]), because Godot 2D zoom is inverse to distance — a larger zoom factor means a closer camera, while a 3D spring arm gets *longer* as you zoom out. Storing one `Distance` and deriving the 2D zoom from it keeps the two views in lockstep by construction.

The rig's placement is a hard-won invariant, stated in the file's own header comment: it lives in the **main scene tree, not inside the 3D `SubViewport`**, because a node inside a `SubViewport` only receives input the `SubViewportContainer` forwards — and mouse-motion events never arrive there. The old arcball control was wired inside `World3DComponent` and *silently did nothing* for exactly this reason. That is why all `_Input` handling ([[client/Scripts/Components/Camera/CameraRigComponent.cs#_Input#1|_Input]]) sits in the rig.

Input falls into five groups, all gated on `LocalPlayer.Local != null` (no camera control before you possess a body):

- **Held-key yaw** in `_Process`: `Camera_Left`/`Camera_Right` rotate `Yaw` at `YawSpeed` degrees per second.
- **Scroll zoom**: `Zoom_In`/`Zoom_Out` multiply or divide `Distance` by `ZoomStep` (1.1×, clamped to `DistanceMin`–`DistanceMax`), which simultaneously changes the 2D zoom and the 3D spring-arm length — one gesture, both views.
- **Resets**: `Reset_Camera_Yaw` / `Reset_Camera_Pitch` snap back to north / straight-down.
- **Arcball drag** (`Arcball_Camera_Controls` held): mouse motion adds yaw and clamps pitch between `PitchMin` and `PitchMax`. On press the cursor is *captured* (`Input.MouseModeEnum.Captured`) so it can't leave the window mid-drag, and on release `WarpMouse` puts the cursor back where the drag started — the standard "infinite drag" trick.
- **Cursor-relative snap rotates** — the bullet-hell affordance. `Camera_Snap_So_That_Cursor_Is_North` rotates the world so the cursor ends up straight above the player; the East/West variant puts it level left or right. The math measures the angle from the player's *canvas* position to the mouse (`GetGlobalTransformWithCanvas().Origin.AngleToPoint(mousePosition)`), adds `5π/2` (π/2 to convert "angle to point" into "rotation needed to make it north", plus a full 2π turn) and mods by 2π to get a positive minimal rotation. While held, mouse motion fine-tunes the yaw at `MouseRotateSensitivity` radians per pixel; on release the mouse is warped to the snap-computed screen position (`center + (0, -_snapDist)` for north), so the cursor lands exactly where the camera rotation implies it should — aim is preserved across the snap. `_snapDist` is multiplied by `Zoom2D` because the warp happens in screen pixels while the measurement started in canvas space.

Every step above happens only when the local player exists; the rig is otherwise inert, which is why the menu works without any camera components at all (see the priority handoff below).

## The 2D presenter and the PhantomCamera priority handoff

The project uses the `phantom_camera` addon: instead of moving a `Camera2D` directly, you place `PhantomCamera2D` nodes that each describe a framing (follow target, zoom, rotation offset, tween), and a `PhantomCameraHost` child of the real camera blends toward whichever phantom has the highest `Priority`. The real `MainCamera2D` and its `PhantomCameraHost` live in `game.tscn` (lines ~130–140); no gameplay code ever touches them.

[[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs|Camera2DPresenterComponent]] holds no state of its own — its `_Process` copies `_rig.Yaw` into the local player's PhantomCamera2D `RotationOffset` and `_rig.Zoom2D` into its `Zoom` every frame. Which phantom it writes to is decided by registration: `LocalPlayer._Ready` grabs its `%LocalPlayerPhantomCamera2D` child and calls `RegisterCamera`, which sets that phantom's `Priority = 60` ([[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs#RegisterCamera#1|RegisterCamera]]). On death `_ExitTree` calls `UnregisterCamera`, which drops the priority to 0 — guarded by `IsInstanceValid`, because the phantom is a child of the dying `LocalPlayer` and may already be freed.

Authority is therefore a pure priority contest:

- While the lobby is up, `LobbyComponent.ShowLobby` sets the menu phantom (`MainMenuPhantomCamera2D` in `game.tscn`) to priority 60; `HideLobby` drops it to 0 ([[client/Scripts/Components/Lobby/LobbyComponent.cs#ShowLobby#1|ShowLobby]]). The menu phantom is declared with `priority = 1` in the scene, so even with no lobby logic it still wins over a default-priority phantom.
- At join, the player phantom jumps to 60 and takes over; on death it yields.

The player phantom in `local_player.tscn` (lines ~71–80) follows the `DamageReceivingComponent` node (not the entity root — the hurtbox *is* the canonical body position), has `rotate_with_target = true`, and uses a tween resource with `duration = 0.0`: camera rotation snaps instantly rather than lagging, which matters in a dodge game where a rotated view must never trail the dodge. That phantom supersedes a **disabled legacy `Camera2D`** child (`visible = false`, zoom 0.75) still present in the scene — kept, not deleted (see Known gaps).

`LocalPlayer._PhysicsProcess` also *reads* the rig: movement input is rotated by `cameraRig.Yaw` before being applied ([[client/Scripts/Players/Local/LocalPlayer.cs#_PhysicsProcess#1|_PhysicsProcess]]), and the body `Rotation` is set to the yaw while moving — so "up" on the keyboard always means "up on screen" no matter how the camera is turned, and the character sprite faces along the view. The rig is thus not just a camera; it is the frame of reference for player control.

## The 3D backdrop pipeline

The 3D view is pure decoration layered *under* the 2D game: `game.tscn` declares a `3D` `CanvasLayer` (behind the `UI` layer) containing a full-rect `SubViewportContainer` (`stretch = true`, `stretch_shrink = 3`) and a `SubViewport` with `transparent_bg = true` — the 2D world shows through wherever the 3D scene draws nothing. Two viewport properties carry the design constraints already mentioned: `handle_input_locally = false` (input stays in the main tree — the reason the rig can live outside) and `render_target_update_mode = 0` (always update — the backdrop animates every frame even though it's a separate render target).

Inside the viewport sits an instance of `world_3d.tscn`: a directional light, a procedural-sky `WorldEnvironment`, the `HexGridOverlay3DComponent` tile field, the `EnemyBullets3DComponent` bullet mirror, and a `PhantomCamera3D` (follow mode 6 = third person, spring arm) with its own host on the viewport's `Camera3D`. [[client/Scripts/Components/Camera/World3DComponent.cs|World3DComponent]] — the scene's root and the 3D presenter — registers with the `GameManager` entity through the component framework's SubViewport ancestor walk, so `GetSibling<CameraRigComponent>()` finds the rig even across the viewport boundary. Its `_Process` writes the rig's pitch and *negated* yaw into the phantom's third-person rotation and the rig's `Distance` into `SpringLength`. The negation is stated once in the code and nowhere else: 2D rotation is clockwise in Godot's y-down space, while 3D yaw about +Y is counter-clockwise, so every 2D→3D rotation copy in the project flips the sign.

Entities opt into the backdrop with [[client/Scripts/Players/CharacterModel3D.cs|CharacterModel3D]] — a plain C# `IDisposable`, *not* a node or component: it instantiates a `PackedScene` model under the `World3DComponent`, and its `SyncFrom2D(pos, yaw)` copies the 2D global position to `(x, 0, y)` and the negated yaw each frame. `LocalPlayer` creates one from its exported `KnightScene` in `_Ready` and — crucially — passes the model to `world3D.SetCameraFollowTarget`, which is what makes the 3D PhantomCamera3D track the player (`ClearCameraFollowTarget` on exit calls the addon's `erase_follow_target` through untyped `Call`, because the phantom camera is a GDScript addon node with no C# type). `Enemy._Ready` does the same with its `SkeletonScene` ([[client/Scripts/Players/Enemies/Enemy.cs#_Ready#1|Enemy._Ready]]) and syncs in `_Process`, disposing in `_ExitTree`. `World3DComponent.DefaultModelScale` (50) matches `CharacterModel3D`'s default scale argument; callers currently rely on the default.

**Verified against the scene:** the `3D` CanvasLayer is declared `visible = false` in `game.tscn` (line ~106). All of this machinery — model mirroring, tile field, bullet mirror, spring-arm camera — runs every frame, but the layer renders nothing in the shipped scene. Flip that one property to re-enable the backdrop.

## Enemy bullets, mirrored into 3D

Enemy bullets exist only in 2D: the BlastBullets2D factory (a GDExtension node) simulates and renders them into the 2D world, so without help the 3D viewport would show enemies firing nothing. [[client/Scripts/Components/Bullets/EnemyBullets3DComponent.cs|EnemyBullets3DComponent]] (a child in `world_3d.tscn`, `BulletScene` = the KayKit `hex_bullet.tscn`) rebuilds them in 3D each frame. Its `_Process` walks `BulletSpawnerComponent.LiveEnemyBullets` — the live `DirectionalBullets2D` instances — and for each one reads bullet transforms through untyped `Call("all_bullets_get_transforms")`, because the GDExtension exposes no C# API (the same calling convention `BulletSpawnerComponent`/`BulletControllerComponent` use).

Two details are load-bearing:

- **Per-index liveness.** The bulk `get_all_bullets_status` array is unreliable for multi-bullet instances — only index 0 ever reports true — so liveness goes through `Call("is_bullet_status_enabled", i)` per bullet ([[client/Scripts/Components/Bullets/EnemyBullets3DComponent.cs#SyncInstance#1|SyncInstance]]). The comment says `BulletControllerComponent` hits the same quirk; this is a factory bug the client works around in two places.
- **A flat, never-shrinking pool.** `NextNode()` hands out the first `_used` entries of a `List<Node3D>`, growing on demand; `HideUnused()` hides the tail. Bullet counts spike and settle every fight, so the pool trades a bounded memory overhead for zero allocation in the steady state. Each pooled node is placed at `(x, BulletHeight, y)` with the sign-flipped yaw — `BulletHeight` (20) hovers bullets above the tile plane so they read against the ground.

## Debug overlays and the tripled hex math

Two overlays visualize the server's hex coordinate system, both fed by `MapConfig` rows through a child `TableBinderComponent` with `ReplayExistingRows = true` (a cached config row still arrives via the insert path, so no manual re-query is needed):

- [[client/Scripts/Components/Camera/HexGridOverlayComponent.cs|HexGridOverlayComponent]] (2D, child of the `game.tscn` root) redraws whenever the camera position or zoom changes: `_Draw` converts the viewport rect to world bounds, then to a hex range via `WorldToHex`, and draws each hex — outline, plus per-hex labels for hex coords, chunk coords, and the chunk's spiral index. Chunks are 3-colored by `(cq − cr) mod 3`, which provably gives adjacent chunks different colors; out-of-bounds chunks (beyond `ChunkCols`/`ChunkRows`) get a flat grey. Its signals are wired inline in `game.tscn` (lines ~154–161, 212–213), where the node is also declared `visible = false` — hidden, but still subscribed (see Known gaps).
- [[client/Scripts/Components/Camera/HexGridOverlay3DComponent.cs|HexGridOverlay3DComponent]] (3D, in `world_3d.tscn`) instances KayKit hex tile scenes in a ring of `ViewRadiusHexes` (12) around the player, refreshing only when the player has moved at least one hex (`DistanceSquaredTo` vs `_outerRadius²` — the hysteresis avoids rebuilding the ring every frame). Its 3-coloring uses a *different* formula, `(2cq + cr) mod 3`, selecting among the exported purple/pink/grey tile scenes; each tile optionally gets a code-created `Label3D` with hex/chunk/spiral coords.

Both overlays re-derive the server's hex geometry — `HexToWorld`, `WorldToHex` (cube-coordinate rounding), `ToLowerRes` (a port of the `hexx` crate's `Hex::to_lower_res`), and `HexSpiralIndex` (the analytic Z²→ℕ spiral bijection) — because Godot can't call into the SpacetimeDB module. That is why `HexSpiralIndex` exists three times: the server's [[server/spacetimedb/src/world/hex.rs#spiral_chunk_index#1|spiral_chunk_index]] and both overlay components. Any change to the chunk-indexing scheme must land in all three or the overlays will label chunks with indices the server disagrees with. (The terrain system is the third consumer of this math — see [[07 Terrain & World Streaming]].)

## The camera's reach into the simulation

The coupling in camera-6 deserves emphasis because it violates the usual "presentation can't affect gameplay" intuition — deliberately. [[server/spacetimedb/src/main/global.rs#CAMERA_VIEW_RADIUS#1|main/global.rs]] defines:

```
CAMERA_VIEW_RADIUS = (CLIENT_VIEWPORT_HEIGHT_PX / CLIENT_CAMERA_ZOOM) / 2
                   = (360 / 0.75) / 2 = 240 px
```

— the vertical half-extent of what the client can see at reference zoom. The enemy tick reducers then compute activation and attack ranges as `template.move_sim_factor * CAMERA_VIEW_RADIUS` and `template.attack_sim_factor * CAMERA_VIEW_RADIUS` ([[server/spacetimedb/src/enemy/reducers.rs#tick_enemy_behavior#1|tick_enemy_behavior]]), with seeded factors like 1.5/0.85 up to 2.75/1.35. So an enemy wakes up and starts approaching from off-screen, and opens fire at roughly the moment it becomes visible — the seeded factors encode "how far beyond the edge of the screen" each enemy type starts acting. The invariant, stated in the constants' own comment, runs the other way too: `SIMULATION_CHUNK_RINGS` must cover the largest sim-factor range or simulation silently stops for that enemy. Changing the client's zoom reference changes server behavior; treat `CLIENT_CAMERA_ZOOM` as a gameplay constant, not a cosmetic one.

## Row-driven character presentation

Every sprite in the game is chosen by a server row, resolved through the same path: the row carries a `texture_id`, [[client/Scripts/Game/GameManager.cs#GetResPath#1|GameManager.GetResPath]] forwards it to `CatalogComponent.GetResPath`, which looks the id up in the seeded `TextureEntry` catalog and returns a `res://` path; the caller `GD.Load`s the `SpriteFrames`.

- **Local player:** [[client/Scripts/Components/Visual/LocalPlayerProfileComponent.cs|LocalPlayerProfileComponent]] (in `local_player.tscn`) receives `LocalPlayerActiveProfile` rows from its binder and applies `profile.TextureId` to the entity's `AnimatedSprite2D` on insert. Updates are deliberately notification-only — it raises `AimSettingsChanged` on the entity so observers like `CombatComponent` stay wired to the entity, but does not re-apply the texture, matching the pre-refactor behavior.
- **Remote players:** [[client/Scripts/Components/Visual/RemoteVisualComponent.cs|RemoteVisualComponent]] is itself an `AnimatedSprite2D` (via the `VisualComponent` base — the component scene's root *is* the sprite). `SetTexture` resolves the profile texture id the same way; its `_Process` plays `Walk` or `Idle` from the sibling `InterpolationComponent.Moving` flag — animation state comes from the movement system, not from any server row.
- **Enemies and drops** follow the same catalog path from their own rows (template texture, item texture) — covered in [[08 Enemies & AI]] and [[10 Inventory, Items & Enchantments]].

Walk/Idle for the *local* sprite is driven directly in `LocalPlayer._PhysicsProcess` from the input vector — the only presentation path that doesn't wait for a server round-trip, because local input feedback can't afford one.

## Known gaps / stubs

- **Leftover debug print:** `Camera2DPresenterComponent.RegisterCamera` still `GD.Print`s the registered phantom's offset and zoom on every join ([[client/Scripts/Components/Camera/Camera2DPresenterComponent.cs#RegisterCamera#1|RegisterCamera]]).
- **Hidden but subscribed:** `HexGridOverlayComponent` in `game.tscn` is `visible = false` yet its `MapConfigBinder` still subscribes and its `_Process`/`QueueRedraw` still run against camera movement — known waste; the overlay is toggled only by editing the scene.
- **Disabled legacy camera:** `local_player.tscn` keeps a `visible = false` legacy `Camera2D` child (zoom 0.75) superseded by the PhantomCamera2D setup — retained deliberately, but a drift hazard if anyone re-enables it.
- **3D backdrop off in the shipped scene:** the `3D` CanvasLayer in `game.tscn` is `visible = false` (line ~106) — all 3D machinery (models, tiles, bullet mirror, spring arm) runs without rendering. Verified against the scene file; an earlier note elsewhere in the repo claims the opposite — the scene file wins.
- **Dead overlay code:** `HexGridOverlayComponent.DrawHudText` is fully written but its call in `_Draw` is commented out, and the `SeamLineColor`/`SeamLineWidth` fields are declared but never used (a world-wrap seam visualization that was never hooked up). `World3DComponent.DefaultModelScale` is exported but unread — callers rely on `CharacterModel3D`'s own default.
- **Duplicate component scenes (drift hazard):** `camera_rig_component.tscn`, `camera_2d_presenter_component.tscn`, and `hex_grid_overlay_component.tscn` under `Scenes/Components/` are unreferenced duplicates — the live wiring is inline in `game.tscn` as documented above. Don't cite or edit them as if they were the live setup.

## Where to go next

The debug-overlay side of presentation continues in [[12 Admin & Debug]] (the inline `DebugOverlay` in `game.tscn`). For the systems this one mirrors, read [[06 Movement & Position Sync]] (where `InterpolationComponent.Moving` comes from) and [[07 Terrain & World Streaming]] (the server original of the hex math the overlays re-derive).
