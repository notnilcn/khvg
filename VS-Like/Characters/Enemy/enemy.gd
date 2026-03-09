extends Sprite2D
class_name Enemy

## enemy.gd
##
## Enemy movement and state are SERVER-ONLY.  The server runs all the physics
## and AI; clients just display whatever position the server sends them
## (via a MultiplayerSynchronizer added in _ready).
##
## HIT DETECTION is CLIENT-AUTHORITATIVE:
##   1. A client's weapon Area2D detects an overlap with this enemy locally.
##   2. The client calls  report_hit.rpc_id(1, damage)  — sends a message to
##      the SERVER (peer_id 1 is always the server) saying "I hit this enemy".
##   3. The server runs two cheap sanity checks:
##        a) Is the damage value in a plausible range? (not 1 000 000)
##        b) Was the caller's player within weapon range of this enemy?
##   4. If checks pass, the server applies damage and the enemy dies if needed.
##
## This removes all round-trip latency from the shooter's perspective — your
## hits always feel instant.  The server just validates that you didn't cheat.

signal died

# ── preloads ──────────────────────────────────────────────────────────────────
const DROP_SCENE:         PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const FLOATING_TEXT_SCENE: PackedScene = preload("res://VS-Like/Game/UI/FloatingText/floating_text.tscn")

# ── references ────────────────────────────────────────────────────────────────
@onready var audio_player_2d: AudioStreamPlayer2D = $AudioPlayer2D

# ── physics server handles ────────────────────────────────────────────────────
# Enemies use the low-level PhysicsServer2D API directly (instead of
# CharacterBody2D) because it's faster when you have 300 of them at once.
var object: RID   # the physics body handle
var shape         # the collision shape handle

# ── data ──────────────────────────────────────────────────────────────────────
var res_enemy:  ResEnemy
var curr_health: float
var speed:       int
var damage:      float
var res_drop:    ResDrop
var is_elite:    bool = false

## Unique id assigned by the Spawner. Used by clients to reference this enemy
## when reporting hits (clients say "I hit enemy 42 for 18 damage").
var enemy_id: int = -1

# ── movement ──────────────────────────────────────────────────────────────────
var direction: Vector2
# `target` is whichever player this enemy is currently chasing.
# On the server this is a live Player node; on clients it's irrelevant
# because movement doesn't run client-side.
var target: Node2D


# ── ready ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	# Set up the low-level physics body.
	var physics_server := PhysicsServer2D
	object = physics_server.body_create()

	shape = PhysicsServer2D.circle_shape_create()
	PhysicsServer2D.shape_set_data(shape, res_enemy.radius)
	PhysicsServer2D.body_add_shape(object, shape)
	physics_server.body_set_space(object, get_world_2d().space)

	var trans := Transform2D(0, global_position)
	physics_server.body_set_state(object, PhysicsServer2D.BODY_STATE_TRANSFORM, trans)
	physics_server.body_set_mode(object, PhysicsServer2D.BODY_MODE_RIGID)
	physics_server.body_set_param(object, PhysicsServer2D.BODY_PARAM_GRAVITY_SCALE, 0.0)
	# Layer 3 = Enemy.  Mask 1+3 = collides with World and other Enemies.
	physics_server.body_set_collision_layer(object, 0b0100)
	physics_server.body_set_collision_mask(object, 0b0101)

	# This one-frame wait is required so that Area2D nodes on weapons and the
	# player hurtbox can detect this physics body via get_overlapping_bodies().
	await get_tree().process_frame
	PhysicsServer2D.body_attach_object_instance_id(object, get_instance_id())

	# Apply the Custom Resource data.
	texture = res_enemy.sprite
	if is_elite:
		curr_health = res_enemy.health * 10
		material    = load("res://VS-Like/Characters/Enemy/Utilities/Shader/material_elite.tres")
		res_drop    = load("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Chest/res_chest.tres")
	else:
		curr_health = res_enemy.health
		res_drop    = load("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Point/res_point.tres")

	speed  = res_enemy.speed
	damage = res_enemy.damage

	# Pick the nearest player as initial target (only matters on server).
	target = _get_nearest_player()

	# Add a MultiplayerSynchronizer so the server's position for this enemy
	# is automatically broadcast to all clients each network tick.
	_setup_sync()


## Add a synchronizer that streams this enemy's position from server→clients.
func _setup_sync() -> void:
	var sync   := MultiplayerSynchronizer.new()
	var config := SceneReplicationConfig.new()
	config.add_property(NodePath(":position"))
	sync.replication_config = config
	add_child(sync)


