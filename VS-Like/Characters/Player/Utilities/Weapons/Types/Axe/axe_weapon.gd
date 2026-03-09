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
	if not body is Enemy:
		return

	# Only the LOCAL player's weapons should report hits.
	# Every peer has copies of ALL players' nodes in their scene tree (for
	# display), but only the owning peer should fire the RPC.
	# We check this by seeing if our parent chain leads to the local player.
	var local_player := Global.get_local_player()
	if local_player == null:
		return
	if not _belongs_to_local_player(local_player):
		return

	var final_damage := damage * local_player.extra_damage

	# Show floating text on ALL peers immediately (pure visual, zero latency).
	body._show_floating_text_all.rpc(round(final_damage))

	# Tell the server (peer 1) to validate and apply the damage.
	body.report_hit.rpc_id(1, final_damage)


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
