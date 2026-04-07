extends Label
class_name FloatingText

# Damage colors
@export var colors: Dictionary[String, Color]

var floating_number: int = 55

## READY
func _ready() -> void:
	_set_color()
	text = str(floating_number)
	
	var tween: Tween = get_tree().create_tween()
	tween.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(self, "scale", Vector2(2.0, 2.0), 0.3)
	tween.tween_property(self, "scale", Vector2(1.0, 1.0), 0.3)
	await tween.finished
	queue_free()

## SET TEXT COLOR
func _set_color():
	if floating_number >= 18:
		add_theme_color_override("font_color", colors["Very High"])
	elif floating_number >= 14:
		add_theme_color_override("font_color", colors["High"])
	else:
		add_theme_color_override("font_color", colors["Normal"])
