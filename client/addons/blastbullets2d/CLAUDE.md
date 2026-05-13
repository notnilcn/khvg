# CLAUDE.md — BlastBullets2D + SpacetimeDB Translation Layer

This file documents how to implement Task 6 (bullet pattern system) by mapping SpacetimeDB `BulletPattern` rows to blastbullets2d spawn calls. Read this alongside the root `CLAUDE.md` Task 6 section.

## Plugin Overview

**BlastBullets2D** uses a `BulletFactory2D` node as the central spawner. All bullets are rendered as `MultiMeshInstance2D` nodes for performance. There are two concrete bullet types:

- **`DirectionalBullets2D`** — each bullet has its own direction derived from its `Transform2D` rotation. Supports homing, orbiting, per-bullet speed/rotation data, curves, and movement patterns. Spawned via `spawn_directional_bullets(data)` or `spawn_controllable_directional_bullets(data)` (returns the node for runtime control).
- **`BlockBullets2D`** — all bullets in the group travel in the same direction at the same speed. Simpler and slightly more efficient. Spawned via `spawn_block_bullets(data)`.

The `BulletFactory2D` emits `body_entered` and `area_entered` signals for hit detection. All setup goes into a `XxxBulletsData2D` resource before spawning.

---

## SpacetimeDB `BulletPattern` Table Schema

Every column below is a primitive type (no Godot objects in the DB). The translation layer on the client converts these into the appropriate blastbullets2d resources and calls.

### Identity & Timing

| Column | C# Type | Notes |
|--------|---------|-------|
| `PatternId` | `ulong` PK AutoInc | Passed to `ReportHit` so the server can audit |
| `EnemyId` | `ulong` | Which enemy fired this (links to `EnemyState.enemy_id`) |
| `OriginX` | `float` | Enemy global position X at fire time |
| `OriginY` | `float` | Enemy global position Y at fire time |
| `SpawnTime` | `Timestamp` | Server time when fired; used for late-join position reconstruction |
| `ExpiresAt` | `Timestamp` | `SpawnTime + MaxLifeTime + audit_retention_window`; scheduled reducer deletes past this |

### Bullet Type

| Column | C# Type | Notes |
|--------|---------|-------|
| `IsBlockBullets` | `bool` | `false` → `spawn_directional_bullets`, `true` → `spawn_block_bullets` |

### Pattern Shape (determines `Transform2D[]` generation)

All `Transform2D[]` are generated client-side from these columns. The `origin` is `Vector2(OriginX, OriginY)`.

| Column | C# Type | Notes |
|--------|---------|-------|
| `PatternShape` | `string` | Enum string: `"single"` \| `"circle"` \| `"fan"` \| `"line"` \| `"grid"` |
| `BulletCount` | `int` | Total bullets spawned. For `"single"` always 1. For `"grid"` = GridCols × GridRows |
| `StartAngleDegrees` | `float` | Base direction angle in degrees (Godot convention: 0° = right, 90° = down). For `"circle"` this is the offset from which even distribution begins. For `"fan"` this is the center of the arc. |
| `SpreadAngleDegrees` | `float` | Total arc for `"fan"` (e.g., 60 = 30° either side of center). Ignored for `"circle"` (always 360°) and `"single"`. |
| `RadiusOffset` | `float` | Spawn radius offset from origin. Bullets spawn at `origin + direction * RadiusOffset`. For `"line"` this is offset along the firing direction before the perpendicular spread begins. |
| `LineSpacing` | `float` | Spacing between bullets in `"line"` pattern (perpendicular to `StartAngleDegrees`). |
| `GridRows` | `int` | Rows per column for `"grid"` pattern. `GridCols = BulletCount / GridRows`. |
| `GridColOffset` | `float` | Column spacing for `"grid"` (maps to `BulletFactory2D.helper_generate_transforms_grid` `column_offset`). |
| `GridRowOffset` | `float` | Row spacing for `"grid"` (maps to `row_offset`). |
| `GridAlignment` | `int` | `BulletFactory2D.Alignment` enum (0–8, default 4 = CENTER). Only used for `"grid"`. |

