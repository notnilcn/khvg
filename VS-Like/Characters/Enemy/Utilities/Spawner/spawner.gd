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

# Tracks spawn-data for every live enemy on the SERVER.
# Used to send a catch-up list to clients that join after enemies already exist.
# Entries are removed when the enemy is despawned via _rpc_despawn_enemy.
var _active_enemy_data: Array[Dictionary] = []

const PLAYER_SPAWN_POSITIONS: Array = [
	Vector2(0, 0),
	Vector2(200, 0),
	Vector2(-200, 0),
	Vector2(0, 200),
]


func _ready() -> void:
	# Guard against _ready() being called a second time (e.g. scene reload quirk).
	if _initialized:
		return
	_initialized = true

	# ── server setup ─────────────────────────────────────────────────────────
	if multiplayer.is_server():
		# Spawn the host's own player now that the scene is loaded.
		# Clients request their spawn via RPC (see the else-branch below).
		_spawn_player_for_peer(1)

		# If any clients were ALREADY connected before this _ready() ran
		# (happens when the host dies and the scene reloads while a client
		# is still in the game), notify those clients to clean up stale nodes
		# and re-join the fresh scene.
		for peer_id in multiplayer.get_peers():
			_notify_scene_reload.rpc_id(peer_id)

		# Clean up player nodes when a peer disconnects.
		Network.player_left.connect(_on_player_left)
	else:
		# Client-side: tell the server we are in the scene and ready to be spawned.
		# rpc_id(1, ...) sends ONLY to peer 1 (the server).
		print("Spawner: client ready, requesting player spawn from server")
		_request_player_spawn.rpc_id(1)


# ── scene reload notification ─────────────────────────────────────────────────

## Sent by the server to already-connected clients when the server scene reloads
## (e.g. the host died and respawned).  The client removes all stale player nodes
## from the previous game session and re-requests a fresh spawn.
##
## @rpc("authority")    — only the server sends this.
## @rpc("call_remote")  — only clients run it (server handles its own state).
@rpc("authority", "call_remote", "reliable")
func _notify_scene_reload() -> void:
	print("Spawner: server scene reloaded — clearing stale nodes and rejoining")

	# Remove every player node we were tracking from the old session.
	for peer_id: int in Global.players.keys():
		var player: Player = Global.players[peer_id]
		if is_instance_valid(player):
			player.queue_free()
	Global.players.clear()

	# Also remove any enemy nodes that still exist under this spawner from the
	# previous session (they're now orphaned on the client's side).
	for child in get_children():
		if child is Enemy:
			child.queue_free()

	# Re-join the server's fresh game session.
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
	for existing_peer_id: int in Global.players.keys():
		var existing_player: Player = Global.players[existing_peer_id]
		print("Spawner: sending existing player %d to new peer %d" % [existing_peer_id, new_peer_id])
		_create_player_node.rpc_id(new_peer_id, existing_peer_id, existing_player.global_position)

	# ── now spawn the new player for EVERYONE (broadcast) ─────────────────────
	_spawn_player_for_peer(new_peer_id)

	# ── catch-up: send all currently-alive enemies to the new client ──────────
	# MultiplayerSpawner's built-in catch-up fires at peer_connected time —
	# BEFORE the client's game scene is loaded, so those packets are dropped.
	# We solve this by tracking enemy data ourselves and manually replaying it
	# when we know the client's scene is ready (i.e. right now).
	print("Spawner: sending %d existing enemies to peer %d" % [_active_enemy_data.size(), new_peer_id])
	for data: Dictionary in _active_enemy_data:
		_rpc_spawn_enemy.rpc_id(new_peer_id, data)


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


# ── enemy spawning ────────────────────────────────────────────────────────────

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


