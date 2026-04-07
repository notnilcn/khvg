extends Weapon
## arrow_weapon.gd
##
## Spawns ArrowProjectile nodes that fly toward the nearest enemy.
## Each projectile carries the owner's peer_id so it knows whose extra_damage
## to apply and whether it should report the hit (only the owner does).

const PROJECTILE_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/arrow_projectile.tscn")

var projectile_count: int   = 1
var spread_angle:     float = 15.0  # degrees between projectiles


## Find the nearest enemy to shoot at.
func get_projectile_direction() -> Vector2:
	var local_player := Global.get_local_player()
	if local_player == null:
		return Vector2.RIGHT

	var enemies: Array[Node2D] = local_player.enemy_detection.get_overlapping_bodies()
	if not enemies.is_empty():
		return global_position.direction_to(enemies.front().global_position)
	return Vector2.RIGHT


## FIRE WEAPON
func _on_fire() -> void:
	# Only the local player's arrow weapon should spawn projectiles.
	# Remote players' arrow weapons are visual-only on our machine.
	var local_player := Global.get_local_player()
	if local_player == null:
		return
	if not _belongs_to_local_player(local_player):
		return

	var base_direction := get_projectile_direction()
	var base_angle     := base_direction.angle()
	var start_angle    := base_angle - ((projectile_count - 1) * spread_angle * 0.5) * deg_to_rad(1)

	for i in projectile_count:
		var projectile: ArrowProjectile = PROJECTILE_SCENE.instantiate()
		projectile.damage        = damage
		projectile.fire_rate     = resource.fire_rate  # forwarded to server for timing check
		projectile.owner_peer_id = multiplayer.get_unique_id()

		var projectile_angle  := start_angle + (i * spread_angle * deg_to_rad(1))
		projectile.direction   = Vector2(cos(projectile_angle), sin(projectile_angle)).normalized()
		projectile.global_position = global_position

		get_tree().current_scene.add_child(projectile)


func _belongs_to_local_player(local_player: Player) -> bool:
	var node: Node = self
	while node != null:
		if node == local_player:
			return true
		node = node.get_parent()
	return false


## UPGRADE WEAPON
func _on_upgraded() -> void:
	projectile_count += 2
