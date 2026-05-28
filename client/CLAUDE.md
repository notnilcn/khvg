# 2D → 2.5D Migration: SubViewport 3D Layer

## Why this approach

BlastBullets2D has no 3D equivalent. The full-3D migration (`client3d/`) was abandoned for this reason.

**Strategy:** Keep every 2D system intact (CharacterBody2D physics, Camera2D, Area2D collision, BlastBullets2D bullets, SpacetimeDB reducers). Add a full-screen 3D SubViewport that renders character models and the hex tile grid on top of the existing 2D scene.

Reference codebase: `../client3d/` — contains working 3D versions of all scripts. Use it for adapting logic, but **do not copy blindly**; several things differ (see gotchas below).

---

## Architecture

### Scene layering (bottom to top in main.tscn)

```
Main (Node2D, GameManager.cs)
├── TileMap                          ← 2D ground texture, stays unchanged
├── SubViewportContainer             ← stretches full-screen, transparent
│   └── SubViewport (transparent_bg=true, own_world_3d=false)
│       └── World3D (world_3d.tscn) ← Node3D root, World3D.cs singleton
│           ├── DirectionalLight3D
│           ├── WorldEnvironment
│           ├── HexGridOverlay (Node3D, 3D tile spawning)
│           ├── Camera3D
│           │   └── PhantomCameraHost
│           ├── PhantomCamera3D (top-down, follows CameraTarget)
│           └── CameraTarget (Node3D, position synced to LocalPlayer each frame)
├── HexGridOverlay (OLD 2D — remove after Task 6)
└── BulletManager (added dynamically, always last → renders on top of SubViewport)
```

Entity nodes (LocalPlayer, Enemy, NonLocalPlayer) are added dynamically as children of the scene root. Their `AnimatedSprite2D` nodes are hidden; 3D models live inside the SubViewport.

### Coordinate mapping

2D `GlobalPosition(x, y)` → 3D `new Vector3(x, 0, y)`.  
The 3D world is a flat Y=0 plane; 2D X maps to 3D X, 2D Y maps to 3D Z.  
This is the **only** coordinate convention — never deviate from it.

### Model scale

FBX models (~1.7 m tall) at scale 1 are ~1.7 Godot units = invisible at 2D game scale.  
Use `World3D.DefaultModelScale = 50f`: a Knight becomes ~85 units tall, readable at game scale.  
Tweak this constant after first visual check.

### Key files being created

| File | Purpose |
|------|---------|
| `Scripts/Game/World3D.cs` | Node3D singleton; spawns/despawns 3D model nodes; owns the Camera3D |
| `Scripts/Characters/CharacterModel3D.cs` | Plain C# class (not a Node); wraps a Node3D in the SubViewport |
| `Scripts/Game/CameraController.cs` | Reads camera input (orbit, zoom, yaw, top-down toggle) |
| `Scenes/world_3d.tscn` | SubViewport contents; root node has World3D.cs attached |
| `Scripts/World/pixel_art.gdshader` | Post-process color quantization (copy from client3d) |

### Gotchas vs client3d

- **`MapConfig` not `WorldConfig`**: the server table is `MapConfig` in this project. `HexGridOverlay` must subscribe to `conn.Db.MapConfig`, not `WorldConfig`.
- **No `VelX/VelY`**: the server's `PlayerPosition` here does not carry velocity fields. `NonLocalPlayer` stays with the simpler lerp (no extrapolation).
- **`phantom_camera` not installed**: copy `client3d/addons/phantom_camera/` to `client/addons/phantom_camera/` and enable the plugin in **Project → Project Settings → Plugins**.
- **No colored hex tile .tscn files yet**: `hex_purple.tscn`, `hex_pink.tscn`, `hex_grey.tscn` exist in `client3d` but not here. Copy them from `client3d/Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/`.
- **2D player's `GlobalPosition` is `Vector2`**: wherever client3d code reads `player.GlobalPosition.Z`, this project must read `player.GlobalPosition.Y`.

---

## Task 1 — SubViewport skeleton + World3D singleton

**Goal:** a 3D sky renders behind the 2D scene; game still boots normally.

### Steps

**1a. Copy phantom_camera addon**

Copy the entire directory:
```
client3d/addons/phantom_camera/  →  client/addons/phantom_camera/
```
Then open the Godot editor → **Project → Project Settings → Plugins → phantom_camera → Enable**.  
Verify `PhantomCameraManager` appears in the Autoloads list.

