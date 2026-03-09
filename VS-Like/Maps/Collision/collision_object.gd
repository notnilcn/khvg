extends StaticBody2D

const MIN_RADIUS: int = 350

@onready var timer: Timer = $Timer

func reposition_object():
	var rand_position: Vector2 = _get_random_spawn_pos(Global.player.global_position)
	global_position = rand_position

# GET A RANDOM POSITION AROUND THE PLAYER
func _get_random_spawn_pos(global_pos: Vector2) -> Vector2:
	var angle = randf() * TAU
	var distance = MIN_RADIUS + randf() * 100
	var offset = Vector2(cos(angle), sin(angle)) * distance
	return global_pos + offset


func _on_visible_on_screen_notifier_2d_screen_entered() -> void:
	if !timer.is_stopped():
		timer.stop()
		print("STOP TIMER")


func _on_visible_on_screen_notifier_2d_screen_exited() -> void:
	timer.start()
	print("START TIMER")


func _on_timer_timeout() -> void:
	reposition_object()
	print("REPOSITION")
