extends Weapon
## sine_arrow_weapon.gd
##
## Fires arrows toward the player's cursor in a sine wave trajectory.
## Uses the same client-authoritative hit reporting as ArrowProjectile.

const PROJECTILE_SCENE: PackedScene = preload("res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/sine_arrow_projectile.tscn")

var _amplitude_bonus: float = 0.0   ## Increased by upgrades.


func _ready() -> void:
	super._ready()
	fire.connect(_on_fire)
	upgraded.connect(_on_upgraded)


func _on_fire() -> void:
	var local_player := Global.get_local_player()
	if local_player == null:
		return
	if not _belongs_to_local_player(local_player):
		return

	var cursor_world := get_global_mouse_position()
	var dir := global_position.direction_to(cursor_world)
	if dir == Vector2.ZERO:
		dir = Vector2.RIGHT

	var proj: SineArrowProjectile = PROJECTILE_SCENE.instantiate()
	proj.damage        = damage
	proj.fire_rate     = resource.fire_rate
	proj.owner_peer_id = multiplayer.get_unique_id()
	proj.direction     = dir
	proj.amplitude     = 30.0 + _amplitude_bonus
	proj.global_position = global_position

	get_tree().current_scene.add_child(proj)


func _on_upgraded() -> void:
	_amplitude_bonus += 20.0  ## Each upgrade widens the wave by 20 px.


func _belongs_to_local_player(local_player: Player) -> bool:
	var node: Node = self
	while node != null:
		if node == local_player:
			return true
		node = node.get_parent()
	return false
