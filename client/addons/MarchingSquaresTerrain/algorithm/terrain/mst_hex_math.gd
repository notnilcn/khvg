# Portions derived from hex_map_godot (135dc75) - MIT. See THIRD_PARTY_NOTICES.md.
@tool
extends RefCounted
class_name MSTHexMath
## Pointy-top axial hex math shared by the terrain chunk, editor tools and JSON IO.
## Ports of client/Scripts/World/HexMath.cs and server/spacetimedb/src/world/hex.rs
## (vendored hexx 0.24.0 semantics), including the canonical-lap wrap helpers (wrap_hex/
## wrap_chunk, mirroring the server's wrap_hex_coords) — all copies must change together.


const SQRT3 : float = 1.7320508075688772

# Axial deltas for the 6 neighbor directions, indexed 0-5, where direction D's
# hex_to_world offset has angle 60*D degrees (same table as the server's HEX_NEIGHBOR_DELTAS).
const NEIGHBOR_OFFSETS = [
	Vector2i(1, 0),
	Vector2i(0, 1),
	Vector2i(-1, 1),
	Vector2i(-1, 0),
	Vector2i(0, -1),
	Vector2i(1, -1),
]


## World position (x, z) of a hex center. Port of HexMath.HexToWorld.
static func hex_to_world(q: int, r: int, size: float) -> Vector2:
	return Vector2(size * (SQRT3 * q + SQRT3 * 0.5 * r), size * 1.5 * r)


## Hex containing a world position (x, z). Port of HexMath.WorldToHex (cube rounding).
static func world_to_hex(x: float, z: float, size: float) -> Vector2i:
	var q := (x * SQRT3 / 3.0 - z / 3.0) / size
	var r := z * 2.0 / 3.0 / size
	var s := -q - r
	var rq := roundf(q)
	var rr := roundf(r)
	var rs := roundf(s)
	if absf(rq - q) > absf(rr - r) and absf(rq - q) > absf(rs - s):
		rq = -rr - rs
	elif absf(rr - r) > absf(rs - s):
		rr = -rq - rs
	return Vector2i(int(rq), int(rr))


## Center hex of a chunk in global axial coords. Port of hexx Hex::to_higher_res.
static func chunk_center(cq: int, cr: int, radius: int) -> Vector2i:
	var s := -cq - cr
	return Vector2i(cq * (radius + 1) - radius * s, cr * (radius + 1) - radius * cq)


## Chunk owning a hex in global axial coords. Port of hexx Hex::to_lower_res.
static func world_to_chunk(q: int, r: int, radius: int) -> Vector2i:
	var s := -q - r
	var area := float(3 * radius * (radius + 1) + 1)
	var shift := 3 * radius + 2
	var a := floori(float(r + shift * q) / area)
	var b := floori(float(s + shift * r) / area)
	var c := floori(float(q + shift * s) / area)
	return Vector2i(floori((1.0 + a - b) / 3.0), floori((1.0 + b - c) / 3.0))


## Local axial offset of a hex within its owning chunk. Port of hexx Hex::to_local.
static func hex_to_local(q: int, r: int, radius: int) -> Vector2i:
	var chunk := world_to_chunk(q, r, radius)
	return Vector2i(q, r) - chunk_center(chunk.x, chunk.y, radius)


## Wrap a chunk coordinate into the canonical lap (0 .. cols-1, 0 .. rows-1) with positive
## modulo. Mirrors the server's wrap_chunk_coords; a non-positive grid size passes through.
static func wrap_chunk(cq: int, cr: int, cols: int, rows: int) -> Vector2i:
	if cols <= 0 or rows <= 0:
		return Vector2i(cq, cr)
	return Vector2i(posmod(cq, cols), posmod(cr, rows))


## Map an arbitrary hex back into the canonical lap: the chunk coords wrap and the local offset
## inside the chunk is preserved, so the wrapped hex is the same point on the torus. Port of
## HexMath.WrapHex (client/Scripts/World/HexMath.cs) and the server's world/wrap.rs
## wrap_hex_coords — all copies must change together.
static func wrap_hex(q: int, r: int, radius: int, cols: int, rows: int) -> Vector2i:
	if cols <= 0 or rows <= 0:
		return Vector2i(q, r)
	var chunk := world_to_chunk(q, r, radius)
	var wrapped_chunk := wrap_chunk(chunk.x, chunk.y, cols, rows)
	if wrapped_chunk == chunk:
		return Vector2i(q, r)
	var center := chunk_center(wrapped_chunk.x, wrapped_chunk.y, radius)
	return center + hex_to_local(q, r, radius)


## All local axial offsets inside a hex-shaped chunk (3R(R+1)+1 hexes).
static func hex_range(radius: int) -> Array[Vector2i]:
	var offsets : Array[Vector2i] = []
	for q in range(-radius, radius + 1):
		for r in range(maxi(-radius, -q - radius), mini(radius, -q + radius) + 1):
			offsets.append(Vector2i(q, r))
	return offsets


## Axial delta of the neighbor in direction dir (0-5, server neighbor-delta order).
static func neighbor_offset(dir: int) -> Vector2i:
	return NEIGHBOR_OFFSETS[posmod(dir, 6)]


