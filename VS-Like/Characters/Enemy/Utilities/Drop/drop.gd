extends Area2D
class_name Drop
## drop.gd
##
## Drops (XP points, food, chests) are spawned by the server but exist on all
## peers.  Collection is handled by whoever physically overlaps the drop — in
## practice that's only the local player, so no special authority check is
## needed beyond confirming the body IS a Player.
##
## Bug fix: the original code used `global_position / 10` for attraction with
## no delta multiplication, making it framerate-dependent.  Fixed below.

@onready var sprite_2d:    Sprite2D            = $Sprite2D
@onready var audio_player: AudioStreamPlayer   = $AudioPlayer

var can_attract:      bool   = true
var start_attracting: bool   = false
## How fast the drop moves toward the player (units per second).
const ATTRACT_SPEED: float   = 300.0

@export var resource: ResDrop


func _ready() -> void:
	can_attract        = resource.can_attract
	sprite_2d.texture  = resource.sprite
	if resource.audio:
		audio_player.stream = resource.audio


func _process(delta: float) -> void:
	if not start_attracting:
		return

	# Move toward the LOCAL player.
	# delta is "seconds since last frame" — multiplying by it makes the
	# speed consistent regardless of how fast the game is running.
	var local_player := Global.get_local_player()
	if local_player == null:
		return

	var to_player := local_player.global_position - global_position
	# Move at ATTRACT_SPEED but never overshoot (min with actual distance).
	global_position += to_player.normalized() * min(ATTRACT_SPEED * delta, to_player.length())


## Player enters the detection area — collect the drop.
func _on_body_entered(body: Node2D) -> void:
	if not body is Player:
		return

	# Only the local player collects drops on their machine.
	if not body.is_multiplayer_authority():
		return

	visible = false
	resource.drop_collected(body)

	if resource.audio:
		audio_player.pitch_scale = randf_range(0.95, 1.05)
		audio_player.play()
		await audio_player.finished

	queue_free()


## Drop lifetime expired without being collected.
func _on_timer_timeout() -> void:
	queue_free()
