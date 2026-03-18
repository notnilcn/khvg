extends Node2D
class_name Weapon

signal fire
signal upgraded

# Data
var resource: ResWeapon : set = _set_resource
var upgrade_level: int = 1

# Stats
var damage: float
var fire_rate: float
var attach_to_rotation: bool
var fire_timer: Timer
var audio: AudioStreamWAV
var audio_player: AudioStreamPlayer

## true  → this weapon belongs to the LOCAL player on this machine.
##          The fire timer fires normally, hit detection runs, _rpc_fire is sent.
## false → this weapon is a visual copy of a REMOTE player's weapon.
##          The timer fires but does nothing; animation is triggered by _rpc_fire.
##
## Set this BEFORE add_child() so _ready() sees the correct value.
var is_owner_weapon: bool = false


## READY - Add fire timer to control fire rate
func _ready() -> void:
	fire_timer = Timer.new()
	fire_timer.autostart = true
	fire_timer.wait_time = resource.fire_rate
	fire_timer.timeout.connect(_fire_weapon)
	add_child(fire_timer)

	# If it is passive dont attack add audio
	if !resource.is_passive:
		audio_player = AudioStreamPlayer.new()
		audio_player.stream = resource.audio
		add_child(audio_player)

# Set Custom Resource
func _set_resource(res: ResWeapon) -> void:
	resource = res
	damage = resource.damage
	fire_rate = resource.fire_rate
	attach_to_rotation = resource.attach_to_rotation
	# If it is passive dont add audio
	if !resource.is_passive:
		audio = resource.audio

# Fire Weapon using Fire Timer
func _fire_weapon() -> void:
	# Non-owner weapons sit idle — their animation is triggered by the
	# _rpc_fire broadcast from the machine that owns this weapon.
	if not is_owner_weapon:
		return

	if not resource.is_passive:
		audio_player.pitch_scale = randf_range(0.95, 1.05)
		audio_player.play()

	fire.emit()

	# Broadcast to all OTHER peers so they can show the swing animation on their
	# copy of this weapon node.  "call_remote" means the sender is skipped —
	# it already emitted fire locally above.
	if multiplayer.has_multiplayer_peer():
		_rpc_fire.rpc()


## Received on non-owner peers when the weapon's owner fires.
## Emits fire locally so the weapon's _on_fire() animation callback runs.
##
## @rpc("any_peer")   — broadcaster is whoever owns this weapon (not always server).
## @rpc("call_remote") — only non-senders run this (sender already fired locally).
## @rpc("reliable")   — weapon swings must not disappear due to packet loss.
@rpc("any_peer", "call_remote", "reliable")
func _rpc_fire() -> void:
	fire.emit()

# Upgrade Weapon
func upgrade_weapon():
	upgrade_level += 1
	upgraded.emit()


## Called before this weapon is removed from a player.
## Override in passive weapon subclasses to reverse any stat bonuses that were
## applied in _ready() / _on_upgraded() so the player's stats stay accurate.
func remove_passive_effects(_player: Player) -> void:
	pass
