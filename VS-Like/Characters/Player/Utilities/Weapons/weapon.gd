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
func _fire_weapon():
	if !resource.is_passive:
		audio_player.pitch_scale = randf_range(0.95, 1.05)
		audio_player.play()
	fire.emit()

# Upgrade Weapon
func upgrade_weapon():
	upgrade_level += 1
	upgraded.emit()
