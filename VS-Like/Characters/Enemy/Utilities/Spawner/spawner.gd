extends Node2D
## spawner.gd
##
## Handles all spawning: enemies, elite enemies, circle patterns, food drops,
## AND players (new in multiplayer).
##
## IMPORTANT: Enemy spawning only runs on the SERVER.
## Clients do nothing here — they receive enemy positions via the
## MultiplayerSynchronizer attached to each enemy node.
##
## Player spawning also happens here: when a peer connects (or when the server
## itself starts), we instantiate a Player scene, set its multiplayer_authority
## to that peer's id, and add it to the scene.  Because every peer has the
## Spawner in their scene tree, the add_child call needs to be replicated to
## all peers — we do this with an RPC.

# ── preloads ──────────────────────────────────────────────────────────────────
const DROP_SCENE:    PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const RES_FOOD:      ResDropFood = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/res_food.tres")
const ENEMY_SCENE:   PackedScene = preload("res://VS-Like/Characters/Enemy/enemy.tscn")
const PLAYER_SCENE:  PackedScene = preload("res://VS-Like/Characters/Player/player.tscn")

# ── constants ─────────────────────────────────────────────────────────────────
const MIN_SPAWN_RADIUS: int = 400
const MAX_ENEMIES:      int = 300

# ── exports ───────────────────────────────────────────────────────────────────
## Drag the starting enemy resource(s) here in the Godot editor.
## (Zombie, Bat, Cyclops are unlocked progressively by _add_enemy_type.)
@export var all_res_enemy: Array[ResEnemy]

# ── node references ───────────────────────────────────────────────────────────
@onready var spawn_timer: Timer = $SpawnTimer

# ── state ─────────────────────────────────────────────────────────────────────
var enemies_count: int = 0 : set = _set_enemies_count
var minutes: int        : set = _set_minutes
var seconds: int        : set = _set_seconds

## Counter used to give each enemy a unique id. Starts at 0, increments each
## spawn.  Only the server assigns ids (clients never spawn enemies).
var _next_enemy_id: int = 0

## Player spawn positions, indexed by join order.
## First joiner (the host) spawns at index 0, etc.
const PLAYER_SPAWN_POSITIONS: Array = [
	Vector2(0, 0),
	Vector2(200, 0),
	Vector2(-200, 0),
	Vector2(0, 200),
]
var _spawn_position_index: int = 0


# ── ready ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	# Both server and clients run _ready, but we only wire up Network signals
	# and start spawning on the server.
	if not multiplayer.is_server():
		return

	# Listen for peers connecting / disconnecting so we can spawn / remove
	# their player nodes.
	Network.player_joined.connect(_on_player_joined)
	Network.player_left.connect(_on_player_left)

	# Spawn the host's own player immediately (the server's peer_id is always 1).
	_spawn_player_for_peer(1)


# ── player spawning ───────────────────────────────────────────────────────────

## Called on the server when any peer (including the host, peer_id 1) connects.
func _on_player_joined(peer_id: int) -> void:
	_spawn_player_for_peer(peer_id)


## Called on the server when a peer disconnects.
func _on_player_left(peer_id: int) -> void:
	# Tell all peers to remove that player's node.
	_remove_player.rpc(peer_id)


## Server-side: instantiate a player and tell ALL peers (including self) to
## add it to their scene with the correct authority.
func _spawn_player_for_peer(peer_id: int) -> void:
	# Pick a spawn position, cycling through the list if more players join.
	var pos: Vector2 = PLAYER_SPAWN_POSITIONS[
		_spawn_position_index % PLAYER_SPAWN_POSITIONS.size()
	]
	_spawn_position_index += 1

	# RPC to every peer (including the server itself via call_local) so they
	# all create the same player node with the right authority set.
	_create_player_node.rpc(peer_id, pos)


## Runs on EVERY peer (server + all clients).
## Creates the player node, names it by peer_id so RPCs can route to it,
## and sets authority so only the owning peer processes input.
@rpc("authority", "call_local", "reliable")
func _create_player_node(peer_id: int, spawn_pos: Vector2) -> void:
	var player: Player = PLAYER_SCENE.instantiate()

	# The node name MUST be deterministic and identical on all peers.
	# Godot uses it internally to route RPCs to the right node.
	player.name = "Player_%d" % peer_id

	# set_multiplayer_authority tells Godot which peer "owns" this node.
	# The owner is the only one that processes input and sends sync updates.
	player.set_multiplayer_authority(peer_id)

	player.global_position = spawn_pos
	add_child(player)


## Runs on EVERY peer — removes a disconnected player's node.
@rpc("authority", "call_local", "reliable")
func _remove_player(peer_id: int) -> void:
	var node := get_node_or_null("Player_%d" % peer_id)
	if node:
		node.queue_free()