## XZ offset of corner dir (0-5) from the hex center, pointy-top (angle 60*dir - 30 degrees).
static func hex_corner_offset(dir: int, size: float) -> Vector2:
	var angle := deg_to_rad(60.0 * dir - 30.0)
	return Vector2(size * cos(angle), size * sin(angle))


#region texture slot encoding

## Convert a texture slot (0-15) to the vertex color pair encoding read by the terrain shader.
## Slot = c0_channel * 4 + c1_channel. Consolidated from MSTDataHandler's private helpers.
static func slot_to_color_pair(slot: int) -> Array:
	var c0 := Color(0, 0, 0, 0)
	var c1 := Color(0, 0, 0, 0)
	@warning_ignore("integer_division")
	var c0_ch := slot / 4
	var c1_ch := slot % 4
	
	match c0_ch:
		0: c0.r = 1.0
		1: c0.g = 1.0
		2: c0.b = 1.0
		3: c0.a = 1.0
	
	match c1_ch:
		0: c1.r = 1.0
		1: c1.g = 1.0
		2: c1.b = 1.0
		3: c1.a = 1.0
	
	return [c0, c1]


## Convert a vertex color pair back to its texture slot (0-15). Inverse of slot_to_color_pair.
static func color_pair_to_slot(c0: Color, c1: Color) -> int:
	var c0_idx := 0
	var c0_max := c0.r
	if c0.g > c0_max: c0_max = c0.g; c0_idx = 1
	if c0.b > c0_max: c0_max = c0.b; c0_idx = 2
	if c0.a > c0_max: c0_idx = 3
	
	var c1_idx := 0
	var c1_max := c1.r
	if c1.g > c1_max: c1_max = c1.g; c1_idx = 1
	if c1.b > c1_max: c1_max = c1.b; c1_idx = 2
	if c1.a > c1_max: c1_idx = 3
	
	return c0_idx * 4 + c1_idx

#endregion

#region smooth hex heights

## Smooth-top height sampling. Lockstep with the server's world/heights.rs: the 7
## world-unit samples are [center, corner c0..c5] with the corners at
## hex_corner_offset(dir, size) (angle 60*dir - 30) in neighbor-delta order, matching
## the chunk's smooth fan lattice and the C# feeder's draw_hex_heights payload -
## all copies change together.

## Fan triangle containing a local XZ offset: [dir, w_center, w_corner_dir,
## w_corner_dir+1], the barycentric weights on (center, corner dir, corner dir+1).
## Points outside the hex use the closest triangle's linear extension.
static func hex_fan_coords(local_xz: Vector2, size: float) -> Array:
	var best_dir := 0
	var best_weights := Vector3.ZERO
	var best_min := -INF
	for dir in range(6):
		var b := hex_corner_offset(dir, size)
		var c := hex_corner_offset(dir + 1, size)
		var weights := _triangle_barycentric(local_xz, b, c)
		var min_weight : float = minf(minf(weights.x, weights.y), weights.z)
		if min_weight > best_min:
			best_min = min_weight
			best_dir = dir
			best_weights = weights
	return [best_dir, best_weights.x, best_weights.y, best_weights.z]


## Barycentric weights (on (0,0), b, c) of a point p in the triangle (0,0), b, c.
static func _triangle_barycentric(p: Vector2, b: Vector2, c: Vector2) -> Vector3:
	var denom := b.x * c.y - c.x * b.y
	if is_zero_approx(denom):
		return Vector3(1.0, 0.0, 0.0)
	var w_b := (p.x * c.y - c.x * p.y) / denom
	var w_c := (b.x * p.y - p.x * b.y) / denom
	return Vector3(1.0 - w_b - w_c, w_b, w_c)


## Barycentric interpolation of the 7 server heights at a local XZ offset from the
## hex center (closest fan's linear extension outside the hex).
static func smooth_hex_height(heights: PackedInt32Array, size: float, local_xz: Vector2) -> float:
	var fan := hex_fan_coords(local_xz, size)
	var dir : int = fan[0]
	var h0 := float(heights[0])
	var h1 := float(heights[1 + dir])
	var h2 := float(heights[1 + ((dir + 1) % 6)])
	return float(fan[1]) * h0 + float(fan[2]) * h1 + float(fan[3]) * h2


## Surface normal of the sampled smooth top: central differences of smooth_hex_height
## with the given probe distance (default size * 0.25), as (-dx, 1, -dz) normalized.
static func smooth_hex_normal(heights: PackedInt32Array, size: float, local_xz: Vector2, probe: float = -1.0) -> Vector3:
	var h : float = probe if probe > 0.0 else size * 0.25
	var dx := (smooth_hex_height(heights, size, local_xz + Vector2(h, 0.0)) - smooth_hex_height(heights, size, local_xz - Vector2(h, 0.0))) / (2.0 * h)
	var dz := (smooth_hex_height(heights, size, local_xz + Vector2(0.0, h)) - smooth_hex_height(heights, size, local_xz - Vector2(0.0, h))) / (2.0 * h)
	return Vector3(-dx, 1.0, -dz).normalized()

#endregion
