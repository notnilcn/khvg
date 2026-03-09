extends Area2D
class_name ArrowProjectile

@export var speed: int = 700 # Projectile Speed

var direction: Vector2 # Projectile Direction
var damage: float # Projectile Damage

## READY
func _ready() -> void:
	rotation = direction.angle() # Rotate to direction

## PHYSICS PROCCESS
func _physics_process(delta: float) -> void:
	position += direction * speed * delta # Move Projectile


## PROJECTILE IMPACT
func _on_body_entered(body: Node2D) -> void:
	if body is Enemy:
		body.hurt(damage * Global.player.extra_damage) # Damage Enemy
		queue_free() # Remove Projectile
