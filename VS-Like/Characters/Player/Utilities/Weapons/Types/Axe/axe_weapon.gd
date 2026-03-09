extends Weapon

@onready var first_coll_shape: CollisionShape2D = $FirstAxe/FirstCollShape # First Axe
@onready var second_coll_shape: CollisionShape2D = $SecondAxe/SecondCollShape # Second Axe

var has_second_axe: bool = false
var tween_speed: float = 0.5 # Axe speed

# Axe Movement
func rotation_tween():
	var tween: Tween = get_tree().create_tween()
	tween.tween_property(self, "rotation_degrees", 90, tween_speed)
	
	# Make it invisible and reset rotation when finished
	await tween.finished
	visible = false
	first_coll_shape.disabled = true
	if has_second_axe:
		second_coll_shape.disabled = true
	rotation = 0.0


## FIRE WEAPON
func _on_fire() -> void:
	visible = true
	first_coll_shape.disabled = false
	if has_second_axe:
		second_coll_shape.disabled = false
	rotation_tween()


## WEAPON DAMAGE
func _on_area_2d_body_entered(body: Node2D) -> void:
	if visible:
		if body is Enemy:
			body.hurt(damage * Global.player.extra_damage)


## UPGRADE WEAPON
func _on_upgraded() -> void:
	if upgrade_level == 2:
		# Add second axe
		has_second_axe = true
		$SecondAxe.visible = true
	elif upgrade_level == 3:
		# Increment Axe Speed
		tween_speed = 0.25