#### Transform generation algorithms (GDScript)

```gdscript
# Returns Transform2D[] for the pattern, origin is Vector2
static func generate_transforms(pattern: BulletPatternRow, origin: Vector2) -> Array[Transform2D]:
    match pattern.pattern_shape:
        "single":
            var angle = deg_to_rad(pattern.start_angle_degrees)
            var pos = origin + Vector2.RIGHT.rotated(angle) * pattern.radius_offset
            return [Transform2D(angle, pos)]
        
        "circle":
            var transforms: Array[Transform2D] = []
            var n = pattern.bullet_count
            for i in n:
                var angle = deg_to_rad(pattern.start_angle_degrees) + i * TAU / n
                var pos = origin + Vector2.RIGHT.rotated(angle) * pattern.radius_offset
                transforms.append(Transform2D(angle, pos))
            return transforms
        
        "fan":
            var transforms: Array[Transform2D] = []
            var n = pattern.bullet_count
            var center = deg_to_rad(pattern.start_angle_degrees)
            var half_spread = deg_to_rad(pattern.spread_angle_degrees) * 0.5
            for i in n:
                var t = 0.0 if n == 1 else float(i) / float(n - 1)
                var angle = center - half_spread + t * deg_to_rad(pattern.spread_angle_degrees)
                var pos = origin + Vector2.RIGHT.rotated(angle) * pattern.radius_offset
                transforms.append(Transform2D(angle, pos))
            return transforms
        
        "line":
            var transforms: Array[Transform2D] = []
            var n = pattern.bullet_count
            var dir_angle = deg_to_rad(pattern.start_angle_degrees)
            var perp_angle = dir_angle + PI * 0.5
            var forward_offset = Vector2.RIGHT.rotated(dir_angle) * pattern.radius_offset
            var half_width = (n - 1) * pattern.line_spacing * 0.5
            for i in n:
                var perp_offset = Vector2.RIGHT.rotated(perp_angle) * (i * pattern.line_spacing - half_width)
                var pos = origin + forward_offset + perp_offset
                transforms.append(Transform2D(dir_angle, pos))
            return transforms
        
        "grid":
            var marker = Transform2D(deg_to_rad(pattern.start_angle_degrees), origin)
            return BulletFactory2D.helper_generate_transforms_grid(
                pattern.bullet_count, marker,
                pattern.grid_rows, pattern.grid_alignment,
                pattern.grid_col_offset, pattern.grid_row_offset, true, false)
```

> **DEFERRED — Path2D bullet patterns:** The plugin supports `set_bullet_movement_pattern_from_path(Path2D)` and `set_bullet_movement_pattern_from_curve(Curve2D)`, which let you literally draw the bullet's travel path in the Godot editor. This is more expressive than the parametric shapes above. To store a `Curve2D` in SpacetimeDB you would need columns for each control point: position `(x, y)`, in-tangent `(in_x, in_y)`, and out-tangent `(out_x, out_y)` — stored as a packed float array or a JSON string, with a separate `PointCount` column. The `PatternShape` value `"path"` would be reserved for this. Deferred because control-point serialization adds non-trivial schema complexity and the parametric shapes cover the common bullet-hell patterns for now. Revisit when a designer needs to hand-author a specific path that can't be expressed as circle/fan/line/grid.

### Speed

| Column | C# Type | Notes |
|--------|---------|-------|
| `Speed` | `float` | Initial speed of bullets. Maps to `BulletSpeedData2D.speed`. |
| `SpeedMax` | `float` | Max speed after acceleration. Maps to `BulletSpeedData2D.max_speed`. Set equal to `Speed` for constant speed. |
| `SpeedAcceleration` | `float` | Speed increase per frame. Maps to `BulletSpeedData2D.acceleration`. 0 = constant speed. |
| `SpeedRandomMin` | `float` | If > 0: use `BulletSpeedData2D.generate_random_data()` with this as the minimum. Gives each bullet a random speed in `[SpeedRandomMin, SpeedRandomMax]`. |
| `SpeedRandomMax` | `float` | Max bound for random speed. Only used when `SpeedRandomMin > 0`. |