**1b. Copy hex tile scenes**

Copy these three files from `client3d/Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/` to the same path in `client/`:
- `hex_grey.tscn`
- `hex_pink.tscn`
- `hex_purple.tscn`

They reference `hex_grass.fbx` which already exists in `client/`.

**1c. Create `Scripts/Game/World3D.cs`**

```csharp
#nullable enable
using Godot;

public partial class World3D : Node3D
{
    public static World3D? Instance { get; private set; }

    public const float DefaultModelScale = 50f;

    // The root 3D node that entity models are added to.
    public Node3D Scene3D => this;

    private Node3D? _cameraTarget;
    private LocalPlayer? _followTarget;

    public override void _Ready()
    {
        Instance = this;
        _cameraTarget = GetNode<Node3D>("CameraTarget");
    }

    public override void _Process(double _delta)
    {
        if (_followTarget != null && _cameraTarget != null)
        {
            var p = _followTarget.GlobalPosition;
            _cameraTarget.GlobalPosition = new Vector3(p.X, 0f, p.Y);
        }
    }

    public void SetCameraFollowTarget(LocalPlayer player) => _followTarget = player;
    public void ClearCameraFollowTarget() => _followTarget = null;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
```

**1d. Create `Scenes/world_3d.tscn`** in the Godot editor:

1. Create a new scene, root node type **Node3D**, rename to `World3D`, attach `Scripts/Game/World3D.cs`.
2. Add children:
   - **DirectionalLight3D** — enable shadows, rotate to a readable angle
   - **WorldEnvironment** — create a new Environment resource, SkyMaterial = ProceduralSkyMaterial
   - **CameraTarget** (Node3D) — position (0, 0, 0); this node must be named exactly `CameraTarget`
   - **Camera3D** — add child **PhantomCameraHost** (Node, from phantom_camera)
   - **PhantomCamera3D** — set `follow_mode = 6` (Third Person), `follow_target = CameraTarget`, `spring_length = 300`, third_person_rotation_degrees = (-80, 0, 0)
3. Save as `Scenes/world_3d.tscn`.

**1e. Wire SubViewportContainer in `main.tscn`**

The scene already has a SubViewportContainer node. Configure it:
- **SubViewportContainer**: Anchor = Full Rect, `stretch = true`, `mouse_filter = MOUSE_FILTER_IGNORE`
- **SubViewport** (child): `size = Vector2i(1280, 720)`, `transparent_bg = true`, `own_world_3d = false`
- Add an instance of `world_3d.tscn` as a child of the SubViewport.
- Move SubViewportContainer to be **above TileMap** in the scene tree (TileMap first, SubViewportContainer second).

### Validation

Press F5. Join the world. The SubViewport should render the 3D sky/environment overlaid on the 2D scene. The game should connect and spawn the player normally. The 2D Camera2D still drives the 2D scene. Nothing else changes.

---

## Task 2 — Camera follows the 2D player

**Goal:** when you move the player, the 3D camera pans to follow.

### Steps

**2a. Update `LocalPlayer.cs`** — tell World3D to follow this player:

Add to `_Ready()`:
```csharp
World3D.Instance?.SetCameraFollowTarget(this);
```

Add to `_ExitTree()`:
```csharp
World3D.Instance?.ClearCameraFollowTarget();
```

### Validation

Connect, join world, move with WASD. The 3D scene (sky, horizon) pans as you move. The 2D Camera2D still follows the player sprite independently.

---

## Task 3 — Camera controls (arcball, zoom, top-down toggle)

**Goal:** right-click drag orbits camera; scroll zooms; Q/E rotate yaw; Space toggles top-down.

### Steps

**3a. Create `Scripts/Game/CameraController.cs`**

