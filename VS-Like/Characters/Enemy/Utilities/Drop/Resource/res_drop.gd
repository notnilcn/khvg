extends Resource
class_name ResDrop

@export var sprite: Texture2D
@export var audio: AudioStreamWAV
@export var can_attract: bool = false # Drop moves towards the Player? This is only used for points

func drop_collected(_player: Player):
	pass