For **BlockBullets2D**, a single `BulletSpeedData2D` is created and assigned to `BlockBulletsData2D.block_speed`. For **DirectionalBullets2D**, if `SpeedRandomMin > 0` use `generate_random_data(BulletCount, ...)`, otherwise create one `BulletSpeedData2D` and blastbullets2d shares it across all bullets.

> **DEFERRED — Speed curves via `BulletCurvesData2D`:** The plugin's `BulletCurvesData2D` resource has a `movement_speed_curve` (a Godot `Curve`) that overrides `BulletSpeedData2D` and lets you define arbitrary speed-over-lifetime profiles (e.g., slow start → fast → slow stop). This is more powerful than the flat `Speed/SpeedMax/SpeedAcceleration` columns above but has the same serialization problem as Path2D: a `Curve` is a sequence of `(time, value, left_tangent, right_tangent, tangent_mode)` points that can't be stored as a single DB column without packing them. The `CurvesPreset` column (see below) is the interim solution — it references a client-side `.tres` file which can contain a fully authored `movement_speed_curve`. When fine-grained per-pattern speed curves are needed from the server, add a `SpeedCurvePoints` string column (JSON-packed array of point structs) and parse it into a `Curve` resource in the translation layer.

### Rotation

| Column | C# Type | Notes |
|--------|---------|-------|
| `RotationSpeed` | `float` | Spin rate in radians/sec. Maps to `BulletRotationData2D.rotation_speed`. 0 = no spin. |
| `RotationSpeedMax` | `float` | Max spin rate. Maps to `BulletRotationData2D.max_rotation_speed`. |
| `RotationAcceleration` | `float` | Spin acceleration per frame. Maps to `BulletRotationData2D.rotation_acceleration`. |
| `StopRotationWhenMaxReached` | `bool` | Maps to `MultiMeshBulletsData2D.stop_rotation_when_max_reached`. |

Provide `BulletRotationData2D` via `MultiMeshBulletsData2D.all_bullet_rotation_data` only if `RotationSpeed != 0`. Omit the array entirely for non-spinning bullets.

### Block-Specific

Only used when `IsBlockBullets = true`.

| Column | C# Type | Notes |
|--------|---------|-------|
| `BlockDirectionDegrees` | `float` | Direction the entire block travels. Maps to `BlockBulletsData2D.block_rotation_radians` (convert: `deg_to_rad(BlockDirectionDegrees)`). |

For block bullets, `StartAngleDegrees` in the transform generation is ignored for direction — `BlockDirectionDegrees` controls movement. The transforms still define the spatial layout of bullets within the block.

### Directional-Specific

Only used when `IsBlockBullets = false`.

| Column | C# Type | Notes |
|--------|---------|-------|
| `AdjustDirectionBasedOnRotation` | `bool` | Maps to `DirectionalBulletsData2D.adjust_direction_based_on_rotation`. Enable if rotating bullets should curve their path. |

### Lifetime & Collision

| Column | C# Type | Notes |
|--------|---------|-------|
| `MaxLifeTime` | `float` | Maps to `MultiMeshBulletsData2D.max_life_time`. Also used to compute `ExpiresAt`. |
| `IsLifeTimeInfinite` | `bool` | Maps to `MultiMeshBulletsData2D.is_life_time_infinite`. Use only for wall-bounded arenas. |
| `BulletMaxCollisionCount` | `int` | Maps to `MultiMeshBulletsData2D.bullet_max_collision_count`. 1 = disable on first hit. 0 = never disabled by collision. |

### Visual

