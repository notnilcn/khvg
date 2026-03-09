extends Weapon

# PROJECTILE SCENE TO SPAWN
const PROJECTILE_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/arrow_projectile.tscn")

var projectile_count: int = 1 
var spread_angle: float = 15.0  # Angle in degrees between projectiles

# Set Projectile direction
func get_projectile_direction() -> Vector2:
	var enemies: Array[Node2D] = Global.player.enemy_detection.get_overlapping_bodies()
	if !enemies.is_empty():
		var first_objective: Node2D = enemies.front()
		return global_position.direction_to(first_objective.global_position)
	else:
		return Vector2.RIGHT


## FIRE WEAPON
func _on_fire() -> void:
	var base_direction: Vector2 = get_projectile_direction()
	
	var base_angle: float = base_direction.angle() # Calculate the base angle in radians
	
	# We calculate the starting point in order to distribute the projectiles symmetrically
	var start_angle: float = base_angle - ((projectile_count - 1) * spread_angle * 0.5) * deg_to_rad(1)
	
	for i in projectile_count:
		var projectile: ArrowProjectile = PROJECTILE_SCENE.instantiate()
		projectile.damage = damage
		
		# Calculate the angle of the current projectile
		var projectile_angle: float = start_angle + (i * spread_angle * deg_to_rad(1))
		
		# Convert angle back to Vector2
		projectile.direction = Vector2(cos(projectile_angle), sin(projectile_angle)).normalized()
		projectile.global_position = global_position
		
		# Spawn Projectile
		get_tree().current_scene.add_child(projectile)


## UPGRADE WEAPON
func _on_upgraded() -> void:
	# Add 2 projectiles per shoot
	projectile_count += 2
