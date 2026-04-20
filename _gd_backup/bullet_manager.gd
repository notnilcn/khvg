extends Node
## bullet_manager.gd  —  Enemy bullet system: parametric + simulated.
##
## PARAMETRIC bullets (LINEAR, SINE_WAVE):
##   Position is a pure function of elapsed time.  Server sends one spawn event;
##   clients reconstruct the trajectory locally using the identical formula.
##   Zero positional sync packets after spawn.
##
## SIMULATED bullets (SIMULATED — reserved for homing/guided bullets):
##   Server computes movement each tick and broadcasts the result.
##   Use _rpc_update_simulated to push server position; clients display it.
##   Higher bandwidth but necessary when the trajectory depends on runtime state.
##
## Usage (server only):
##   BulletManager.server_spawn_linear(pos, dir, speed, damage)
##   BulletManager.server_spawn_sine(pos, dir, speed, damage, amplitude, frequency)

# ── pattern type constants ─────────────────────────────────────────────────────
const PATTERN_LINEAR:    int = 0  ## Straight line along direction.
const PATTERN_SINE_WAVE: int = 1  ## Sine wave lateral to the fire direction.
const PATTERN_SIMULATED: int = 2  ## Server-driven; use _rpc_update_simulated.

# ── tunables ──────────────────────────────────────────────────────────────────
const BULLET_VISUAL_SCENE: PackedScene = preload("res://VS-Like/Bullets/BulletVisual/bullet_visual.tscn")
const POOL_SIZE:     int   = 500
const BULLET_RADIUS: float = 4.0   ## Bullet's effective hit radius (px).
const PLAYER_RADIUS: float = 8.0   ## Player's effective hit radius (px).

# ── state ─────────────────────────────────────────────────────────────────────
## Server: live bullet records.  Client: mirrors server via RPCs.
var _active:  Array[Dictionary] = []
## Fast id → record lookup on all peers.
var _by_id:   Dictionary        = {}
var _next_id: int               = 0
## Visual pool and active-visual map (all peers including listen-server).
var _pool:    Array[Node2D]     = []
var _visuals: Dictionary        = {}  # bullet_id(int) -> Node2D


func _ready() -> void:
	for i in POOL_SIZE:
		var v: Node2D = BULLET_VISUAL_SCENE.instantiate()
		v.visible = false
		add_child(v)
		_pool.append(v)


func _process(delta: float) -> void:
	var now: float = Time.get_ticks_msec() * 0.001
	if multiplayer.is_server():
		_server_tick(now, delta)
	_update_visuals(now)


# ── public API — server only ───────────────────────────────────────────────────

## Spawn a straight-line bullet.
func server_spawn_linear(
		spawn_pos: Vector2,
		direction: Vector2,
		speed:     float,
		damage:    float,
		lifetime:  float = 4.0,
) -> void:
	_spawn({
		"type":      PATTERN_LINEAR,
		"spawn_pos": spawn_pos,
		"direction": direction.normalized(),
		"speed":     speed,
		"damage":    damage,
		"lifetime":  lifetime,
		"amplitude": 0.0,
		"frequency": 0.0,
		"sim_pos":   spawn_pos,
	})


## Spawn a sine-wave bullet.  amplitude = max lateral deviation in px;
## frequency = full oscillations per second.
func server_spawn_sine(
		spawn_pos: Vector2,
		direction: Vector2,
		speed:     float,
		damage:    float,
		amplitude: float,
		frequency: float,
		lifetime:  float = 4.0,
) -> void:
	_spawn({
		"type":      PATTERN_SINE_WAVE,
		"spawn_pos": spawn_pos,
		"direction": direction.normalized(),
		"speed":     speed,
		"damage":    damage,
		"lifetime":  lifetime,
		"amplitude": amplitude,
		"frequency": frequency,
		"sim_pos":   spawn_pos,
	})


# ── position function (deterministic — identical on server and clients) ────────

## Returns the world position of a bullet at `elapsed` seconds after its spawn.
## Call this from any peer; the result is identical everywhere for the same inputs.
static func get_position(data: Dictionary, elapsed: float) -> Vector2:
	var sp: Vector2 = data["spawn_pos"]
	var d:  Vector2 = data["direction"]
	match data["type"]:
		PATTERN_SINE_WAVE:
			var perp: Vector2 = Vector2(-d.y, d.x)
			return sp \
				+ d    * (data["speed"]     as float) * elapsed \
				+ perp * (data["amplitude"] as float) \
				       * sin((data["frequency"] as float) * TAU * elapsed)
		PATTERN_SIMULATED:
			return data["sim_pos"]
		_:  # LINEAR
			return sp + d * (data["speed"] as float) * elapsed


