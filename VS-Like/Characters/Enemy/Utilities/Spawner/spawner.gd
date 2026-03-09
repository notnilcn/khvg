extends Node2D
## spawner.gd
##
## Handles spawning of enemies AND players.
##
## ── ENEMY SPAWNING ────────────────────────────────────────────────────────────
## Only the server spawns enemies, but enemies need to EXIST on all peers so
## that clients can see them and weapon hit-detection works client-side.
##
## We solve this with Godot's MultiplayerSpawner node:
##   - You tell it which node to watch and which scenes are allowed.
##   - When the SERVER calls add_child() for a registered scene in that node,
##     MultiplayerSpawner automatically tells all connected clients to create
##     the same node with the same initialization data.
##   - When the server destroys a node (queue_free), clients auto-destroy theirs.
##
## This means we don't need manual "please spawn this enemy" RPCs — the
## MultiplayerSpawner handles it under the hood.
##
## ── PLAYER SPAWNING ───────────────────────────────────────────────────────────
## When a peer connects, the server broadcasts an RPC to create a Player node
## on ALL peers.  Each peer sets the correct multiplayer_authority on that node
## so only the owning peer processes input for it.

const DROP_SCENE:   PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const RES_FOOD:     ResDropFood = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/res_food.tres")
const ENEMY_SCENE:  PackedScene = preload("res://VS-Like/Characters/Enemy/enemy.tscn")
const PLAYER_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/player.tscn")

const MIN_SPAWN_RADIUS: int = 400
const MAX_ENEMIES:      int = 300

@export var all_res_enemy: Array[ResEnemy]
@onready var spawn_timer: Timer = $SpawnTimer

var enemies_count: int = 0 : set = _set_enemies_count
var minutes: int        : set = _set_minutes
var seconds: int        : set = _set_seconds

var _next_enemy_id:        int = 0
var _spawn_position_index: int = 0
var _initialized:          bool = false

const PLAYER_SPAWN_POSITIONS: Array = [
	Vector2(0, 0),
	Vector2(200, 0),
	Vector2(-200, 0),
	Vector2(0, 200),
]

## The MultiplayerSpawner that replicates enemy nodes to all clients.
## Created programmatically in _ready() so we don't need to modify the .tscn.
var _enemy_spawner: MultiplayerSpawner


func _ready() -> void:
	# Guard against _ready() being called a second time (e.g. scene reload quirk).
	if _initialized:
		return
	_initialized = true

	# ── set up MultiplayerSpawner for enemies ─────────────────────────────────
	# This node watches THIS spawner for add_child calls involving enemy.tscn.
	# When the server adds an enemy, all clients automatically get a copy.
	_enemy_spawner = MultiplayerSpawner.new()
	_enemy_spawner.name = "EnemySpawner"

	# spawn_path: which node's children to replicate. We use our own node.
	# NodePath(".") means "this node (the Spawner)".
	_enemy_spawner.spawn_path = NodePath(".")

	# Register enemy.tscn as a scene that MultiplayerSpawner is allowed to spawn.
	_enemy_spawner.add_spawnable_scene("res://VS-Like/Characters/Enemy/enemy.tscn")

	# spawn_function: a Callable that receives the data dict and returns a
	# configured Enemy node. Called on BOTH server and clients when spawning.
	_enemy_spawner.spawn_function = _create_enemy_from_data

	add_child(_enemy_spawner)

	# ── server setup ─────────────────────────────────────────────────────────
	if multiplayer.is_server():
		# Server spawns its own player now, while it's in the scene.
		# Clients will request their own spawn via RPC once THEY are in the scene.
		# This avoids a timing bug: if the server spawned the client's player on
		# peer_connected (which fires during the WebSocket handshake), the client's
		# scene wouldn't have loaded yet and the spawn RPC would be silently dropped.
		_spawn_player_for_peer(1)

		# Still need to know when peers disconnect so we can clean up their player.
		Network.player_left.connect(_on_player_left)
	else:
		# Client-side: tell the server we are in the scene and ready to be spawned.
		# rpc_id(1, ...) sends to peer 1 (the server) specifically.
		print("Spawner: client ready, requesting player spawn from server")
		_request_player_spawn.rpc_id(1)


# ── player spawning ───────────────────────────────────────────────────────────

## Client calls this on the server once the client's game scene is fully loaded.
## The server then spawns a player node for that client on all peers.
##
## @rpc("any_peer")  — clients (non-servers) need to call this.
## @rpc("call_remote") — runs only on the server, not on the calling client.
@rpc("any_peer", "call_remote", "reliable")
func _request_player_spawn() -> void:
	# This runs on the SERVER.
	var new_peer_id := multiplayer.get_remote_sender_id()
	print("Spawner: server received spawn request from peer ", new_peer_id)

	# ── catch-up: send all already-existing players to the new client ─────────
	# The host's player (and anyone else already in the game) was spawned
	# before this client connected, so the client missed those RPCs entirely.
	# We send them now, targeting only the new client via rpc_id.
	#
	# rpc_id(new_peer_id, ...) sends to ONE specific peer — does NOT run
	# locally on the server (no risk of duplicating nodes the server already has).
	for existing_peer_id: int in Global.players.keys():
		var existing_player: Player = Global.players[existing_peer_id]
		print("Spawner: sending existing player %d to new peer %d" % [existing_peer_id, new_peer_id])
		_create_player_node.rpc_id(new_peer_id, existing_peer_id, existing_player.global_position)

	# ── now spawn the new player for EVERYONE (broadcast) ─────────────────────
	_spawn_player_for_peer(new_peer_id)


