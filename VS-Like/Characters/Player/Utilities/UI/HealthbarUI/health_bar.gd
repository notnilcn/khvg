extends ProgressBar
class_name Healthbar

# Show only when its value is updated
func _on_value_changed(_value: float) -> void:
	visible = true
	$Timer.start()


# Disappear if there are no changes after a while
func _on_timer_timeout() -> void:
	visible = false