| Column | C# Type | Notes |
|--------|---------|-------|
| `TextureResPath` | `string` | Godot `res://` path to the bullet texture, e.g. `"res://assets/bullets/fireball.png"`. Client calls `load(TextureResPath)`. |
| `TextureSizeX` | `float` | Maps to `MultiMeshBulletsData2D.texture_size.x`. |
| `TextureSizeY` | `float` | Maps to `MultiMeshBulletsData2D.texture_size.y`. |
| `TextureRotationRadians` | `float` | Additional texture rotation offset. Maps to `MultiMeshBulletsData2D.texture_rotation_radians`. |
| `IsTexturePermanentRotation` | `bool` | If true, texture stays at `TextureRotationRadians` instead of matching bullet direction. Maps to `MultiMeshBulletsData2D.is_texture_rotation_permanent`. |

> **DEFERRED — Mixed/layered textures per bullet:** The plugin supports an animated `textures[]` array (`MultiMeshBulletsData2D.textures` + `change_texture_times[]`) and per-instance shader parameters (`instance_shader_parameters`) that could be used to compose unique-looking bullets by mixing multiple texture layers or driving shader uniforms. The goal is to let enemy designers combine a base body texture, an overlay glow, and a tint color to produce visually distinct bullets without needing a separate sprite asset per variant. When implementing, `TextureResPath` can be extended to a comma-separated list of layer paths, or a `ShaderPreset` string column (similar to `CurvesPreset`) can reference a client-side `ShaderMaterial` with configurable uniforms passed via `instance_shader_parameters`. Deferred until the visual design direction for bullet variety is decided.

### Collision Shape

| Column | C# Type | Notes |
|--------|---------|-------|
| `CollisionShapeSizeX` | `float` | Maps to `MultiMeshBulletsData2D.collision_shape_size.x`. |
| `CollisionShapeSizeY` | `float` | Maps to `MultiMeshBulletsData2D.collision_shape_size.y`. |
| `CollisionShapeOffsetX` | `float` | Maps to `MultiMeshBulletsData2D.collision_shape_offset.x`. |
| `CollisionShapeOffsetY` | `float` | Maps to `MultiMeshBulletsData2D.collision_shape_offset.y`. |

### Homing (DirectionalBullets2D only)

Homing requires `spawn_controllable_directional_bullets` (returns the node) instead of `spawn_directional_bullets`. Only available when `IsBlockBullets = false`.

| Column | C# Type | Notes |
|--------|---------|-------|
| `IsHoming` | `bool` | If true, bullets home toward `HomingTargetIdentity`. |
| `HomingTargetIdentity` | `string` | STDB identity string of the target player. Client resolves to a `Node2D` via `StdbPlayerSync.RemoteNodes`. Empty string = home toward the local player. |
| `HomingSmoothing` | `float` | Turning smoothness. Maps to `DirectionalBullets2D.homing_smoothing`. Test 0–20. 0 = instant snap. |
| `HomingTakeControlOfTextureRotation` | `bool` | If true, texture rotates to face the homing direction. Maps to `DirectionalBullets2D.homing_take_control_of_texture_rotation`. |

### Curves (optional)

`BulletCurvesData2D` uses Godot `Curve` resources which can't be stored in the DB. Use a string preset name resolved client-side.

| Column | C# Type | Notes |
|--------|---------|-------|
| `CurvesPreset` | `string` | Empty string = no curves. Named presets defined in a client-side Dictionary (see Curve Presets section below). Example values: `"sine_wave"`, `"spiral_out"`, `"decelerate"`, `"accelerate_then_stop"`. |

---

## Client Translation Layer

The autoload `StdbBulletSync.cs` subscribes to `BulletPattern` and translates rows into blastbullets2d spawn calls.

### On Insert (new pattern fired)