# ── process ───────────────────────────────────────────────────────────────────
func _process(_delta: float) -> void:
	# Enemy AI and physics only run on the server.
	# Clients receive position updates via MultiplayerSynchronizer (above).
	if not multiplayer.is_server():
		return

	_move_character()

	# Sync our Sprite2D position from the physics body.
	var new_trans: Transform2D = PhysicsServer2D.body_get_state(
		object, PhysicsServer2D.BODY_STATE_TRANSFORM
	)
	global_position = new_trans.origin

	# Refresh target in case a nearer player appeared.
	target = _get_nearest_player()

	# Despawn if too far from every player (prevents infinite map growth).
	if _distance_to_nearest_player() >= 600:
		queue_free()


func _move_character() -> void:
	if target:
		direction = global_position.direction_to(target.global_position).normalized()
		PhysicsServer2D.body_set_state(
			object, PhysicsServer2D.BODY_STATE_LINEAR_VELOCITY, direction * speed
		)


# ── hit detection (CLIENT-AUTHORITATIVE) ─────────────────────────────────────
## Clients call this via RPC when one of their weapons hits this enemy locally.
##
## @rpc arguments:
##   "any_peer"   — any connected peer (not just the server) can call this.
##                  We need clients to be able to send hits to the server.
##   "call_remote" — only runs on the RECEIVER (the server), not the caller.
##                   The client's local hurt() is handled via show_floating_text
##                   called directly for immediate visual feedback (see weapons).
##   "reliable"   — guaranteed delivery; important so kills don't get lost.
@rpc("any_peer", "call_remote", "reliable")
func report_hit(damage_amount: float) -> void:
	# This function body runs ONLY on the server (because call_remote +
	# the client called rpc_id(1, ...) targeting the server).

	# ── sanity check 1: is the damage value plausible? ──────────────────────
	# The client sends the damage value it calculated locally.  We trust it
	# for normal values, but cap wildly impossible numbers.
	# Max realistic damage = base weapon damage × extra_damage multiplier.
	# 500 is a very generous ceiling that no weapon combo can naturally reach.
	if damage_amount <= 0 or damage_amount > 500:
		return

	# ── sanity check 2: was the caller's player close enough? ───────────────
	# Get the peer_id of whoever sent this RPC.
	var caller_id := multiplayer.get_remote_sender_id()
	var caller_player: Player = Global.players.get(caller_id, null)
	if caller_player == null:
		return  # Sender has no player node — ignore.

	var dist := global_position.distance_to(caller_player.global_position)
	if dist > Network.HIT_VALIDATION_RANGE:
		# Enemy is too far from the player for any weapon to reach.
		# The generous range (350 units) accommodates normal network lag;
		# only truly impossible hits get rejected.
		return

	# ── checks passed — apply damage ─────────────────────────────────────────
	hurt(damage_amount)


# ── internal damage application (server-only) ─────────────────────────────────
func hurt(dmg: float) -> void:
	curr_health -= dmg
	audio_player_2d.play()

	# Show floating damage text.  This runs on the server (which is also a
	# player), and we also send it to clients so they see the numbers too.
	_show_floating_text_all(round(dmg))

	if curr_health <= 0:
		die()


## Show floating damage text on ALL peers (server + all clients).
## We use an RPC call to trigger it everywhere simultaneously.
@rpc("authority", "call_local", "reliable")
func _show_floating_text_all(dmg: int) -> void:
	var floating_text: FloatingText = FLOATING_TEXT_SCENE.instantiate()
	floating_text.global_position  = global_position
	floating_text.floating_number  = dmg
	get_tree().current_scene.call_deferred("add_child", floating_text)


func die() -> void:
	if is_elite:
		spawn_drop()
	else:
		if randf() > 0.25:
			spawn_drop()
	queue_free()


func spawn_drop() -> void:
	var drop: Drop = DROP_SCENE.instantiate()
	drop.resource        = res_drop
	drop.global_position = global_position
	get_tree().current_scene.call_deferred("add_child", drop)


# ── helpers ───────────────────────────────────────────────────────────────────
## Returns the nearest Player node across all connected players.
func _get_nearest_player() -> Player:
	var nearest:      Player = null
	var nearest_dist: float  = INF

	for player in Global.players.values():
		var dist := global_position.distance_to(player.global_position)
		if dist < nearest_dist:
			nearest_dist = dist
			nearest      = player

	return nearest


func _distance_to_nearest_player() -> float:
	var min_dist: float = INF
	for player in Global.players.values():
		var d := global_position.distance_to(player.global_position)
		if d < min_dist:
			min_dist = d
	return min_dist


# ── cleanup ───────────────────────────────────────────────────────────────────
func _exit_tree() -> void:
	died.emit()
	PhysicsServer2D.free_rid(object)
	PhysicsServer2D.free_rid(shape)