```csharp
#nullable enable
using Godot;
using PhantomCamera;

public partial class CameraController : Node
{
    private PhantomCamera3D? _cam;

    private float _zoom      = 300f;
    private const float ZoomMin   = 50f;
    private const float ZoomMax   = 1200f;
    private const float ZoomSpeed = 3f;
    private const float MouseSens = 0.3f;
    private const float PitchMin  = -89f;
    private const float PitchMax  = -10f;
    private const float YawSpeed  = 90f;

    private bool _isTopDown = true;
    private float _savedYaw = 0f;

    public override void _Ready()
    {
        var pcamNode = GetParent().GetNode<Node3D>("PhantomCamera3D");
        _cam = pcamNode.AsPhantomCamera3D();
        if (_cam != null)
            _cam.SetThirdPersonRotationDegrees(new Vector3(-80f, 0f, 0f));
    }

    public override void _Input(InputEvent e)
    {
        if (_cam == null) return;

        // Scroll zoom
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _zoom = Mathf.Clamp(_zoom - 20f, ZoomMin, ZoomMax);
            if (mb.ButtonIndex == MouseButton.WheelDown)
                _zoom = Mathf.Clamp(_zoom + 20f, ZoomMin, ZoomMax);
        }

        // Arcball orbit (right-click drag)
        if (Input.IsMouseButtonPressed(MouseButton.Right) && e is InputEventMouseMotion mm)
        {
            _isTopDown = false;
            var rot = _cam.GetThirdPersonRotationDegrees();
            rot.X -= mm.Relative.Y * MouseSens;
            rot.X  = Mathf.Clamp(rot.X, PitchMin, PitchMax);
            rot.Y -= mm.Relative.X * MouseSens;
            rot.Y  = Mathf.Wrap(rot.Y, 0f, 360f);
            _cam.SetThirdPersonRotationDegrees(rot);
        }

        // Space → top-down
        if (e is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Space)
        {
            _isTopDown = true;
            var rot = _cam.GetThirdPersonRotationDegrees();
            rot.X = -80f;
            _cam.SetThirdPersonRotationDegrees(rot);
        }
    }

    public override void _Process(double delta)
    {
        if (_cam == null) return;

        // Smooth zoom
        var next = Mathf.Lerp(_cam.SpringLength, _zoom, ZoomSpeed * (float)delta);
        _cam.SpringLength   = next;
        _cam.FollowDistance = next;

        // Q/E yaw (only when not dragging)
        if (!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            float yaw = 0f;
            if (Input.IsKeyPressed(Key.Q)) yaw += 1f;
            if (Input.IsKeyPressed(Key.E)) yaw -= 1f;
            if (yaw != 0f)
            {
                var rot = _cam.GetThirdPersonRotationDegrees();
                rot.Y = Mathf.Wrap(rot.Y + yaw * YawSpeed * (float)delta, 0f, 360f);
                _cam.SetThirdPersonRotationDegrees(rot);
            }
        }
    }
}
```

**3b. Add CameraController to world_3d.tscn**

Open `world_3d.tscn` in the editor. Add a child node of type **Node**, rename to `CameraController`, attach `Scripts/Game/CameraController.cs`.  
The controller's `_Ready()` fetches `PhantomCamera3D` as a sibling via `GetParent().GetNode<Node3D>("PhantomCamera3D")` — the PhantomCamera3D node must be named exactly `PhantomCamera3D` in the scene tree.

### Validation

Connect and join world. Right-click and drag → camera orbits. Scroll → zooms. Q/E → yaw rotates. Space → snaps back to top-down. The 2D player sprite still follows the 2D Camera2D.

---

## Task 4 — LocalPlayer 3D model

**Goal:** Knight.fbx model appears at the player's position, rotates with mouse aim.

### Steps

**4a. Create `Scripts/Characters/CharacterModel3D.cs`**

```csharp
#nullable enable
using Godot;
using System;

public class CharacterModel3D : IDisposable
{
    private readonly Node3D _node;
    private bool _disposed;

    public CharacterModel3D(PackedScene scene, Vector2 pos2D, Node3D parent, float scale = World3D.DefaultModelScale)
    {
        _node = scene.Instantiate<Node3D>();
        _node.GlobalPosition = new Vector3(pos2D.X, 0f, pos2D.Y);
        _node.Scale = Vector3.One * scale;
        parent.AddChild(_node);
    }

    public void SyncFrom2D(Vector2 pos, float yaw2D)
    {
        if (_disposed) return;
        _node.GlobalPosition = new Vector3(pos.X, 0f, pos.Y);
        // 2D rotation is clockwise from screen-up; 3D Y-rotation is counter-clockwise from +Z.
        // Negate to match visual direction.
        _node.Rotation = new Vector3(0f, -yaw2D, 0f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (GodotObject.IsInstanceValid(_node))
            _node.QueueFree();
    }
}
```

**4b. Update `LocalPlayer.cs`**

