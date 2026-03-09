extends Area2D

# Make Drops move towards to the Player if can_attract is True
func _on_area_entered(area: Area2D) -> void:
	if area is Drop:
		if area.can_attract:
			area.start_attracting = true