func _on_player_left(peer_id: int) -> void:
	_remove_player.rpc(peer_id)


func _spawn_player_for_peer(peer_id: int) -> void:
	var pos: Vector2 = PLAYER_SPAWN_POSITIONS[
		_spawn_position_index % PLAYER_SPAWN_POSITIONS.size()
	]
	_spawn_position_index += 1
	print("Spawner: spawning player for peer ", peer_id, " at ", pos)
	_create_player_node.rpc(peer_id, pos)


## Runs on ALL peers. Creates the player node with the correct authority so
## only the owning peer processes input, while everyone can see the character.
@rpc("authority", "call_local", "reliable")
func _create_player_node(peer_id: int, spawn_pos: Vector2) -> void:
	# Guard: if this peer already has a node for this player, skip.
	# This can happen if the catch-up RPC and the broadcast RPC both arrive.
	if get_node_or_null("Player_%d" % peer_id) != null:
		return
	var player: Player = PLAYER_SCENE.instantiate()
	player.name                  = "Player_%d" % peer_id
	player.set_multiplayer_authority(peer_id)
	player.global_position       = spawn_pos
	add_child(player)


@rpc("authority", "call_local", "reliable")
func _remove_player(peer_id: int) -> void:
	var node := get_node_or_null("Player_%d" % peer_id)
	if node:
		node.queue_free()


# ── enemy spawning (SERVER ONLY) ──────────────────────────────────────────────

func _spawn_single_enemy(is_elite: bool = false) -> void:
	var ref_player := _get_reference_player()
	if ref_player == null:
		return
	var spawn_pos: Vector2 = _get_random_spawn_pos(ref_player.global_position, MIN_SPAWN_RADIUS)
	_create_enemy(spawn_pos, _get_enemy(), is_elite)


func _spawn_circle_pattern(enemies_to_spawn: int) -> void:
	var ref_player := _get_reference_player()
	if ref_player == null:
		return
	var player_pos: Vector2 = ref_player.global_position
	var angle_step: float   = TAU / enemies_to_spawn
	for i in range(enemies_to_spawn):
		var angle:     float   = i * angle_step
		var spawn_pos: Vector2 = player_pos + Vector2(cos(angle), sin(angle)) * MIN_SPAWN_RADIUS
		_create_enemy(spawn_pos, all_res_enemy[0])


## Spawn an enemy via MultiplayerSpawner.
## The spawner calls _create_enemy_from_data() on BOTH server and all clients,
## then adds the returned node as a child.  When the server later calls
## queue_free() on the enemy, all client copies are also removed automatically.
func _create_enemy(spawn_pos: Vector2, res_enemy: ResEnemy, is_elite: bool = false) -> void:
	var eid := _next_enemy_id
	_next_enemy_id += 1

	# Pack initialization data into a dictionary.
	# resource_path gives us a stable string to re-load the resource on clients.
	var data := {
		"res_path": res_enemy.resource_path,
		"pos":      spawn_pos,
		"is_elite": is_elite,
		"eid":      eid,
	}

	# spawn() adds the node to this spawner and replicates it to all clients.
	var enemy: Enemy = _enemy_spawner.spawn(data)

	# Connect the died signal so we can track the enemy count on the server.
	enemy.died.connect(enemy_died)
	enemies_count += 1


## Called by MultiplayerSpawner on EVERY peer to create and initialize an enemy.
## Must return a Node — the spawner adds it to the scene tree.
func _create_enemy_from_data(data: Dictionary) -> Node:
	var enemy: Enemy       = ENEMY_SCENE.instantiate()
	enemy.res_enemy        = load(data["res_path"]) as ResEnemy
	enemy.is_elite         = data["is_elite"]
	enemy.global_position  = data["pos"]
	enemy.enemy_id         = data["eid"]
	# Deterministic name = same on server and all clients, required for RPC routing.
	enemy.name             = "Enemy_%d" % data["eid"]
	return enemy


# ── helpers ───────────────────────────────────────────────────────────────────

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


# ── setters ───────────────────────────────────────────────────────────────────

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
		_spawn_circle_pattern(min(40, MAX_ENEMIES - enemies_count))


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
		ref_player.global_position, randi_range(MIN_SPAWN_RADIUS, 600)
	)
	var food: Drop       = DROP_SCENE.instantiate()
	food.resource        = RES_FOOD
	food.global_position = spawn_pos
	add_child(food)
