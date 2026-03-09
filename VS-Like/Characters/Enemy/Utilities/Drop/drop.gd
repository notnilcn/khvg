extends Area2D
class_name Drop

# References
@onready var sprite_2d: Sprite2D = $Sprite2D
@onready var audio_player: AudioStreamPlayer = $AudioPlayer

# Drop data
var can_attract: bool = true
var start_attracting: bool = false
@export var resource: ResDrop

## READY
func _ready() -> void:
	# Setting data using the Custom Resource
	can_attract = resource.can_attract
	sprite_2d.texture = resource.sprite
	if resource.audio:
		audio_player.stream = resource.audio

## PROCCESS
func _process(_delta: float) -> void:
	# Move drop towards the Player
	if start_attracting:
		global_position += (Global.player.global_position - global_position) / 10


# Player collected the drop
func _on_body_entered(body: Node2D) -> void:
	if body is Player:
		visible = false # Make it invisible
		resource.drop_collected(body) # Collection Action
		
		# If the Drop has audio, wait for it to finish to remove Drop
		if resource.audio:
			audio_player.pitch_scale = randf_range(0.95, 1.05)
			audio_player.play()
			await audio_player.finished
			queue_free()
		else:
			queue_free()


# Remove Drop if not collected in time
func _on_timer_timeout() -> void:
	queue_free()