# ── server tick ───────────────────────────────────────────────────────────────

func _server_tick(now: float, delta: float) -> void:
	var to_remove: Array[int] = []

	for data: Dictionary in _active:
		var id: int = data["id"]
		if id in to_remove:
			continue  # already hit this frame

		var elapsed: float = now - (data["spawn_time"] as float)
		if elapsed >= (data["lifetime"] as float):
			to_remove.append(id)
			continue

		var cur:  Vector2 = get_position(data, elapsed)
		var prev: Vector2 = get_position(data, maxf(elapsed - delta, 0.0))

		for player: Player in Global.players.values():
			if _swept_hit(prev, cur, player.global_position):
				# Server calls hurt() with RPC so it runs on all peers.
				player.hurt.rpc(data["damage"])
				to_remove.append(id)
				break

	_despawn_many(to_remove)


## Swept circle-vs-segment hit test.  Finds the closest point on segment [a, b]
## to `centre` and checks whether it lies within the combined radii.
func _swept_hit(a: Vector2, b: Vector2, centre: Vector2) -> bool:
	var ab:     Vector2 = b - a
	var len_sq: float   = ab.length_squared()
	var t:      float   = 0.0
	if len_sq > 0.0:
		t = clampf((centre - a).dot(ab) / len_sq, 0.0, 1.0)
	return (a + ab * t).distance_to(centre) < BULLET_RADIUS + PLAYER_RADIUS


func _despawn_many(ids: Array[int]) -> void:
	if ids.is_empty():
		return
	for bid in ids:
		_active = _active.filter(func(d: Dictionary) -> bool: return d["id"] != bid)
		_by_id.erase(bid)
		_hide_bullet(bid)
		_rpc_despawn.rpc(bid)


# ── spawn helper ──────────────────────────────────────────────────────────────

func _spawn(data: Dictionary) -> void:
	data["id"]         = _next_id
	data["spawn_time"] = Time.get_ticks_msec() * 0.001
	_next_id          += 1
	_active.append(data)
	_by_id[data["id"]] = data
	_show_bullet(data)
	_rpc_spawn.rpc(data)  # call_remote — clients add to _active and show visual


# ── RPCs ──────────────────────────────────────────────────────────────────────

## Received on clients when the server spawns a bullet.
@rpc("authority", "call_remote", "reliable")
func _rpc_spawn(data: Dictionary) -> void:
	_active.append(data)
	_by_id[data["id"]] = data
	_show_bullet(data)


## Received on clients when the server despawns a bullet.
@rpc("authority", "call_remote", "reliable")
func _rpc_despawn(id: int) -> void:
	_active = _active.filter(func(d: Dictionary) -> bool: return d["id"] != id)
	_by_id.erase(id)
	_hide_bullet(id)


## Sent by the server each tick for SIMULATED bullets so clients can display
## the server-computed position.  Only needed when PATTERN_SIMULATED is used
## (e.g. homing bullets).  Use unreliable_ordered — stale packets are useless.
@rpc("authority", "call_remote", "unreliable_ordered")
func _rpc_update_simulated(id: int, pos: Vector2) -> void:
	var data: Dictionary = _by_id.get(id, {})
	if not data.is_empty():
		data["sim_pos"] = pos


# ── visual helpers ────────────────────────────────────────────────────────────

func _update_visuals(now: float) -> void:
	for id: int in _visuals:
		var data: Dictionary = _by_id.get(id, {})
		if data.is_empty():
			continue
		var elapsed: float = now - (data["spawn_time"] as float)
		(_visuals[id] as Node2D).global_position = get_position(data, elapsed)


func _show_bullet(data: Dictionary) -> void:
	if _pool.is_empty():
		return
	var v: Node2D = _pool.pop_back()
	v.visible     = true
	v.global_position = data["spawn_pos"]
	_visuals[data["id"]] = v


func _hide_bullet(id: int) -> void:
	var v: Node2D = _visuals.get(id, null)
	if v == null:
		return
	v.visible = false
	_visuals.erase(id)
	_pool.append(v)
