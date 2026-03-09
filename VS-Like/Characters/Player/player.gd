extends CharacterBody2D
class_name Player

## player.gd
##
## In single-player this node ran input and physics for the one local player.
## In multiplayer EVERY peer spawns a copy of this node for EVERY player in the
## game — but each copy only processes INPUT for the player it "owns".
##
## Ownership is controlled by `multiplayer_authority`.  The server sets:
##   player_node.set_multiplayer_authority(peer_id)
## so peer 42's player only processes WASD on peer 42's machine.
## Everyone else just displays that player's replicated position.
##
## The MultiplayerSynchronizer node (added below in _ready) is what actually
## sends position updates from the authoritative peer to all other peers.

# ── exports / references ─────────────────────────────────────────────────────
@export var resource: ResPlayer

@onready var sprite_2d:      Sprite2D     = $Sprite2D
@onready var enemy_detection: Area2D      = $EnemyDetection

# HUD — only shown / updated on THIS machine's local player
@onready var health_bar:           Healthbar         = $HealthBar
@onready var progress_bar:         ProgressBar       = %ProgressBar
@onready var weapons_container_ui: WeaponContainerUI = %WeaponsContainerUI
@onready var weapon_selection:     WeaponSelectionUI = %WeaponSelection
@onready var chest_ui:             ChestUI           = %ChestUI

# ── data ──────────────────────────────────────────────────────────────────────
var max_health: float
var speed:      float
var health:     float : set = _set_health
var points:     float = 0.0 : set = _set_points
var weapons:    Array[Weapon] = []

# Multipliers granted by passive weapons (Boots / Parchment / Ring)
var extra_exp:    float = 1.0
var extra_damage: float = 1.0
var extra_speed:  float = 1.0


# ── ready ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	# Register this node in the global players dictionary so any script can
	# find it via  Global.players[peer_id].
	# `get_multiplayer_authority()` returns the peer_id the server assigned us.
	var my_peer_id := get_multiplayer_authority()
	Global.players[my_peer_id] = self

	# Add a MultiplayerSynchronizer child so Godot automatically replicates
	# this player's position (and sprite scale for the flip animation) to all
	# peers every network tick.
	#
	# How it works:
	#   - The peer that IS the authority sends position updates.
	#   - All other peers receive them and update this node's position silently.
	#   - We don't have to write any send/receive code — Godot handles it.
	_setup_sync()

	await get_tree().process_frame
	_init_player()


func _init_player() -> void:
	sprite_2d.texture = resource.sprite
	max_health         = resource.max_health
	speed              = resource.max_speed
	health             = max_health

	# Only the local player needs their HUD visible.
	# Other players' HUD nodes exist in the scene but we hide them.
	var is_local := is_multiplayer_authority()
	health_bar.visible           = is_local
	progress_bar.visible         = is_local
	weapons_container_ui.visible = is_local

	if is_local:
		# Give the local player their starting weapon.
		var weapon: Weapon = resource.res_weapon.weapon_scene.instantiate()
		weapon.resource = resource.res_weapon
		if weapon.attach_to_rotation:
			sprite_2d.add_child(weapon)
		else:
			add_child(weapon)
		weapons.append(weapon)
		weapons_container_ui.add_weapon(weapon)


## Adds a MultiplayerSynchronizer that keeps position + sprite flip in sync
## across all peers without us writing any manual send/receive code.
func _setup_sync() -> void:
	var sync   := MultiplayerSynchronizer.new()
	var config := SceneReplicationConfig.new()

	# Sync the player's world position (CharacterBody2D.position).
	config.add_property(NodePath(":position"))

	# Sync the sprite's horizontal scale so the flip-left/right animation
	# is visible to other players too.
	config.add_property(NodePath("Sprite2D:scale"))

	sync.replication_config = config

	# CRITICAL: tell the synchronizer which peer is the authority (sender).
	# Without this line it defaults to authority=1 (the server), meaning only
	# the server ever broadcasts position — even for players owned by clients.
	# Each player's sync node must mirror that player's multiplayer_authority.
	sync.set_multiplayer_authority(get_multiplayer_authority())

	# By default MultiplayerSynchronizer replicates 60 times per second.
	# For a fast-moving game you may want to lower this to reduce bandwidth.
	# sync.replication_interval = 1.0 / 20.0  # 20 Hz example

	add_child(sync)


# ── physics ───────────────────────────────────────────────────────────────────
func _physics_process(_delta: float) -> void:
	# IMPORTANT: only the authoritative peer (the player's owner) processes
	# keyboard input.  On everyone else's machine this player's position is
	# silently updated by the MultiplayerSynchronizer above — no input needed.
	if not is_multiplayer_authority():
		return

	_handle_movement()
	move_and_slide()


func _handle_movement() -> void:
	var movement_input := Input.get_vector("Left", "Right", "Up", "Down").normalized()
	velocity = movement_input * (speed * extra_speed)

	if velocity.x > 0:
		sprite_2d.scale.x = 1
	elif velocity.x < 0:
		sprite_2d.scale.x = -1


# ── health ────────────────────────────────────────────────────────────────────
## Called by the server to deal damage to this player.
##
## @rpc arguments:
##   "any_peer"   — we need this (not "authority") because the SERVER calls
##                  hurt() on player nodes it doesn't own.  "authority" would
##                  only allow the owning client to call it — backwards from
##                  what we want.
##   "call_local" — also run on the caller (the server), which matters when
##                  the server's own player takes damage.
##   "reliable"   — guaranteed delivery; important for damage events.
@rpc("any_peer", "call_local", "reliable")
func hurt(dmg: float) -> void:
	# Security: only accept damage calls that originated from the server.
	# get_remote_sender_id() returns 0 when the function is called locally
	# (i.e. the server calling hurt() on its own machine), and 1 when a
	# remote peer sends it.  We reject anything from peer_id > 1 (clients).
	var sender := multiplayer.get_remote_sender_id()
	if sender != 0 and sender != 1:
		return  # A client tried to damage a player directly — ignore it.

	health -= dmg

	# Only the owning client reloads the scene on death.
	# Everyone's copy of this player node runs hurt(), but only the machine
	# that actually controls this character needs to react to death.
	if health <= 0 and is_multiplayer_authority():
		get_tree().call_deferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn")


func _set_health(new_health: float) -> void:
	health = clamp(new_health, 0, max_health)
	# Guard: health_bar may not exist yet during _init_player on remote copies.
	if health_bar and is_multiplayer_authority():
		health_bar.value = health


# ── experience / levelling ────────────────────────────────────────────────────
func _set_points(new_points: float) -> void:
	if not is_multiplayer_authority():
		return

	if new_points >= progress_bar.max_value:
		points = 0
		progress_bar.max_value += 100
		weapon_selection.show_selection()
	else:
		points = new_points

	progress_bar.value = points


# ── cleanup ───────────────────────────────────────────────────────────────────
func _exit_tree() -> void:
	# Remove from global registry when the node is destroyed.
	var my_peer_id := get_multiplayer_authority()
	Global.players.erase(my_peer_id)