```gdscript
func _on_bullet_pattern_inserted(row: BulletPatternRow) -> void:
    var elapsed = (Time.get_unix_time_from_system() - row.spawn_time_unix) 
    # Skip if already expired
    if elapsed >= row.max_life_time:
        return
    
    var origin = Vector2(row.origin_x, row.origin_y)
    var transforms = BulletPatternHelper.generate_transforms(row, origin)
    
    var data: MultiMeshBulletsData2D
    if row.is_block_bullets:
        var block_data = BlockBulletsData2D.new()
        block_data.block_rotation_radians = deg_to_rad(row.block_direction_degrees)
        block_data.block_speed = _make_speed_data(row)
        data = block_data
    else:
        var dir_data = DirectionalBulletsData2D.new()
        dir_data.adjust_direction_based_on_rotation = row.adjust_direction_based_on_rotation
        dir_data.all_bullet_speed_data = _make_all_speed_data(row)
        data = dir_data
    
    # Shared MultiMeshBulletsData2D fields
    data.transforms = transforms
    data.default_texture = load(row.texture_res_path)
    data.texture_size = Vector2(row.texture_size_x, row.texture_size_y)
    data.texture_rotation_radians = row.texture_rotation_radians
    data.is_texture_rotation_permanent = row.is_texture_rotation_permanent
    data.max_life_time = row.max_life_time - elapsed  # remaining lifetime
    data.is_life_time_infinite = row.is_life_time_infinite
    data.bullet_max_collision_count = row.bullet_max_collision_count
    data.collision_shape_size = Vector2(row.collision_shape_size_x, row.collision_shape_size_y)
    data.collision_shape_offset = Vector2(row.collision_shape_offset_x, row.collision_shape_offset_y)
    data.set_collision_layer_from_array([3])  # enemy bullet layer — fixed game rule
    data.set_collision_mask_from_array([2])   # hits player layer — fixed game rule
    
    if row.rotation_speed != 0.0:
        var rot = BulletRotationData2D.new()
        rot.rotation_speed = row.rotation_speed
        rot.max_rotation_speed = row.rotation_speed_max
        rot.rotation_acceleration = row.rotation_acceleration
        data.all_bullet_rotation_data = [rot]
    
    data.stop_rotation_when_max_reached = row.stop_rotation_when_max_reached
    
    # Custom data carrying damage + pattern id for hit reporting
    var custom = BulletCustomData.new()  # user-defined Resource subclass
    custom.damage = row.damage
    custom.pattern_id = row.pattern_id
    data.bullets_custom_data = custom
    
    # Curves preset
    if row.curves_preset != "":
        data  # set after spawn via all_bullets_set_curves_data
    
    if not row.is_block_bullets and row.is_homing:
        var dir_instance: DirectionalBullets2D = \
            GLOBALS.BULLET_FACTORY.spawn_controllable_directional_bullets(data)
        var target: Node2D = _resolve_homing_target(row.homing_target_identity)
        if target:
            dir_instance.homing_smoothing = row.homing_smoothing
            dir_instance.homing_take_control_of_texture_rotation = row.homing_take_control_of_texture_rotation
            dir_instance.all_bullets_push_back_homing_target(target)
        if row.curves_preset != "":
            dir_instance.call_deferred("all_bullets_set_curves_data", BULLET_CURVE_PRESETS[row.curves_preset])
    else:
        if row.is_block_bullets:
            GLOBALS.BULLET_FACTORY.spawn_block_bullets(data)
        else:
            GLOBALS.BULLET_FACTORY.spawn_directional_bullets(data)
            # For non-controllable directional with curves, access via BulletFactory2D after spawn
            # — needs factory to return the instance or use a deferred approach
```

### Late-Join: Reconstructing In-Flight Bullets

When `SubscriptionReady` fires, any `BulletPattern` rows already in the table represent in-flight bullets. Handle them in `_on_subscription_ready`:

1. For each existing `BulletPattern` row, compute `elapsed = now - spawn_time_unix`.
2. If `elapsed >= max_life_time`, skip (already expired).
3. Adjust transforms to current bullet positions: for simple straight-line bullets, `pos += direction * speed * elapsed`. For accelerating bullets: `pos += direction * (speed * elapsed + 0.5 * acceleration * elapsed²)`.
4. Set `data.max_life_time = row.max_life_time - elapsed` so bullets expire at the right wall-clock time.
5. Spawn normally with the adjusted transforms.

For homing bullets that are already in flight, resolve the target the same way — if the target no longer exists, the bullet continues in its last direction.

---

## Hit Detection → `ReportHit`

Connect to `BulletFactory2D`'s signals in `StdbBulletSync._ready`:

