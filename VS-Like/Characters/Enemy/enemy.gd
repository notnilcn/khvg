extends Sprite2D
class_name Enemy

## enemy.gd
##
## Each enemy node exists on EVERY peer's machine (the server and all clients).
## But only the server runs the AI and physics — clients just display the enemy
## at whatever position the server tells them via MultiplayerSynchronizer.
##
## HIT DETECTION is CLIENT-AUTHORITATIVE:
##   1. Client weapon detects overlap locally → reports via RPC to server.
##   2. Server validates (range + damage sanity) → applies damage.
##   3. Server kills enemy if health <= 0 → MultiplayerSpawner removes it
##      from all clients automatically.

signal died

const DROP_SCENE:          PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const FLOATING_TEXT_SCENE: PackedScene = preload("res://VS-Like/Game/UI/FloatingText/floating_text.tscn")

@onready var audio_player_2d: AudioStreamPlayer2D = $AudioPlayer2D

# ── low-level physics handles (SERVER ONLY) ───────────────────────────────────
# These are only created on the server. On clients they stay at their zero-value
# (RID() is invalid) and are never touched.
var object: RID
var shape:  RID

# ── data ──────────────────────────────────────────────────────────────────────
var res_enemy:   ResEnemy
var curr_health: float
var speed:       int
var damage:      float
var res_drop:    ResDrop
var is_elite:    bool = false
var enemy_id:    int  = -1

# ── movement (SERVER ONLY) ────────────────────────────────────────────────────
var direction: Vector2
var target:    Node2D


func _ready() -> void:
	# ── visual setup (runs on ALL peers) ─────────────────────────────────────
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

	# ── position sync (runs on ALL peers) ────────────────────────────────────
	# MultiplayerSynchronizer streams server's position to all clients.
	# Now that enemies exist on clients (via MultiplayerSpawner in spawner.gd),
	# this actually has nodes to sync between.
	_setup_sync()

	# ── physics setup (SERVER ONLY) ──────────────────────────────────────────
	# Clients don't simulate physics — they just follow synced positions.
	if not multiplayer.is_server():
		return

	_setup_physics()
	target = _get_nearest_player()


func _setup_sync() -> void:
	var sync   := MultiplayerSynchronizer.new()
	var config := SceneReplicationConfig.new()
	config.add_property(NodePath(":position"))
	sync.replication_config = config
	add_child(sync)


func _setup_physics() -> void:
	# Low-level physics API — faster than CharacterBody2D for 300 enemies.
	object = PhysicsServer2D.body_create()
	shape  = PhysicsServer2D.circle_shape_create()

	PhysicsServer2D.shape_set_data(shape, res_enemy.radius)
	PhysicsServer2D.body_add_shape(object, shape)
	PhysicsServer2D.body_set_space(object, get_world_2d().space)
	PhysicsServer2D.body_set_state(
		object, PhysicsServer2D.BODY_STATE_TRANSFORM, Transform2D(0, global_position)
	)
	PhysicsServer2D.body_set_mode(object, PhysicsServer2D.BODY_MODE_RIGID)
	PhysicsServer2D.body_set_param(object, PhysicsServer2D.BODY_PARAM_GRAVITY_SCALE, 0.0)
	# Layer 3 = Enemy.  Mask bits 1+3 = collides with World and other Enemies.
	PhysicsServer2D.body_set_collision_layer(object, 0b0100)
	PhysicsServer2D.body_set_collision_mask(object, 0b0101)

	# This one-frame wait lets Area2D nodes (weapons, hurtbox) detect this body.
	await get_tree().process_frame
	PhysicsServer2D.body_attach_object_instance_id(object, get_instance_id())


func _process(_delta: float) -> void:
	# Enemy AI and physics update only on the server.
	# Clients receive updated positions via the MultiplayerSynchronizer.
	if not multiplayer.is_server():
		return

	_move_character()

	# Read back the position the physics server calculated and apply it to
	# the Sprite2D node (so the MultiplayerSynchronizer picks it up).
	var new_trans: Transform2D = PhysicsServer2D.body_get_state(
		object, PhysicsServer2D.BODY_STATE_TRANSFORM
	)
	global_position = new_trans.origin

	# Refresh nearest-player target each frame.
	target = _get_nearest_player()

	# Despawn if no player is nearby (prevents runaway world growth).
	if _distance_to_nearest_player() >= 600:
		queue_free()


func _move_character() -> void:
	if target:
		direction = global_position.direction_to(target.global_position).normalized()
		PhysicsServer2D.body_set_state(
			object, PhysicsServer2D.BODY_STATE_LINEAR_VELOCITY, direction * speed
		)


# ── hit detection (CLIENT-AUTHORITATIVE) ─────────────────────────────────────
## Called by clients when one of their weapons overlaps this enemy locally.
##
## @rpc("any_peer")  — any connected peer can send this to the server.
## @rpc("call_remote") — runs only on the receiver (the server), not the sender.
##   The sending client's immediate visual feedback (floating text) is already
##   triggered directly in the weapon script before this RPC is even sent.
@rpc("any_peer", "call_remote", "reliable")
func report_hit(damage_amount: float) -> void:
	# This body only runs on the server.

	# ── sanity check 1: plausible damage? ────────────────────────────────────
	if damage_amount <= 0 or damage_amount > 500:
		return

	# ── sanity check 2: was the caller close enough? ─────────────────────────
	var caller_id     := multiplayer.get_remote_sender_id()
	var caller_player := Global.players.get(caller_id, null) as Player
	if caller_player == null:
		return

	# HIT_VALIDATION_RANGE is generous — it accommodates normal network lag.
	# Only truly impossible hits (teleporting, speedhacking) get rejected.
	if global_position.distance_to(caller_player.global_position) > Network.HIT_VALIDATION_RANGE:
		return

	# ── apply damage ─────────────────────────────────────────────────────────
	hurt(damage_amount)


func hurt(dmg: float) -> void:
	# Runs on server only (called directly, not via RPC).
	curr_health -= dmg
	audio_player_2d.play()

	if curr_health <= 0:
		die()


## Show floating damage text on all peers.
## "any_peer" so weapon scripts on clients can trigger this immediately
## for zero-latency hit feedback — no need to wait for server confirmation.
@rpc("any_peer", "call_local", "reliable")
func show_floating_text(dmg: int) -> void:
	var floating_text: FloatingText = FLOATING_TEXT_SCENE.instantiate()
	floating_text.global_position  = global_position
	floating_text.floating_number  = dmg
	get_tree().current_scene.call_deferred("add_child", floating_text)


func die() -> void:
	# Drop spawning is server-only. The server removes the enemy node via
	# queue_free(), and MultiplayerSpawner automatically removes it from all
	# clients — no need for an explicit "die" RPC.
	if is_elite:
		spawn_drop()
	elif randf() > 0.25:
		spawn_drop()

	queue_free()


func spawn_drop() -> void:
	var drop: Drop       = DROP_SCENE.instantiate()
	drop.resource        = res_drop
	drop.global_position = global_position
	get_tree().current_scene.call_deferred("add_child", drop)


# ── helpers ───────────────────────────────────────────────────────────────────
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


func _exit_tree() -> void:
	died.emit()
	# Only free physics RIDs if they were created (server only).
	if object.is_valid():
		PhysicsServer2D.free_rid(object)
	if shape.is_valid():
		PhysicsServer2D.free_rid(shape)
