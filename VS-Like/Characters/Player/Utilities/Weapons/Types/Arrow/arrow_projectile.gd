extends Area2D
class_name ArrowProjectile
## arrow_projectile.gd  —  CLIENT-AUTHORITATIVE HIT REPORTING
##
## The projectile moves entirely on the LOCAL client.  When it touches an enemy
## we report the hit to the server and destroy the projectile locally.
## The server validates and applies the damage.

@export var speed: int = 700

var direction: Vector2
var damage:    float

# Which player fired this projectile — stored so we can read extra_damage
# without depending on Global.get_local_player() (which may be a different
# player if another peer's projectile somehow exists here).
var owner_peer_id: int = 1


func _ready() -> void:
	rotation = direction.angle()


func _physics_process(delta: float) -> void:
	position += direction * speed * delta


## PROJECTILE IMPACT
func _on_body_entered(body: Node2D) -> void:
	if not body is Enemy:
		return

	# Look up the player who owns this projectile.
	var shooter: Player = Global.players.get(owner_peer_id, null)
	if shooter == null:
		queue_free()
		return

	# Only the owning client should report hits — if we're on a different
	# machine viewing this projectile, do nothing (the owner handles it).
	if owner_peer_id != multiplayer.get_unique_id():
		queue_free()
		return

	var final_damage := damage * shooter.extra_damage

	# Immediate visual feedback — floating text on all peers.
	body._show_floating_text_all.rpc(round(final_damage))

	# Report to server for validation + health deduction.
	body.report_hit.rpc_id(1, final_damage)

	queue_free()
