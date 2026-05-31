# Hex Grid + Chunking System

This document covers the mathematical foundation and planned implementation for the game's hex building grid. References:
- https://www.redblobgames.com/grids/hexagons/ (coordinate systems, pixel↔hex, storage)
- https://observablehq.com/@sanderevers/hexagon-tiling-of-an-hexagonal-grid (small_to_big algorithm)
- https://observablehq.com/@sanderevers/hexmod-representation (hexmod: compact local index)

---

## System Overview

Two independent coordinate layers coexist:

| Layer | Purpose | Coordinates |
|-------|---------|-------------|
| **Continuous** | Players, enemies, bullets, physics | `Vector2` (float world space) |
| **Small hex** | Individual building tiles | Axial `(hq, hr)` integers |
| **Chunk hex** | AoI subscriptions, chunk ownership | Axial `(cq, cr)` integers |

The **map boundary** is a **rhombus in chunk space**: chunks at `(cq, cr)` where `0 ≤ cq < chunkCols` and `0 ≤ cr < chunkRows`. Each chunk is itself a hexagonal region of small hexes with radius `hexRadius`. The continuous coordinate world is separate and may wrap (torus); the hex building grid does not wrap — it has hard boundaries.

The two layers never interact during hit detection. The only conversion point is at immunity zone enter/exit: a player entering a structure calls `WorldToHex` once to set `player.IsInImmunityZone = true`. No per-bullet per-tick conversion.

---

## Coordinate Systems

### Cube Coordinates (math layer)

Three integers `(x, y, z)` with the constraint `x + y + z = 0`. All hex arithmetic (distance, neighbors, rotation, ring) is cleanest in cube coords. `y` is always derived as `-x - z` and need not be stored.

Mapping to redblobgames notation: `q = x`, `r = z`, `s = y = -q - r`.

### Axial Coordinates (storage layer)

Store only `(q, r)`; compute `s = -q - r` when needed. Small hex tiles: `(hq, hr)`. Chunk tiles: `(cq, cr)`.

The `small_to_big` and `hexmod` algorithms below use cube notation `(x, y, z)` internally. When calling them, pass `x = hq`, `y = -(hq + hr)`, `z = hr`.

### World Coordinates

`Vector2` float `(wx, wy)` used by the physics/bullet system. Pointy-top hex orientation:

```csharp
// Hex → World (center of hex tile)
static Vector2 HexToWorld(int hq, int hr, float R) {
    float innerR = R * 0.866025404f;  // R * sqrt(3)/2
    float wx = 2f * innerR * hq + innerR * hr;
    float wy = 1.5f * R * hr;
    return new Vector2(wx, wy);
}
```

---

## Constants (all derived from hexRadius)