# ── enemy spawning (SERVER ONLY) ──────────────────────────────────────────────

func _spawn_single_enemy(is_elite: bool = false) -> void:
	# Pick a random position near whichever player is closest to the world
	# center — or just the first available player.
	var ref_player := _get_reference_player()
	if ref_player == null:
		return

	var spawn_pos: Vector2 = _get_random_spawn_pos(ref_player.global_position, MIN_SPAWN_RADIUS)
	_create_enemy(spawn_pos, _get_enemy(), is_elite)


func _spawn_circle_pattern(enemies_to_spawn: int) -> void:
	var ref_player := _get_reference_player()
	if ref_player == null:
		return

	var player_pos:  Vector2 = ref_player.global_position
	var angle_step:  float   = TAU / enemies_to_spawn

	for i in range(enemies_to_spawn):
		var angle:     float   = i * angle_step
		var spawn_pos: Vector2 = player_pos + Vector2(cos(angle), sin(angle)) * MIN_SPAWN_RADIUS
		_create_enemy(spawn_pos, all_res_enemy[0])


func _create_enemy(spawn_pos: Vector2, res_enemy: ResEnemy, is_elite: bool = false) -> void:
	var enemy: Enemy = ENEMY_SCENE.instantiate()
	enemy.res_enemy       = res_enemy
	enemy.is_elite        = is_elite
	enemy.global_position = spawn_pos
	enemy.enemy_id        = _next_enemy_id
	_next_enemy_id       += 1

	enemy.died.connect(enemy_died)
	add_child(enemy)
	enemies_count += 1


# ── helpers ───────────────────────────────────────────────────────────────────

## Returns any available player to use as a spawn-position reference.
## Prefers the first player in the dictionary.
func _get_reference_player() -> Player:
	if Global.players.is_empty():
		return null
	return Global.players.values()[0]


func _get_random_spawn_pos(global_pos: Vector2, radius_distance: int) -> Vector2:
	var angle    := randf() * TAU
	var distance := radius_distance + randf() * 100
	return global_pos + Vector2(cos(angle), sin(angle)) * distance


func _get_enemy() -> ResEnemy:
	return all_res_enemy.pick_random()


func enemy_died() -> void:
	enemies_count -= 1


func _add_enemy_type(minute: int) -> void:
	match minute:
		1:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Zombie/res_zombie.tres"))
			spawn_timer.wait_time = 0.75
		2:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Bat/res_bat.tres"))
			spawn_timer.wait_time = 0.5
		3:
			all_res_enemy.append(load("res://VS-Like/Characters/Enemy/Types/Cyclops/res_cyclops.tres"))
			spawn_timer.wait_time = 0.35


# ── setters (update debug UI labels) ─────────────────────────────────────────

func _set_enemies_count(value: int) -> void:
	enemies_count = value
	%EnemiesCountLabel.text = "ENEMIES: " + str(value)


func _set_minutes(value: int) -> void:
	minutes = value
	if multiplayer.is_server():
		_add_enemy_type(minutes)
	%Minutes.text = _set_digits(minutes)


func _set_seconds(value: int) -> void:
	seconds = value
	if seconds >= 60:
		seconds  = 0
		minutes += 1
	%Seconds.text = _set_digits(seconds)


func _set_digits(n: int) -> String:
	return ("0" if str(abs(n)).length() == 1 else "") + str(n)


# ── timer callbacks ───────────────────────────────────────────────────────────

func _on_timer_timeout() -> void:
	seconds += 1


func _on_spawn_timer_timeout() -> void:
	if not multiplayer.is_server():
		return
	if enemies_count < MAX_ENEMIES:
		_spawn_single_enemy()


func _on_pattern_timer_timeout() -> void:
	if not multiplayer.is_server():
		return
	if enemies_count < MAX_ENEMIES:
		var to_spawn := min(40, MAX_ENEMIES - enemies_count)
		_spawn_circle_pattern(to_spawn)


func _on_elite_timer_timeout() -> void:
	if not multiplayer.is_server():
		return
	_spawn_single_enemy(true)


func _on_food_timer_timeout() -> void:
	if not multiplayer.is_server():
		return

	var ref_player := _get_reference_player()
	if ref_player == null:
		return

	var spawn_pos: Vector2 = _get_random_spawn_pos(
		ref_player.global_position,
		randi_range(MIN_SPAWN_RADIUS, 600)
	)

	var food: Drop  = DROP_SCENE.instantiate()
	food.resource        = RES_FOOD
	food.global_position = spawn_pos
	add_child(food)
