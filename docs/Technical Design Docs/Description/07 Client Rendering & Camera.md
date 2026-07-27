# 07 Client Rendering & Camera

## Assumed knowledge

- [[00 Roadmap]] for how these docs are structured and the conventions used below.
- [[01 Architecture & Sync Model]] for the tables/reducers/views vocabulary — nothing camera-specific lives on the server; everything in this doc is client-only code reacting to state other docs already cover.
- [[02 World & Hex Grid]]'s "Client rendering" section for `TerrainManager` (2D terrain/decor batching, fully covered there and not repeated here) and its "Known gaps and stubs" finding on `HexGridOverlay3D` — this doc cites that finding rather than re-deriving it, since `HexGridOverlay3D` and `WorldRenderer3D` are different classes living in the same 3D scene.
- [[03 Player System]] for `LocalPlayer`'s 2D movement/physics (`_PhysicsProcess`, `MoveAndSlide`) and `RemotePlayer`'s interpolation — both entirely 2D; this doc only adds the 3D/camera layer sitting on top of `LocalPlayer` specifically.
- [[05 Enemy AI & Bullet Patterns]] for `Enemy.cs`'s 2D interpolation and the BlastBullets2D bullet layer — also entirely 2D, confirmed again from this doc's side below.
- [[06 Combat & Damage Math]] for the `Area2D`-based hit detection this doc's central claim rests on: 2D collision is the only thing that has ever been shown to be authoritative anywhere in this codebase.

## The 30-second version

Every gameplay-relevant thing in this game — position, physics, collision, hit detection — lives in 2D, exactly as the preceding six docs describe. 3D exists, but only as a cosmetic overlay: `WorldRenderer3D` runs a `Node3D` scene (`world_3d.tscn`) composited into a transparent, reduced-resolution `SubViewport` alongside the 2D `Main` scene, and the *only* thing it ever renders that represents live game state is the local player's own `CharacterModel3D` — a `Knight.fbx` model whose transform is overwritten every physics frame from the 2D `CharacterBody2D`'s own `GlobalPosition`/`Rotation`. No enemy and no remote player gets one. A single `CameraRig` node — deliberately living outside the 3D `SubViewport` so it can still receive mouse input — owns yaw/pitch/zoom as the one source of camera truth; two independent, stateless presenters (`CameraController2D` for the 2D `PhantomCamera2D`, `WorldRenderer3D` for the 3D `PhantomCamera3D`) read it every frame and drive their own camera, and `LocalPlayer._PhysicsProcess` reads the same yaw a third time to rotate its own movement input. Beyond the local player's model, camera spring arm, sky, and lighting, `world_3d.tscn` renders nothing that reflects the actual world: `HexGridOverlay3D` — [[02 World & Hex Grid|02's]] finding, confirmed again here — populates fixed placeholder tile scenes rather than real terrain data. The 3D layer, in short, is a half-built parallel presentation track for one specific model, not a second renderer for the game.

## System flowchart

Ordering below is a scoped excerpt of [[99 End-to-End Timeline Flowchart]] — the "Camera and rendering, spectating all of it" fork of the parallel in-world loop.

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
```sync
![[99 End-to-End Timeline Flowchart#^camera-7{seamless:true,title:false,marker:07.}]]
```
```sync
![[99 End-to-End Timeline Flowchart#^camera-8{seamless:true,title:false,marker:08.}]]
```

## 2D-authoritative, 3D-cosmetic