Given `hexRadius` (the distance in small hexes from a chunk's center to its outer ring):

```csharp
int area  = 3 * hexRadius * hexRadius + 3 * hexRadius + 1;  // small hexes per chunk
int shift = 3 * hexRadius + 2;                               // helper constant
```

`area` is also the number of unique hexmod values (0 … area−1) and the size of any per-chunk data array.

---

## Core Algorithms

All functions below are in C# using floored integer division (`FloorDiv`) and non-negative modulo (`Mod`):

```csharp
static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
static int Mod(int a, int b) => ((a % b) + b) % b;
```

### WorldToHex — continuous position → small hex cube coords

BitCraft's algorithm (fractional cube rounding):

```csharp
static (int x, int z) WorldToHex(float wx, float wy, float R) {
    float innerR = R * 0.866025404f;
    float fx = wx / (innerR * 2f);
    float fy = -fx;
    float offset = wy / (R * 3f);
    fx -= offset;
    fy -= offset;
    int ix = (int)MathF.Round(fx);
    int iy = (int)MathF.Round(fy);
    int iz = (int)MathF.Round(-fx - fy);
    if (ix + iy + iz != 0) {
        float dx = MathF.Abs(fx - ix), dy = MathF.Abs(fy - iy), dz = MathF.Abs(-fx - fy - iz);
        if (dx > dy && dx > dz) ix = -iy - iz;
        else if (dz > dy)       iz = -ix - iy;
    }
    // Returns cube (x=hq, z=hr); y = -x-z (not stored)
    return (ix, iz);
}
```

### SmallToBig — small hex → chunk (big hex) cube coords

From the sanderevers notebook. Given small hex cube `(x, y, z)` returns chunk cube `(ci, cj, ck)`:

```csharp
static (int ci, int cj, int ck) SmallToBig(int x, int y, int z, int area, int shift) {
    int xh = FloorDiv(y + shift * x, area);
    int yh = FloorDiv(z + shift * y, area);
    int zh = FloorDiv(x + shift * z, area);
    int ci = FloorDiv(1 + xh - yh, 3);
    int cj = FloorDiv(1 + yh - zh, 3);
    int ck = FloorDiv(1 + zh - xh, 3);
    return (ci, cj, ck);
}
```

Chunk axial coords: `cq = ci`, `cr = ck`. Validate map bounds: `0 ≤ cq < chunkCols && 0 ≤ cr < chunkRows`.

### CenterOf — chunk cube coords → small hex cube coords of chunk center

```csharp
static (int x, int y, int z) CenterOf(int ci, int cj, int ck, int hexRadius) {
    int r = hexRadius;
    int x = (r + 1) * ci - r * ck;
    int y = (r + 1) * cj - r * ci;
    int z = (r + 1) * ck - r * cj;
    return (x, y, z);
}
```

### Hexmod — small hex → local index within its chunk

Maps each small hex to a unique integer `m ∈ [0, area)`:

```csharp
static int Hexmod(int x, int y, int z, int area, int shift) {
    return Mod(y + shift * x, area);
}
```

**Properties:**
- Moving by vector `(dx, dy, dz)` adds `Hexmod(dx, dy, dz)` to `m` (mod area). This is the key property for fast neighbor lookup: no need to re-call SmallToBig.
- 60° clockwise rotation multiplies `m` by `shift` (mod area). Clockwise rotation in cube: `(x,y,z) → (-z,-x,-y)`.
- Counterclockwise rotation multiplies by `shift_inv = 3*hexRadius*hexRadius` (mod area).

Use hexmod as the array index into per-chunk data (terrain type, building data, etc.).

### InvHexmod — local index → relative small hex cube coords

Recovers the cube coords of a small hex relative to its chunk center, given its hexmod value:

```csharp
static (int x, int y, int z) InvHexmod(int m, int hexRadius, int shift) {
    int r = hexRadius;
    int ms  = FloorDiv(m + r,     shift);
    int mcs = FloorDiv(m + 2 * r, shift - 1);
    int x =  ms * (r + 1) + mcs * (-r);
    int y =  m  + ms * (-2 * r - 1) + mcs * (-r - 1);
    int z = -m  + ms * r             + mcs * (2 * r + 1);
    return (x, y, z);
}
```

To get absolute small hex cube coords for local index `m` in chunk `(ci, cj, ck)`:
```csharp
var (cx, cy, cz) = CenterOf(ci, cj, ck, hexRadius);
var (rx, ry, rz) = InvHexmod(m, hexRadius, shift);
int absX = cx + rx, absY = cy + ry, absZ = cz + rz;  // absX + absY + absZ == 0 ✓
```

---

## Rhombus Map Structure

Chunks live at axial positions `(cq, cr)` where `cq ∈ [0, chunkCols)` and `cr ∈ [0, chunkRows)`. In cube coords `ci = cq`, `ck = cr`, `cj = -cq - cr`.

**Flat array index:**
```csharp
int ChunkArrayIndex(int cq, int cr, int chunkCols) => cr * chunkCols + cq;
```

**SpacetimeDB chunk_index (u64 for subscription queries):**
```csharp
// Simple linear encoding — unique within the map
ulong ChunkIndex(int cq, int cr, int chunkCols) => (ulong)(cr * chunkCols + cq);
```

For AoI queries, store `chunk_index` on `BuildingTile` rows and query `WHERE chunk_index IN (...)` for the 3×3 chunk grid surrounding the player (or 9 individual chunk indices).

---

## SpacetimeDB Table: BuildingTile

```csharp
[Table]
public partial class BuildingTile {
    [PrimaryKey, AutoInc] public ulong TileId;
    public int HexQ;          // small hex axial q (= cube x)
    public int HexR;          // small hex axial r (= cube z)
    public int ChunkQ;        // chunk axial cq
    public int ChunkR;        // chunk axial cr
    public int LocalIndex;    // hexmod value ∈ [0, area)
    public ulong ChunkIndex;  // flat chunk index for AoI subscription
    public string BuildingType;
    public Identity OwnerId;
}
```

`(ChunkQ, ChunkR, LocalIndex)` together uniquely identify a tile and can replace `(HexQ, HexR)` — store both only if you need fast lookup by absolute coords. Add an index on `ChunkIndex` for subscription queries.

---

## The GenerateMap Function

The planned function signature:

```csharp
// hexRadius  — distance from chunk center to outer ring (in small hexes)
// chunkCols  — number of chunks across the base of the rhombus (q direction)
// chunkRows  — number of chunks up the height of the rhombus (r direction)
void GenerateMap(int hexRadius, int chunkCols, int chunkRows)
```

**Iteration pattern (chunk-first, then local hexes):**

```csharp
void GenerateMap(int hexRadius, int chunkCols, int chunkRows) {
    int area  = 3 * hexRadius * hexRadius + 3 * hexRadius + 1;
    int shift = 3 * hexRadius + 2;

    for (int cr = 0; cr < chunkRows; cr++) {
        for (int cq = 0; cq < chunkCols; cq++) {
            int ci = cq, ck = cr, cj = -cq - cr;
            ulong chunkIndex = ChunkIndex(cq, cr, chunkCols);

            var (cx, cy, cz) = CenterOf(ci, cj, ck, hexRadius);

            for (int m = 0; m < area; m++) {
                var (rx, ry, rz) = InvHexmod(m, hexRadius, shift);
                int hx = cx + rx;   // cube x = axial q
                int hz = cz + rz;   // cube z = axial r
                // hx + hy + hz == 0; hy = -(hx+hz), not stored

                // Insert or initialize BuildingTile row for (hx, hz)
                // with ChunkQ=cq, ChunkR=cr, LocalIndex=m, ChunkIndex=chunkIndex
            }
        }
    }
}
```

This covers every small hex exactly once with no overlap or gaps.

---

## AoI Subscriptions for Building Data

Client subscribes to building tiles in the 3×3 chunk neighborhood around the player:

```csharp
// Given player world position:
var (hx, hz) = WorldToHex(player.x, player.y, R);
int hy = -(hx + hz);
var (ci, cj, ck) = SmallToBig(hx, hy, hz, area, shift);
int cq = ci, cr = ck;

// Build list of up to 9 chunk indices (clamped to map bounds):
var indices = new List<ulong>();
for (int dr = -1; dr <= 1; dr++) {
    for (int dq = -1; dq <= 1; dq++) {
        int nq = cq + dq, nr = cr + dr;
        if (nq >= 0 && nq < chunkCols && nr >= 0 && nr < chunkRows)
            indices.Add(ChunkIndex(nq, nr, chunkCols));
    }
}
// Subscribe: WHERE chunk_index IN (indices)
```

Re-subscribe when the player's (cq, cr) changes (chunk boundary crossing).

---

## Relationship to Continuous Coordinate System

The continuous coordinate system (player/enemy positions, bullets) and the hex grid are **parallel systems that do not interfere**. One-way conversion rules:

- **Continuous → Hex:** Call `WorldToHex` only at immunity zone boundary (player enters/exits a building). Sets `player.IsInImmunityZone = true/false`. Never called per-bullet or per-tick.
- **Hex → Continuous:** Call `HexToWorld` only when placing a building (converting placed tile to a visual position in Godot). Not called in combat.
- **No hex coord is stored on `PlayerPosition` or `EnemyState`.** Chunk subscriptions for buildings are separate from entity AoI.
- **Bullets ignore hex geometry entirely.** Immunity from buildings is a flag on the player, not a spatial query at hit time.

---

## Key Numbers (with hexRadius=3)

| Constant | Value |
|---------|-------|
| area    | 37 small hexes per chunk |
| shift   | 11 |
| shift_inv (CCW rotation multiplier) | 27 |

With hexRadius=5: area=91, shift=17.
With hexRadius=10: area=331, shift=32.

Choose `hexRadius` once based on desired tile granularity per chunk. A larger radius gives more tiles per chunk (finer building resolution) but more work per GenerateMap call. Changing it after any `BuildingTile` rows exist invalidates all stored `LocalIndex` values.