Add exports and fields at the top of the class:
```csharp
[Export] public PackedScene? KnightScene { get; set; }
private CharacterModel3D? _model;
```

In `_Ready()`, after `_sprite = GetNode...`:
```csharp
_sprite.Visible = false;
if (KnightScene != null && World3D.Instance != null)
    _model = new CharacterModel3D(KnightScene, GlobalPosition, World3D.Instance.Scene3D);
World3D.Instance?.SetCameraFollowTarget(this);
```

In `_PhysicsProcess()`, after `MoveAndSlide()`:
```csharp
_model?.SyncFrom2D(GlobalPosition, Rotation);
```

In `_ExitTree()`:
```csharp
_model?.Dispose();
World3D.Instance?.ClearCameraFollowTarget();
```

**4c. Assign KnightScene in the editor**

Open `Scenes/local_player.tscn`. With the root node selected, set `Knight Scene = res://Assets/characters/Knight.fbx` in the Inspector.

### Validation

Connect, join world. The `AnimatedSprite2D` is invisible. A Knight model should appear at the player's position and rotate to face the mouse cursor. Move with WASD — model follows. The 2D physics (collision, bullet detection) still works normally.

**Adjustment knobs after first run:**
- Model too small/large: change `World3D.DefaultModelScale` (currently 50)
- Model faces wrong direction: add a `ForwardOffset` param to `SyncFrom2D` and rotate by `+ Mathf.Pi` or `+ Mathf.Pi / 2`
- Camera too close/far: change the PhantomCamera3D `spring_length` in world_3d.tscn (currently 300)

---

## Task 5 — Enemy and NonLocalPlayer 3D models

**Goal:** Enemies show Skeleton_Warrior.fbx; remote players show Knight.fbx.

### Steps

**5a. Update `Enemy.cs`**

Add exports and fields:
```csharp
[Export] public PackedScene? EnemyModelScene { get; set; }
private CharacterModel3D? _model;
```

In `_Ready()`, after the foreach:
```csharp
if (EnemyModelScene != null && World3D.Instance != null)
    _model = new CharacterModel3D(EnemyModelScene, GlobalPosition, World3D.Instance.Scene3D);
```

In `_Process()`, after `GlobalPosition = GlobalPosition.Lerp(...)`:
```csharp
_model?.SyncFrom2D(GlobalPosition, Rotation);
```

In `_ExitTree()`:
```csharp
_model?.Dispose();
```

Open `Scenes/default_enemy.tscn`, set `Enemy Model Scene = res://Assets/skeletons/Skeleton_Warrior.fbx`.

**5b. Update `NonLocalPlayer.cs`**

Same pattern:
```csharp
[Export] public PackedScene? PlayerModelScene { get; set; }
private CharacterModel3D? _model;
```

`_Ready()`:
```csharp
if (PlayerModelScene != null && World3D.Instance != null)
    _model = new CharacterModel3D(PlayerModelScene, GlobalPosition, World3D.Instance.Scene3D);
```

`_Process()`, after lerp:
```csharp
_model?.SyncFrom2D(GlobalPosition, _targetRotation);
```

`_ExitTree()`:
```csharp
_model?.Dispose();
```

Open `Scenes/non_local_player.tscn`, set `Player Model Scene = res://Assets/characters/Knight.fbx`.

### Validation

Use admin commands to spawn an enemy (`spacetime call bullethell spawn_enemy ...`). A Skeleton_Warrior model should appear. Connect a second client — the remote player should show a Knight model.

---

## Task 6 — HexGridOverlay 3D

**Goal:** Hex tiles render in 3D inside the SubViewport. Remove the old 2D overlay.

### Steps

**6a. Replace `Scripts/Game/HexGridOverlay.cs`**

Replace the entire file with the version from `client3d/Scripts/World/HexGridOverlay.cs`, then apply these two diffs:

**Diff 1 — use MapConfig instead of WorldConfig:**
```csharp
// Replace:
conn.Db.WorldConfig.OnInsert += (_, row)    => OnWorldConfig(row);
conn.Db.WorldConfig.OnUpdate += (_, _, row) => OnWorldConfig(row);
var cfg = conn.Db.WorldConfig.Id.Find(0u);
if (cfg != null) OnWorldConfig(cfg);

// With:
conn.Db.MapConfig.OnInsert += (_, row)    => OnMapConfig(row);
conn.Db.MapConfig.OnUpdate += (_, _, row) => OnMapConfig(row);
var cfg = conn.Db.MapConfig.Id.Find(0u);
if (cfg != null) OnMapConfig(cfg);
```

