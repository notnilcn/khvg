extends Weapon
class_name BibleWeapon

@export var rotation_speed: float = 5.0 # Bible movement

@onready var duration_timer: Timer = $DurationTimer
@onready var first_coll_shape: CollisionShape2D = %FirstCollShape # First Bible
@onready var second_coll_shape: CollisionShape2D = %SecondCollShape # Second Bible

var has_second_bible: bool = false

var is_firing: bool = false # Is Bible Active?

## PROCESS
func _process(_delta: float) -> void:
	if is_firing:
		rotation_degrees += rotation_speed # Rotate Bible


## FIRE WEAPON
func _on_fire() -> void:
	visible = true
	first_coll_shape.disabled = false
	if has_second_bible:
		second_coll_shape.disabled = false
	is_firing = true
	duration_timer.start()


## HIDE BIBLE
func _on_duration_timer_timeout() -> void:
	visible = false
	first_coll_shape.disabled = true
	if has_second_bible:
		second_coll_shape.disabled = true
	is_firing = false


## BIBLE DAMAGE
func _on_area_2d_body_entered(body: Node2D) -> void:
	if body is Enemy:
		body.hurt(damage * Global.player.extra_damage)


## WEAPON UPGRADE
func _on_upgraded() -> void:
	if upgrade_level == 2:
		# Add second Bible
		has_second_bible = true
		$SecondBible.visible = true
	elif upgrade_level == 3:
		# Increment rotation speed
		rotation_speed += 2.5
