extends Area2D

@onready var timer: Timer = $Timer
@onready var audio_player: AudioStreamPlayer = $AudioPlayer

## HANDLE PLAYER DAMAGE WITH ONLY ONE AREA2D (BETTER PERFORMACE)
func hurt_player(body: Node2D):
	Global.player.hurt(body.damage)
	audio_player.play()
	

# Damage player when an enemy enters the area
func _on_body_entered(body: Node2D) -> void:
	if body is Enemy:
		hurt_player(body)
		timer.start()

# Check if there are still enemies inside the area to damage again.
func _on_timer_timeout() -> void:
	var overlapping_bodies: Array[Node2D] = get_overlapping_bodies()
	if !overlapping_bodies.is_empty():
		for enemy in overlapping_bodies:
			hurt_player(enemy)
	timer.start()
