extends Weapon
## axe_weapon.gd  —  CLIENT-AUTHORITATIVE HIT REPORTING
##
## When the axe's Area2D detects an enemy body, we:
##   1. Immediately show floating text locally (zero latency visual feedback).
##   2. Send an RPC to the server: "I hit this enemy for X damage".
##   3. The server validates and applies the actual health deduction.
##
## The player never waits for a server round-trip to see their hit land.

@onready var first_coll_shape:  CollisionShape2D = $FirstAxe/FirstCollShape
@onready var second_coll_shape: CollisionShape2D = $SecondAxe/SecondCollShape

var has_second_axe: bool  = false
var tween_speed:    float = 0.5


func rotation_tween() -> void:
	var tween: Tween = get_tree().create_tween()
	tween.tween_property(self, "rotation_degrees", 90, tween_speed)

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


## WEAPON DAMAGE — fired when the axe Area2D overlaps an enemy physics body.
func _on_area_2d_body_entered(body: Node2D) -> void:
	if not visible:
		return

	# On the SERVER the body IS the Enemy (PhysicsServer2D body is linked to it
	# via body_attach_object_instance_id).  On CLIENTS the body is the Enemy's
	# StaticBody2D child "Hitbox" (created by _setup_client_hitbox in enemy.gd).
	var enemy := _get_enemy_from_body(body)
	if enemy == null:
		return

	# Only the LOCAL player's weapons report hits.  Every peer has copies of all
	# players' weapon nodes (for the swing animation), but only the owner fires RPCs.
	var local_player := Global.get_local_player()
	if local_player == null:
		return
	if not _belongs_to_local_player(local_player):
		return

	var final_damage := damage * local_player.extra_damage

	# Immediate visual feedback on ALL peers — no server round-trip needed.
	enemy.show_floating_text.rpc(round(final_damage))

	# Ask the server to validate and apply the damage.
	# We pass resource.fire_rate so the server can verify the hit isn't arriving
	# faster than the weapon is capable of firing.
	enemy.report_hit.rpc_id(1, final_damage, resource.fire_rate)


## Returns the Enemy node from a physics body, regardless of whether the body IS
## the Enemy (server path) or a StaticBody2D child of the Enemy (client path).
func _get_enemy_from_body(body: Node2D) -> Enemy:
	if body is Enemy:
		return body as Enemy
	if body.get_parent() is Enemy:
		return body.get_parent() as Enemy
	return null


## Returns true if this weapon node is a descendant of the given player node.
func _belongs_to_local_player(local_player: Player) -> bool:
	var node: Node = self
	while node != null:
		if node == local_player:
			return true
		node = node.get_parent()
	return false


## UPGRADE WEAPON
func _on_upgraded() -> void:
	if upgrade_level == 2:
		has_second_axe     = true
		$SecondAxe.visible = true
	elif upgrade_level == 3:
		tween_speed = 0.25
