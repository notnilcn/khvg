extends Weapon
class_name BibleWeapon
## bible_weapon.gd  —  CLIENT-AUTHORITATIVE HIT REPORTING
##
## Same hit-reporting model as the axe: client detects overlap locally,
## sends RPC to server, server validates + applies damage.

@export var rotation_speed: float = 5.0

@onready var duration_timer:    Timer            = $DurationTimer
@onready var first_coll_shape:  CollisionShape2D = %FirstCollShape
@onready var second_coll_shape: CollisionShape2D = %SecondCollShape

var has_second_bible: bool = false
var is_firing:        bool = false


func _process(_delta: float) -> void:
	if is_firing:
		rotation_degrees += rotation_speed


## FIRE WEAPON
func _on_fire() -> void:
	visible = true
	first_coll_shape.disabled = false
	if has_second_bible:
		second_coll_shape.disabled = false
	is_firing = true
	duration_timer.start()


## HIDE BIBLE when duration expires
func _on_duration_timer_timeout() -> void:
	visible = false
	first_coll_shape.disabled = true
	if has_second_bible:
		second_coll_shape.disabled = true
	is_firing = false


## BIBLE DAMAGE — fired when the rotating Area2D enters an enemy body.
func _on_area_2d_body_entered(body: Node2D) -> void:
	# Resolve Enemy from body — on clients body is the StaticBody2D "Hitbox"
	# child, not the Enemy directly (see enemy._setup_client_hitbox).
	var enemy := _get_enemy_from_body(body)
	if enemy == null:
		return

	var local_player := Global.get_local_player()
	if local_player == null:
		return
	if not _belongs_to_local_player(local_player):
		return

	var final_damage := damage * local_player.extra_damage

	enemy.show_floating_text.rpc(round(final_damage))
	enemy.report_hit.rpc_id(1, final_damage, resource.fire_rate)


func _get_enemy_from_body(body: Node2D) -> Enemy:
	if body is Enemy:
		return body as Enemy
	if body.get_parent() is Enemy:
		return body.get_parent() as Enemy
	return null


func _belongs_to_local_player(local_player: Player) -> bool:
	var node: Node = self
	while node != null:
		if node == local_player:
			return true
		node = node.get_parent()
	return false


## WEAPON UPGRADE
func _on_upgraded() -> void:
	if upgrade_level == 2:
		has_second_bible   = true
		$SecondBible.visible = true
	elif upgrade_level == 3:
		rotation_speed += 2.5
