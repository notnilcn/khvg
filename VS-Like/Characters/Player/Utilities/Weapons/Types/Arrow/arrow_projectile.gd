extends Area2D
class_name ArrowProjectile
## arrow_projectile.gd  —  CLIENT-AUTHORITATIVE HIT REPORTING
##
## The projectile moves entirely on the LOCAL client.  When it touches an enemy
## we report the hit to the server and destroy the projectile locally.
## The server validates and applies the damage.

@export var speed: int = 700

var direction:    Vector2
var damage:       float
var fire_rate:    float = 1.0   # set by arrow_weapon.gd; passed to server in report_hit

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
	# On clients the body is the enemy's StaticBody2D "Hitbox" child.
	# On the server the body IS the Enemy (PhysicsServer2D linked via instance_id).
	var enemy := _get_enemy_from_body(body)
	if enemy == null:
		queue_free()
		return

	var shooter: Player = Global.players.get(owner_peer_id, null)
	if shooter == null:
		queue_free()
		return

	# Only the owning client reports hits.
	if owner_peer_id != multiplayer.get_unique_id():
		queue_free()
		return

	var final_damage := damage * shooter.extra_damage

	enemy.show_floating_text.rpc(round(final_damage))
	if multiplayer.is_server():
		enemy.report_hit(final_damage, fire_rate)
	else:
		enemy.report_hit.rpc_id(1, final_damage, fire_rate)

	queue_free()


func _get_enemy_from_body(body: Node2D) -> Enemy:
	if body is Enemy:
		return body as Enemy
	if body.get_parent() is Enemy:
		return body.get_parent() as Enemy
	return null