```gdscript
GLOBALS.BULLET_FACTORY.body_entered.connect(_on_bullet_body_entered)

func _on_bullet_body_entered(hit_body, _multimesh, _bullet_index, custom_data, _transform):
    # Only process hits on the local player
    if hit_body != LocalPlayer.instance:
        return
    var cd = custom_data as BulletCustomData
    SpacetimeDBManager.conn.Reducers.ReportHit(cd.pattern_id, cd.damage)
```

The `ReportHit(ulong patternId, int damage)` reducer in `Lib.cs` updates `CharacterData.hp`. No server-side validation at call time — the `BulletPattern` row is the audit record.

---

## Curve Presets

Define client-side in a const dictionary. Each preset is a `BulletCurvesData2D` resource pre-configured with Godot `Curve` assets:

```gdscript
# In StdbBulletSync or a dedicated BulletCurvePresets autoload
const BULLET_CURVE_PRESETS: Dictionary = {
    "sine_wave": preload("res://assets/bullet_curves/sine_wave.tres"),
    "spiral_out": preload("res://assets/bullet_curves/spiral_out.tres"),
    "decelerate": preload("res://assets/bullet_curves/decelerate.tres"),
    "accelerate_then_stop": preload("res://assets/bullet_curves/accel_stop.tres"),
}
```

Each `.tres` is a saved `BulletCurvesData2D` resource with curves authored in the Godot editor. The DB stores only the string key.

---

## What Is NOT Stored in the DB

- **Collision layer/mask** — fixed game rules (enemy bullets always hit layer 2 players). Hardcoded in the translation layer.
- **Texture animation (`textures[]`, `change_texture_times[]`)** — use a single `TextureResPath` for animated sheets. If animated bullets are needed, use a `CurvesPreset` naming convention to indicate the sheet, or extend `TextureResPath` to support comma-separated frames.
- **`monitorable`** — always `false` for enemy bullets; static bodies don't need to be targets.
- **`z_index`** — fixed per bullet type in the translation layer.
- **Object pooling flags** — always `true` in the translation layer for performance.
- **Movement patterns (`Path2D`/`Curve2D`)** — these require Godot scene tree objects. Use `CurvesPreset` for curve-based movement instead.
- **`inherited_velocity_offset`** — if enemies are moving, compute from `EnemyState` dead-reckoning at fire time and apply in the translation layer (no DB column needed).

---

## Collision Layer Reminder

From root CLAUDE.md:
| Layer | Purpose |
|-------|---------|
| 2 | Player |
| 3 | Enemy |

Enemy bullets: `collision_layer = [3]`, `collision_mask = [2]` (detect player bodies).

---

## EnemyAI Integration Point

Enemy spawning, position simulation, and bullet firing are all handled permanently by the **elevated client** (the privileged multiplayer host). In `Spawner._OnStdbSnapshotTimer` or an equivalent fire-rate timer, the elevated client writes a `BulletPattern` row when an enemy fires.

```csharp
// Pseudocode — elevated client
SpacetimeDB.Reducers.InsertBulletPattern(new BulletPattern {
    EnemyId = enemy.EnemyId,
    OriginX = enemy.X,
    OriginY = enemy.Y,
    SpawnTime = ctx.Timestamp,
    ExpiresAt = ctx.Timestamp + TimeSpan.FromSeconds(maxLifeTime + AUDIT_RETENTION_SECONDS),
    IsBlockBullets = false,
    PatternShape = "circle",
    BulletCount = 8,
    StartAngleDegrees = 0,
    SpreadAngleDegrees = 0,
    RadiusOffset = 0,
    Speed = 250,
    SpeedMax = 250,
    MaxLifeTime = 3.0f,
    BulletMaxCollisionCount = 1,
    TextureResPath = "res://assets/bullets/fireball.png",
    TextureSizeX = 32, TextureSizeY = 32,
    CollisionShapeSizeX = 20, CollisionShapeSizeY = 20,
    Damage = 10,
    // ... zero-values for unused fields
});
```
