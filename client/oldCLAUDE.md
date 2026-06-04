# client/CLAUDE.md

This file provides guidance for Claude Code when working inside the Godot client (`client/`).

## Project context

See the root `CLAUDE.md` for full architecture, SpacetimeDB tables/reducers, and known issues.  
The client is 100% C# (Godot 4.6 mono). The scene is 2.5D: 2D physics on the main scene with a 3D SubViewport (World3D) overlaid for visuals.

---

## Task: 3D Bullet Visuals (hex_bullet.tscn)

**Goal:** Render bullets as glowing 3D `hex_bullet.tscn` instances inside `World3D`. Keep BlastBullets2D for future collision detection.

### Why two systems

BlastBullets2D uses GPU MultiMesh internally and does **not** expose per-bullet positions via a simple polling call on `BulletFactory2D`. However, `spawn_controllable_directional_bullets` returns the `DirectionalBullets2D` set handle, and `all_bullets_get_transforms()` on that handle gives all bullet transforms each frame. This lets the 3D visuals be **true mirrors** of the 2D bullet positions — not a parallel approximation.

### Key API facts (from BlastBullets2D source)

- `spawn_controllable_directional_bullets(data)` on `BulletFactory2D` → returns the `DirectionalBullets2D` set object
- `all_bullets_get_transforms()` on the set → `TypedArray<Transform2D>` for **all** slots (including inactive). Since all bullets in a set share `max_life_time` and expire simultaneously, all slots stay valid for the full lifetime.
- `life_time_over` signal requires `is_life_time_over_signal_enabled = true` to fire; this isn't trivially set at spawn time, so expiry is tracked with a client-side countdown matching `max_life_time = 8f`.
- The set handle is **pooled** (not freed on expiry), so `GodotObject.IsInstanceValid` always returns true — do not use it for expiry detection.
- `BulletManager` is added as the **last child** of the main scene, so its 2D sprites render on top of the SubViewport. Hide them: `_blastBullets.Set("visible", false)` in `_Ready()`.

---

### 1. `Scripts/Game/BulletManager.cs`

**Add exports and fields:**
```csharp
// Default scene used when no per-pattern override is set.
[Export] public PackedScene? BulletScene { get; set; }
[Export] public float BulletScale { get; set; } = 8f;

// Per-pattern overrides — leave null to fall back to BulletScene.
[Export] public PackedScene? StraightBulletScene { get; set; }
[Export] public PackedScene? RingBulletScene     { get; set; }
[Export] public PackedScene? SineBulletScene     { get; set; }
[Export] public PackedScene? BurstBulletScene    { get; set; }
[Export] public PackedScene? SpiralBulletScene   { get; set; }
[Export] public PackedScene? HomingBulletScene   { get; set; }

private sealed class BulletSet3D
{
    public required GodotObject Set;
    public required Node3D[] Visuals;
    public float RemainingLife;
}
private readonly List<BulletSet3D> _trackedSets = new();
```

**In `_Ready()`, after existing BlastBullets2D setup:**
```csharp
_blastBullets.Set("visible", false);
```

**Add helper `SpawnAndTrack`** — takes an optional per-pattern scene; falls back to `BulletScene`:
```csharp
private void SpawnAndTrack(int bulletCount, Vector2 origin, PackedScene? sceneOverride = null)
{
    var setObj = _blastBullets.Call("spawn_controllable_directional_bullets", _directionalData)
                              .AsGodotObject();
    var scene = sceneOverride ?? BulletScene;
    if (scene == null || World3d.Instance == null) return;

    var visuals = new Node3D[bulletCount];
    for (int i = 0; i < bulletCount; i++)
    {
        var v = scene.Instantiate<Node3D>();
        v.Scale    = Vector3.One * BulletScale;
        v.Position = new Vector3(origin.X, 0f, origin.Y);
        World3d.Instance.Scene3D.AddChild(v);
        visuals[i] = v;
    }
    _trackedSets.Add(new BulletSet3D { Set = setObj, Visuals = visuals, RemainingLife = 8f });
}
```

