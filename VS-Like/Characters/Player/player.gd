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
@onready var camera_2d:      Camera2D     = $Camera2D
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

	# No MultiplayerSynchronizer here — we broadcast position via _sync_state RPC
	# in _physics_process instead.  Reason: MultiplayerSynchronizer only starts
	# sending to peers that are connected at the time the sync node is created.
	# A host player created before any client connects would never sync its position
	# to clients that join later.  Explicit RPCs have no such timing restriction.

	await get_tree().process_frame
	_init_player()


func _init_player() -> void:
	sprite_2d.texture = resource.sprite
	max_health         = resource.max_health
	speed              = resource.max_speed
	health             = max_health

	# Only the local player needs their HUD visible, and only the local player's
	# Camera2D should be active.  Every spawned player has a Camera2D child
	# (it's in the .tscn), so we must explicitly disable it for remote players —
	# otherwise the last-spawned player's camera becomes the active one.
	var is_local := is_multiplayer_authority()
	camera_2d.enabled            = is_local
	health_bar.visible           = is_local
	progress_bar.visible         = is_local
	weapons_container_ui.visible = is_local

	# Always create the starting weapon — on every machine, for every player.
	# This makes the swing animation visible to all players, not just the owner.
	#
	# is_owner_weapon = true  → fire timer fires normally; weapon reports hits;
	#                           fire event is broadcast via _rpc_fire to others.
	# is_owner_weapon = false → fire timer ticks but does nothing; animation is
	#                           triggered by _rpc_fire arriving from the owner.
	var weapon: Weapon    = resource.res_weapon.weapon_scene.instantiate()
	weapon.resource       = resource.res_weapon
	weapon.is_owner_weapon = is_local  # must be set BEFORE add_child triggers _ready
	if weapon.attach_to_rotation:
		sprite_2d.add_child(weapon)
	else:
		add_child(weapon)

	if is_local:
		weapons.append(weapon)
		weapons_container_ui.add_weapon(weapon)


# ── physics ───────────────────────────────────────────────────────────────────
func _physics_process(_delta: float) -> void:
	# IMPORTANT: only the authoritative peer (the player's owner) processes
	# keyboard input and broadcasts its state.  On all other machines this
	# function returns early — state arrives via the _sync_state RPC below.
	if not is_multiplayer_authority():
		return

	_handle_movement()
	move_and_slide()

	# Broadcast our world position and sprite direction to every other peer.
	# "unreliable_ordered" means packets can be dropped (fine for positions —
	# the next packet will correct it) and out-of-order packets are discarded
	# (we only care about the most recent position, not old ones).
	_sync_state.rpc(global_position, sprite_2d.scale.x)


## Receive a position broadcast from the peer that owns this player node.
##
## @rpc("any_peer")    — any connected peer can broadcast its own state.
## @rpc("call_remote") — does NOT run on the sender itself (they already know
##                       their own position from local physics).
## @rpc("unreliable_ordered") — low-latency, latest packet wins.
@rpc("any_peer", "call_remote", "unreliable_ordered")
func _sync_state(pos: Vector2, scale_x: float) -> void:
	# Security: ignore packets whose sender is not this node's owner.
	# e.g. a malicious client cannot puppet another player's position.
	if multiplayer.get_remote_sender_id() != get_multiplayer_authority():
		return
	global_position  = pos
	sprite_2d.scale.x = scale_x


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


# ── chat command RPCs ─────────────────────────────────────────────────────────
## Called by the server (via rpc_id) to give this player a weapon.
## Only executes on the peer that owns this player node.
@rpc("any_peer", "call_remote", "reliable")
func rpc_give_weapon(res_path: String) -> void:
	if multiplayer.get_remote_sender_id() not in [0, 1]:
		return  # reject calls from clients
	if not is_multiplayer_authority():
		return  # only the owning peer applies this
	var res := load(res_path) as ResWeapon
	if res:
		Global.add_weapon(res)


## Called by the server (via rpc_id) to remove a weapon from this player.
## Only executes on the peer that owns this player node.
@rpc("any_peer", "call_remote", "reliable")
func rpc_remove_weapon(res_path: String) -> void:
	if multiplayer.get_remote_sender_id() not in [0, 1]:
		return  # reject calls from clients
	if not is_multiplayer_authority():
		return  # only the owning peer applies this
	for i in range(weapons.size()):
		var wpn := weapons[i]
		if wpn.resource.resource_path == res_path:
			wpn.remove_passive_effects(self)
			weapons_container_ui.remove_weapon(wpn)
			weapons.remove_at(i)
			wpn.queue_free()
			return


# ── cleanup ───────────────────────────────────────────────────────────────────
func _exit_tree() -> void:
	# Remove from global registry when the node is destroyed.
	var my_peer_id := get_multiplayer_authority()
	Global.players.erase(my_peer_id)