## SERVER ONLY: spawn an enemy and replicate it to all connected clients.
##
## We no longer use MultiplayerSpawner because its built-in late-join catch-up
## fires at peer_connected time — before the client's game scene exists — so
## the packets are silently dropped.  Instead we use an explicit RPC that only
## fires when we KNOW the client is ready (see _request_player_spawn).
func _create_enemy(spawn_pos: Vector2, res_enemy: ResEnemy, is_elite: bool = false) -> void:
	var eid := _next_enemy_id
	_next_enemy_id += 1

	var data := {
		"res_path": res_enemy.resource_path,
		"pos":      spawn_pos,
		"is_elite": is_elite,
		"eid":      eid,
	}

	# Track for late-join catch-up (sent inside _request_player_spawn).
	_active_enemy_data.append(data)

	# Broadcast to server (call_local) and all currently-connected clients.
	_rpc_spawn_enemy.rpc(data)


## Runs on ALL peers (server via call_local, clients via network).
## Creates, names, and adds the enemy node to this spawner.
##
## @rpc("authority")   — only the server sends this.
## @rpc("call_local")  — also runs on the server immediately (no extra step needed).
## @rpc("reliable")    — enemy appearance must not be lost.
@rpc("authority", "call_local", "reliable")
func _rpc_spawn_enemy(data: Dictionary) -> void:
	var enemy: Enemy       = ENEMY_SCENE.instantiate()
	enemy.res_enemy        = load(data["res_path"]) as ResEnemy
	enemy.is_elite         = data["is_elite"]
	enemy.global_position  = data["pos"]
	enemy.enemy_id         = data["eid"]
	# Deterministic name required so _rpc_despawn_enemy can find it by name on
	# all peers, and so the MultiplayerSynchronizer path is the same everywhere.
	enemy.name             = "Enemy_%d" % data["eid"]

	add_child(enemy)

	# Server-only bookkeeping: count enemies and wire up the despawn callback.
	if multiplayer.is_server():
		enemies_count += 1
		enemy.died.connect(enemy_died)
		# When the enemy calls die(), it emits request_despawn so we can
		# broadcast the removal to all peers (see _rpc_despawn_enemy below).
		enemy.request_despawn.connect(_on_enemy_request_despawn)


## Called on the server when an enemy's die() method fires.
## Broadcasts removal to ALL peers so every machine frees the node.
func _on_enemy_request_despawn(eid: int) -> void:
	_rpc_despawn_enemy.rpc(eid)


## SERVER ONLY helper called by enemy.spawn_drop().
## Broadcasts a drop spawn to ALL peers so every player can collect it
## independently on their own machine (each gets XP / heal / chest).
##
## Drops are lightweight Area2D nodes — having one copy per client is fine.
## Each peer's copy is collected independently and freed locally on pickup.
@rpc("authority", "call_local", "reliable")
func _rpc_spawn_drop(res_path: String, pos: Vector2) -> void:
	var drop: Drop       = DROP_SCENE.instantiate()
	drop.resource        = load(res_path) as ResDrop
	drop.global_position = pos
	# Add to the map scene root (same parent as the original single-player code).
	get_tree().current_scene.call_deferred("add_child", drop)


## Runs on ALL peers (server via call_local, clients via network).
## Finds the enemy node by name and frees it.
##
## @rpc("authority")   — only the server sends this.
## @rpc("call_local")  — also frees it on the server.
## @rpc("reliable")    — enemy removal must not be lost.
@rpc("authority", "call_local", "reliable")
func _rpc_despawn_enemy(eid: int) -> void:
	if multiplayer.is_server():
		# Remove from the catch-up list so late-joining clients don't get a
		# ghost entry for an enemy that's already dead.
		_active_enemy_data = _active_enemy_data.filter(
			func(d: Dictionary) -> bool: return d["eid"] != eid
		)

	var enemy := get_node_or_null("Enemy_%d" % eid)
	if enemy:
		enemy.queue_free()


## Creates and configures an enemy from spawn data.  Called from _rpc_spawn_enemy.
## Kept as a separate helper so _request_player_spawn can reuse the same logic.
## (Previously this was the MultiplayerSpawner's spawn_function callback.)
func _create_enemy_from_data(data: Dictionary) -> Enemy:
	var enemy: Enemy       = ENEMY_SCENE.instantiate()
	enemy.res_enemy        = load(data["res_path"]) as ResEnemy
	enemy.is_elite         = data["is_elite"]
	enemy.global_position  = data["pos"]
	enemy.enemy_id         = data["eid"]
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
