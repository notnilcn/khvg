extends Area2D
class_name SineArrowProjectile
## sine_arrow_projectile.gd  —  CLIENT-AUTHORITATIVE HIT REPORTING
##
## Moves in a sine wave toward a fixed direction set at fire time.
## Hit detection and reporting are identical to ArrowProjectile.

const LIFETIME: float = 4.0

@export var speed:     float = 500.0
@export var amplitude: float = 30.0   ## Max lateral deviation in pixels.
@export var frequency: float = 2.0    ## Oscillations per second.

var direction:     Vector2
var damage:        float
var fire_rate:     float = 1.0
var owner_peer_id: int   = 1

var _spawn_pos: Vector2
var _perp:      Vector2   ## Perpendicular to direction for the wave axis.
var _elapsed:   float = 0.0


func _ready() -> void:
	_spawn_pos = global_position
	_perp      = Vector2(-direction.y, direction.x)
	rotation   = direction.angle()


func _physics_process(delta: float) -> void:
	_elapsed += delta
	if _elapsed > LIFETIME:
		queue_free()
		return

	var prev_pos := global_position
	global_position = _spawn_pos \
		+ direction * speed * _elapsed \
		+ _perp     * amplitude * sin(frequency * TAU * _elapsed)

	# Rotate sprite to match actual travel direction each frame.
	var travel := global_position - prev_pos
	if travel.length_squared() > 0.0:
		rotation = travel.angle()


func _on_body_entered(body: Node2D) -> void:
	var enemy := _get_enemy_from_body(body)
	if enemy == null:
		queue_free()
		return

	var shooter: Player = Global.players.get(owner_peer_id, null)
	if shooter == null:
		queue_free()
		return

	# Only the owning client reports hits; other clients discard on contact.
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