Rename the handler method from `OnWorldConfig(WorldConfig cfg)` to `OnMapConfig(MapConfig cfg)`.  
The fields `cfg.ChunkHexRadius` and `cfg.HexOuterRadius` have the same names in MapConfig — no further changes needed.

**Diff 2 — 2D player's GlobalPosition is Vector2:**
```csharp
// Replace (in _Process):
var pos = player.GlobalPosition;   // was Vector3 in client3d
if (pos.DistanceSquaredTo(_lastRefreshPos) > _outerRadius * _outerRadius)
{
    _lastRefreshPos = pos;
    RefreshTiles(pos);
}

// With:
var p2 = player.GlobalPosition;    // Vector2 from CharacterBody2D
var pos = new Vector3(p2.X, 0f, p2.Y);
if (pos.DistanceSquaredTo(_lastRefreshPos) > _outerRadius * _outerRadius)
{
    _lastRefreshPos = pos;
    RefreshTiles(pos);
}
```

**6b. Move HexGridOverlay into world_3d.tscn**

1. Open `world_3d.tscn`. Add a child **Node3D**, rename to `HexGridOverlay`, attach `Scripts/Game/HexGridOverlay.cs`.
2. In the Inspector, set:
   - `Purple Tile Scene = res://Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_purple.tscn`
   - `Pink Tile Scene = res://Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_pink.tscn`
   - `Grey Tile Scene = res://Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_grey.tscn`
   - `Tile Scale Multiplier = 1.0` (adjust if tiles appear too large/small)
   - `View Radius Hexes = 12`
   - `Show Tile Labels = true` (turn off once visuals look right)
3. Open `Scenes/main.tscn`. Remove the old `HexGridOverlay` (Node2D) from the scene tree.

### Validation

Run the game, connect, join world. Hex tiles should appear in 3D around the player. Moving the player should cause tiles to dynamically load/unload. Labels show hex coordinates and spiral chunk indices. Compare with the 2D debug overlay output from before — the coordinates should match.

---

## Task 7 — Pixel art post-process shader

**Goal:** color quantization gives the game a crisp retro look.

### Steps

**7a. Copy shader from client3d**

Copy `client3d/Scripts/World/pixel_art.gdshader` → `client/Scripts/World/pixel_art.gdshader`.

**7b. Add post-process MeshInstance2D to main.tscn**

1. Open `Scenes/main.tscn`. Add a **MeshInstance2D** child, rename to `PostProcess`.
2. In the Inspector:
   - `Mesh = new QuadMesh` — set Width=2 Height=2 (covers full NDC space)
   - `Material = new CanvasItemMaterial`
3. On the CanvasItemMaterial, set `Shader = res://Scripts/World/pixel_art.gdshader`.
4. Move `PostProcess` to be the **last** child in the scene tree (renders on top of everything).

### Validation

The screen should render with visible color banding (8 levels per channel). Pixel edges on the Knight model and tiles should appear sharp. Adjust the `pixel_size` uniform in the shader or the target resolution constants if the effect looks wrong at your display resolution.

---

## Completion checklist

- [ ] Task 1: SubViewport 3D sky renders; game still starts/connects normally
- [ ] Task 2: 3D camera pans when WASD moves the player
- [ ] Task 3: Right-click orbits, scroll zooms, Q/E yaw, Space top-down
- [ ] Task 4: Knight model at player position; sprite hidden; model rotates with mouse
- [ ] Task 5: Skeleton_Warrior at enemy positions; Knight for remote players
- [ ] Task 6: 3D hex tiles visible; old 2D HexGridOverlay removed; coordinates match server
- [ ] Task 7: Pixel art quantization shader applied

## Notes for future work

- **Animation**: FBX models have an `AnimationPlayer` child. After Task 4, check the node's AnimationPlayer for the exact animation clip names (often `"Idle"`, `"Walk"`). Add `PlayAnimation(string name)` to CharacterModel3D and call it from entity scripts.
- **AoI re-subscription**: still unimplemented (known issue in main CLAUDE.md).
- **ReportHit reducer**: still unimplemented.
- **LootDrop subscription**: still unimplemented.
- **Drop.cs**: currently `Area2D`, still works in 2D; no 3D model needed until loot visuals are prioritized.
