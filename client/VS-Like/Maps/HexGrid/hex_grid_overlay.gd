extends Node2D

const SQRT3 := 1.7320508075688772

# Fallback values used before MapConfig is received from the server.
# These match DEFAULT_CHUNK_HEX_RADIUS and HEX_TILE_OUTER_RADIUS in methods.rs.
const DEFAULT_CHUNK_RADIUS := 7
const DEFAULT_OUTER_RADIUS := 32.0

const LINE_COLOR := Color(0.0, 0.0, 0.0, 0.3)
const LINE_WIDTH := 1.0
const LABEL_COLOR := Color(0.0, 0.0, 0.0, 0.75)
const FONT_SIZE := 8

var _outer_radius: float = DEFAULT_OUTER_RADIUS
var _chunk_radius: int   = DEFAULT_CHUNK_RADIUS
var _chunk_cols: int     = 1

var _cam_pos := Vector2.ZERO
var _cam_zoom := Vector2.ONE
var _font: Font

func _ready() -> void:
	z_index = 0
	_font = ThemeDB.fallback_font
	var mgr = get_node_or_null("/root/SpacetimeDBManager")
	if mgr == null:
		return
	if mgr.IsSubscriptionReady:
		_load_map_config(mgr)
	else:
		mgr.SubscriptionReady.connect(_on_subscription_ready)

func _on_subscription_ready() -> void:
	var mgr = get_node_or_null("/root/SpacetimeDBManager")
	if mgr:
		_load_map_config(mgr)

func _load_map_config(mgr) -> void:
	var cfg = mgr.GetMapConfig()
	if cfg == null:
		return
	_chunk_radius = cfg["chunk_hex_radius"]
	_outer_radius = cfg["hex_outer_radius"]
	_chunk_cols   = cfg["chunk_cols"]
	queue_redraw()

func _process(_delta: float) -> void:
	var cam := get_viewport().get_camera_2d()
	if cam == null:
		return
	if cam.global_position != _cam_pos or cam.zoom != _cam_zoom:
		_cam_pos = cam.global_position
		_cam_zoom = cam.zoom
		queue_redraw()

func _draw() -> void:
	var cam := get_viewport().get_camera_2d()
	if cam == null:
		return
	var half := get_viewport_rect().size / cam.zoom * 0.5
	var world_min := _cam_pos - half
	var world_max := _cam_pos + half

	var h_min := _world_to_hex(world_min.x, world_min.y)
	var h_max := _world_to_hex(world_max.x, world_max.y)
	var q_min := mini(h_min.x, h_max.x) - 1
	var q_max := maxi(h_min.x, h_max.x) + 1
	var r_min := mini(h_min.y, h_max.y) - 1
	var r_max := maxi(h_min.y, h_max.y) + 1

	for r in range(r_min, r_max + 1):
		for q in range(q_min, q_max + 1):
			_draw_hex(q, r)

func _draw_hex(q: int, r: int) -> void:
	var center := _hex_to_world(q, r)

	# Outline
	var pts := PackedVector2Array()
	for i in 6:
		var angle := deg_to_rad(30.0 + 60.0 * i)
		pts.append(center + Vector2(_outer_radius * cos(angle), _outer_radius * sin(angle)))
	pts.append(pts[0])
	draw_polyline(pts, LINE_COLOR, LINE_WIDTH, true)

	# Labels
	var chunk := _to_lower_res(q, r, _chunk_radius)
	var cidx := _hex_spiral_index(chunk.x, chunk.y)
	var hex_str   := "%d,%d" % [q, r]
	var chunk_str := "%d,%d" % [chunk.x, chunk.y]
	var cidx_str  := str(cidx)

	var hw := _font.get_string_size(hex_str,   HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
	var cw := _font.get_string_size(chunk_str, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
	var iw := _font.get_string_size(cidx_str,  HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x

	var line := FONT_SIZE + 1
	draw_string(_font, Vector2(center.x - hw * 0.5, center.y - line),
			hex_str,   HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, LABEL_COLOR)
	draw_string(_font, Vector2(center.x - cw * 0.5, center.y + 2),
			chunk_str, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, LABEL_COLOR)
	draw_string(_font, Vector2(center.x - iw * 0.5, center.y + line + 3),
			cidx_str,  HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, LABEL_COLOR)

# ── Hex math (mirrors server methods.rs) ────────────────────────────────────

func _hex_to_world(q: int, r: int) -> Vector2:
	var R := _outer_radius
	return Vector2(R * (SQRT3 * q + SQRT3 * 0.5 * r), R * 1.5 * r)

func _world_to_hex(wx: float, wy: float) -> Vector2i:
	var R := _outer_radius
	var q := (wx * SQRT3 / 3.0 - wy / 3.0) / R
	var r := wy * 2.0 / 3.0 / R
	var s := -q - r
	var rq := roundf(q); var rr := roundf(r); var rs := roundf(s)
	if absf(rq - q) > absf(rr - r) and absf(rq - q) > absf(rs - s):
		rq = -rr - rs
	elif absf(rr - r) > absf(rs - s):
		rr = -rq - rs
	return Vector2i(int(rq), int(rr))

# Bijection Z² → ℕ using hex spiral order (ring ρ = max(|x|,|y|,|z|) in cube coords).
# Unique for all integer (cq, cr) including negative values and fallback chunk_cols = 1.
func _hex_spiral_index(cq: int, cr: int) -> int:
	var x := cq
	var z := cr
	var y := -cq - cr
	var rho := maxi(absi(x), maxi(absi(y), absi(z)))
	if rho == 0:
		return 0
	var B := 3 * rho * (rho - 1) + 1
	var arm: int
	var step: int
	if z == -rho and x > 0:
		arm = 0; step = y
	elif y == rho and x > -rho:
		arm = 1; step = -x
	elif x == -rho and z < rho:
		arm = 2; step = z
	elif z == rho and x < 0:
		arm = 3; step = x + rho
	elif y == -rho and x >= 0 and x < rho:
		arm = 4; step = x
	else:
		arm = 5; step = y + rho
	return B + arm * rho + step

# Port of hexx Hex::to_lower_res
func _to_lower_res(q: int, r: int, radius: int) -> Vector2i:
	var s := -q - r
	var area := float(3 * radius * (radius + 1) + 1)
	var shift := 3 * radius + 2
	var a := floori(float(r + shift * q) / area)
	var b := floori(float(s + shift * r) / area)
	var c := floori(float(q + shift * s) / area)
	return Vector2i(floori(float(1 + a - b) / 3.0), floori(float(1 + b - c) / 3.0))