**Add `_Process()` override** — syncs 3D positions from BlastBullets2D each frame and expires sets:
```csharp
public override void _Process(double delta)
{
    for (int i = _trackedSets.Count - 1; i >= 0; i--)
    {
        var bs = _trackedSets[i];
        bs.RemainingLife -= (float)delta;

        if (bs.RemainingLife <= 0f)
        {
            foreach (var v in bs.Visuals)
                if (GodotObject.IsInstanceValid(v)) v.QueueFree();
            _trackedSets.RemoveAt(i);
            continue;
        }

        var transforms = bs.Set.Call("all_bullets_get_transforms").AsGodotArray<Transform2D>();
        int count = Math.Min(transforms.Count, bs.Visuals.Length);
        for (int j = 0; j < count; j++)
        {
            if (!GodotObject.IsInstanceValid(bs.Visuals[j])) continue;
            var t = transforms[j];
            bs.Visuals[j].Position = new Vector3(t.Origin.X, 0f, t.Origin.Y);
        }
    }
}
```

**Replace the old `_blastBullets.Call("spawn_directional_bullets", _directionalData)` in each Spawn\* method with `SpawnAndTrack(...)`, passing the corresponding per-pattern scene:**

| Method | bulletCount arg | sceneOverride arg |
|--------|-----------------|-------------------|
| `SpawnStraight` (inside loop) | `1` | `StraightBulletScene` |
| `SpawnRing` | `p.Count` | `RingBulletScene` |
| `SpawnSine` | `p.Count` | `SineBulletScene` |
| `SpawnBurst` | `p.Count` | `BurstBulletScene` |
| `SpawnSpiral` | `p.Arms` | `SpiralBulletScene` |
| `SpawnHoming` | `p.Count` | `HomingBulletScene` |
| `SpawnDirectional` (utility) | `1` | `null` (always uses default) |

When a per-pattern export is `null` (not set in the Inspector), `SpawnAndTrack` falls back to `BulletScene`. So setting only `BulletScene` in the Inspector works for all patterns at once; you override individual ones only when you want a different asset per type.

The existing `SetSpeed` / `_directionalData` setup before each call is **unchanged** — `SpawnAndTrack` reads `_directionalData` after the caller has set it up.

**Clean up in `_ExitTree()`:**
```csharp
foreach (var bs in _trackedSets)
    foreach (var v in bs.Visuals)
        if (GodotObject.IsInstanceValid(v)) v.QueueFree();
_trackedSets.Clear();
```

---

### 2. `Scenes/bullet_manager.tscn`

Keep the `BlastBullets` (BulletFactory2D) child **unchanged**. Add the `BulletScene` default export (and any per-pattern overrides you want set at scene-authoring time):

```
[ext_resource type="PackedScene" uid="uid://b6tg6r2vyhiim"
    path="res://Assets/KayKit_Medieval_Hexagon_Pack/tiles/base/hex_bullet.tscn" id="2_bullet"]

[node name="BulletManager" type="Node2D"]
script = ExtResource("1_hr8h3")
BulletScene = ExtResource("2_bullet")
# Per-pattern overrides are left unset (null) initially — they show up in the
# Inspector as empty PackedScene slots and can be filled without touching code.

[node name="BlastBullets" type="BulletFactory2D" parent="."]   ← keep as-is
```

---

### 3. `Scenes/world_3d.tscn`

Remove the static `hex_grass` test instance node and its `ext_resource` entry (`id="10_7lipw"` — the hex_bullet.tscn reference). Bullets are now spawned dynamically by BulletManager.

---

### What is NOT changed

- All `_directionalData` setup (`SetupDirectionalBullets`, `SetSpeed`)
- Collision layer/mask settings on the 2D bullets
- BlastBullets2D is still the authority for future collision (`area_entered` signal)

---

### Future: wiring collision (when ReportHit is implemented)

Connect `_blastBullets`'s `area_entered(area, set, bullet_index, custom_data, transform_2d)` signal. Find the matching `BulletSet3D` entry by comparing `set` to `_trackedSets[i].Set`, then `QueueFree(_trackedSets[i].Visuals[bullet_index])` to destroy the 3D visual on hit.

---

### Tuning

- Bullets too large/small → adjust `BulletScale` on the BulletManager node in the Inspector (default `8`)
- Bullets at wrong height → change the `0f` Y-component in `new Vector3(t.Origin.X, 0f, t.Origin.Y)`

### Adding a new bullet asset later

1. Create (or import) the new `.tscn` visual scene.
2. In the Inspector on the BulletManager node, drag it into the corresponding per-pattern slot (e.g., `Homing Bullet Scene`). Leave it empty to keep using the default.
3. No code changes required.