Nothing in this doc changes the picture [[06 Combat & Damage Math]] already established: every position, every piece of physics, every hit-detection check anywhere in the client is 2D — `CharacterBody2D`/`Area2D` nodes, `MoveAndSlide`, the zone-based melee checks, BlastBullets2D's bullet collisions. The server has no concept of a third dimension at all; `PlayerPosition`, `Enemy`, `BulletPatternEvent` all carry a plain `(x, y)`. 3D, where it exists on the client, never feeds back into any of that — it only ever reads 2D state and redraws it. Concretely: `CharacterModel3D.SyncFrom2D` (step 7, ^camera-7) takes a `Vector2`/`float` the 2D layer already computed and turns it into a `Transform3D` for display; nothing calls a 3D method that changes gameplay state, and no 3D collision shape exists anywhere in the client's own scripts (BlastBullets2D's collision is exclusively `Area2D`-based, per [[05 Enemy AI & Bullet Patterns]] and [[06 Combat & Damage Math]]). If the 3D layer disappeared entirely tomorrow, nothing about movement, combat, or AOI would change — only what's on screen.

## `CameraRig`: the one source of camera truth

`CameraRig` (`client/Scripts/World/CameraRig.cs`) is a plain `Node`, not a 2D or 3D node — it's added directly to `main.tscn`'s own scene tree (next to `GameManager`), deliberately *outside* the `SubViewportContainer/SubViewport` that hosts the 3D scene. That placement isn't cosmetic: a node inside a `SubViewportContainer`'s `SubViewport` only receives input the container explicitly forwards, and mouse-motion events aren't forwarded that way — an earlier version of the arcball camera control lived inside `WorldRenderer3D` and silently did nothing, per that file's own comment. `CameraRig` owns three fields as the single source of truth — `Yaw` (radians, clockwise in Godot's y-down 2D convention), `PitchDegrees` (3D spring-arm pitch only, unused by the 2D side), and `Distance` (3D spring length, which also derives the 2D zoom via `Zoom2D => ZoomReferenceDistance / Distance`) — and all camera input: yaw hotkeys, scroll-wheel zoom, an arcball drag mode, and two "cursor-relative snap" modes that re-center the mouse and rotate yaw so a specific screen direction (north, or east/west) lines up with the cursor. Every one of `_Process`/`_Input`'s branches is gated on `LocalPlayer.Local != null`, so none of this does anything until a profile has actually joined the world — the lobby has no camera control at all. See [[CameraRig.cs##public override void _Input|_Input]].

## Two presenters, one rig: `CameraController2D` and `WorldRenderer3D`

Neither presenter holds camera state of its own, and neither reads from the other — both read only `CameraRig.Instance`, every frame, independently:

- `CameraController2D` (a `Node2D`, main scene tree) writes the registered `PhantomCamera2D`'s `RotationOffset` to `Yaw` directly and its `Zoom` to `(Zoom2D, Zoom2D)`.
- `WorldRenderer3D` (a `Node3D`, root of `world_3d.tscn`, running *inside* the 3D `SubViewport`) writes its `PhantomCamera3D`'s third-person spring-arm rotation to `(PitchDegrees, -Mathf.RadToDeg(Yaw), 0)` — the yaw is negated because a clockwise rotation in 2D's y-down space is a counter-clockwise rotation about 3D's +Y axis, a sign convention stated once in that file's own comment and nowhere else — and its `SpringLength` to `Distance`.

Both classes carry an explicit comment warning not to merge them or wire one to read the other; `client/Scripts/World/CLAUDE.md` states the reason plainly: `CameraRig` is the only allowed source of truth, and collapsing the two presenters would reintroduce exactly the "camera code inside a `SubViewport` can't see mouse input" bug the split was built to avoid. `LocalPlayer._PhysicsProcess` is a third, non-camera consumer of the same `Yaw` value — it rotates raw WASD input by it before turning that into `Velocity`, already covered in full by [[03 Player System]] — so one shared number drives the 2D camera, the 3D camera, and the player's own movement direction, all from a single field with a single writer. See [[CameraController2D.cs##public override void _Process|_Process]] and [[WorldRenderer3D.cs##public override void _Process|_Process]].

`PhantomCamera` (the `phantom_camera` addon) is the mechanism both presenters target rather than writing directly to a `Camera2D`/`Camera3D`: a scene can hold several `PhantomCamera2D`/`PhantomCamera3D` nodes, each with a `Priority`, and a `PhantomCameraHost` node (sitting on the *real* `Camera2D`/`Camera3D`) continuously matches whichever phantom camera currently has the highest priority. Nothing in this project's own code reads or writes further into the addon than `Priority`, `RotationOffset`/`Zoom` (2D), and `SetThirdPersonRotationDegrees`/`SpringLength`/`FollowTarget` (3D) — root `CLAUDE.md` scopes the addon's own internals as out of bounds unless actively debugging them, and this doc doesn't go further either.

## The main-menu-camera handoff

`GameManager` owns a `MainMenuPhantomCamera2D` reference (`_mainMenuPCam2D`) fetched once in `_Ready`. `ShowLobby` sets its `Priority` to 60 and un-hides the character-select UI; `HideLobby` sets it back to 0. `OnJoinPressed` calls `HideLobby` synchronously, the instant Join is clicked — before `join_world` is even sent to the server, let alone before a `LocalPlayer` node exists. The player's own `%LocalPlayerPhantomCamera2D` isn't registered until `LocalPlayer._Ready` runs (which only happens once the `LoggedInPlayer` row insert reaches `OnLocalPlayerInsert`), and `CameraController2D.RegisterCamera` sets *that* camera's `Priority` to 60 at that point — so for the gap between clicking Join and the player actually spawning, no phantom camera is bidding above 0 at all. Losing the world (death, via [[06 Combat & Damage Math]]'s death cascade, or a deliberate `leave_world`) runs the same handoff in reverse but asymmetrically: `OnLocalPlayerDelete` frees the whole `LocalPlayer` node — which takes the registered `PhantomCamera2D` down with it as a side effect of the node being freed, not through any unregister call — and then calls `ShowLobby`, which is what actually restores the lobby camera's priority. `CameraController2D.UnregisterCamera` exists and would do this more explicitly, but nothing calls it; `client/Scripts/World/CLAUDE.md` already flags this as dangling-but-harmless, since the freed node's own camera stops competing regardless. See [[GameManager.cs##public void ShowLobby|ShowLobby]] and [[GameManager.cs##private void OnJoinPressed|OnJoinPressed]].

## What `world_3d.tscn` and `WorldRenderer3D` actually render

`main.tscn` wraps a `CanvasLayer` ("3D") around a `SubViewportContainer` around a `SubViewport` (`transparent_bg = true`, `size = 213×120`, `stretch_shrink = 3`) that hosts one instance of `world_3d.tscn` — a much lower resolution than the game's actual viewport, then upscaled by the container, which is the mechanism behind the low-poly/pixel-art look the 3D layer has. This `SubViewport` exists in the tree from the moment `main.tscn` loads, regardless of login state; `WorldRenderer3D._Process` just early-returns every frame `LocalPlayer.Local` is null, so the 3D scene sits inert (rendering only its sky/lighting) until a player actually joins.

Inside `world_3d.tscn` itself: a `DirectionalLight3D` and a `WorldEnvironment` (a procedural sky) set the ambient look; a `PhantomCamera3D` (third-person `follow_mode`, configured only in the `.tscn` — the C# wrapper's `FollowMode` property is get-only) and a plain `Camera3D` carrying a `PhantomCameraHost` are the actual 3D viewport camera, matching the 2D side's `MainCamera2D`/`PhantomCameraHost` pairing in `main.tscn`; a full-screen quad (`MeshInstance3D`, `top_level = true`, attached to the `Camera3D`) carries a `ShaderMaterial` running `edge_detection.gdshader` — an outline/edge-detection post-process, the one piece of "make 3D look like the rest of the game" work actually present in the scene; and `HexGridOverlay3D` populates a hex ring of fixed placeholder tile scenes (`PurpleTileScene`/`PinkTileScene`/`GreyTileScene`, 3-colored by chunk parity) — [[02 World & Hex Grid|02's]] finding, confirmed again by reading the file directly for this doc: it never subscribes to `NearbyTerrainTiles`/`NearbyHexDecor`, so nothing about the actual biome/texture data `TerrainManager` draws in 2D is represented in 3D at all. Past the sky, light, camera rig, edge shader, and placeholder grid, the only thing `world_3d.tscn` ever renders that corresponds to real game state is the local player's own `CharacterModel3D` (next section). Taken together, this reads as a genuinely half-built parallel presentation track — a camera rig, a post-process shader, and a debug tile grid are all wired up and running, but the one piece that would make it a second renderer (real terrain) was never connected; it's not a finished feature dressed down, it's a scaffold with one real occupant.

## `CharacterModel3D`: mirroring the local player into 3D

`CharacterModel3D` (`client/Scripts/Players/CharacterModel3D.cs`) is a plain C# class implementing `IDisposable` — not a Godot node subclass, just a thin wrapper holding a reference to one. Its constructor takes a `PackedScene` (`LocalPlayer.KnightScene`, wired to `Knight.fbx` in `local_player.tscn`), instantiates it, scales it (`Vector3.One * 50f` by default), and parents it directly under whatever `Node3D` is passed in — always `WorldRenderer3D.Instance` in practice, so the model lives inside the 3D `SubViewport`'s scene, not under `LocalPlayer` itself in the 2D tree. `LocalPlayer._Ready` constructs one only if both `KnightScene` is assigned and `WorldRenderer3D.Instance` already exists, then calls `WorldRenderer3D.SetCameraFollowTarget(_model3D.Node)` so the 3D `PhantomCamera3D` tracks it.

`SyncFrom2D(Vector2 pos, float yaw2D)` is the only method that ever moves the model after construction, and it's called from `LocalPlayer._PhysicsProcess` every physics frame, right after `MoveAndSlide()`. It does nothing beyond a direct coordinate translation: `GlobalPosition = new Vector3(pos.X, 0f, pos.Y)` (2D's Y becomes 3D's Z; height is always 0) and `Rotation = new Vector3(0f, -yaw2D, 0f)` (the same clockwise-to-counter-clockwise negation the camera presenters use). There is no interpolation, no animation-state machine, no blending between poses — the 3D body's transform is recomputed from scratch every physics tick directly off whatever the `CharacterBody2D` already decided; it does not do anything beyond a direct coordinate mirror. On leaving the world, `LocalPlayer._ExitTree` disposes it (freeing the 3D node) and clears the camera's follow target. See [[CharacterModel3D.cs##public void SyncFrom2D|SyncFrom2D]].

One loose end worth flagging here: `WorldRenderer3D` exposes its own `[Export] public float DefaultModelScale = 50f`, but nothing in `LocalPlayer` ever reads it — `CharacterModel3D`'s constructor uses its own hardcoded `scale = 50f` default instead, since `LocalPlayer` never passes a `scale` argument. The two numbers happen to agree today only because they were both set to the same literal; changing one without the other would silently desync them.

## Why only the local player gets a 3D body

Confirmed directly for this doc, not assumed from the class names: neither `Enemy.cs` nor `RemotePlayer.cs` references `CharacterModel3D`, `WorldRenderer3D`, or `CameraRig` anywhere — a full-text search of both files turns up nothing 3D-related at all. Every enemy and every other player in the game, no matter how close to the camera, is rendered exactly as [[05 Enemy AI & Bullet Patterns]] and [[03 Player System]] describe: a 2D sprite lerping toward a server-authoritative position, nothing more. This is a genuine architectural asymmetry, not an oversight this doc is inferring — the hook point exists (`WorldRenderer3D.Instance` is a public singleton `RemotePlayer`/`Enemy` could reach from anywhere), it's simply never used outside `LocalPlayer`. Practically, this means the 3D layer only ever needs to composite *one* model at a time, and only for the client's own player — there's no 3D crowd, no 3D enemy horde, nothing that would need the 3D scene to scale past a single tracked object. Whether that's a deliberate scope decision (3D exists purely to give the controlling player a body to look at over their own shoulder, never meant to extend further) or simply how far the parallel 3D track has gotten before this doc was written isn't something the code states either way — treat it as an open question the "Known gaps and stubs" section below flags rather than a settled design fact.

## Known gaps and stubs

- **`world_3d.tscn` is a half-built parallel track, not a finished second renderer.** Sky, lighting, a full third-person camera rig, an edge-detection post-process shader, and a placeholder hex-tile debug grid are all wired up and running every frame — but the one piece that would make this a real second renderer of the game world (subscribing `HexGridOverlay3D`, or anything else, to `NearbyTerrainTiles`/`NearbyHexDecor`) was never connected. [[02 World & Hex Grid]] already found this for `HexGridOverlay3D` specifically; this doc confirms the rest of the scene has nothing else that reads real terrain data either.
- **Only the local player ever gets a 3D presenter.** `RemotePlayer.cs` and `Enemy.cs` have no 3D hook of any kind — confirmed by direct search, not inferred. Whether this is a deliberate final scope (3D is a first-person-adjacent flourish for the controlling player only) or an unfinished track that just hasn't reached other entities yet isn't stated anywhere in the code.
- **`WorldRenderer3D.DefaultModelScale` is unused.** `CharacterModel3D`'s own hardcoded `50f` default is what actually applies; the two numbers matching today is coincidence, not a wired connection.
- **`CameraController2D.UnregisterCamera` is dead code.** Nothing calls it; the registered `PhantomCamera2D` is freed as a side effect of `LocalPlayer` itself being freed on death/logout instead, and `GameManager.ShowLobby()` independently restores the lobby camera's priority. Already flagged in `client/Scripts/World/CLAUDE.md` as worth fixing opportunistically rather than urgent.
- **A brief camera-priority gap on join.** Between `HideLobby` (fired the instant Join is clicked) and `LocalPlayer._Ready` actually registering the in-world camera (which waits on the `LoggedInPlayer` row round-tripping through the server), no `PhantomCamera2D` is bidding above priority 0. Not confirmed to cause any visible glitch, but worth knowing about if one is ever reported.

## Where to go next

[[08 Admin, Debug & World Lifecycle]] is the next doc in reading order — the last system doc before [[99 End-to-End Timeline Flowchart]] is fully filled in and can be renumbered from its provisional letter-suffixed steps to plain integers.
