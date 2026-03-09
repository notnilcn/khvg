extends Sprite2D
class_name Enemy

signal died

# Scenes to spawn
const DROP_SCENE: PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const FLOATIN_TEXT_SCENE: PackedScene = preload("res://VS-Like/Game/UI/FloatingText/floating_text.tscn")

# Enemy Audio Player reference
@onready var audio_player_2d: AudioStreamPlayer2D = $AudioPlayer2D

# Physics Server
var object: RID
var shape

# Enemy Data
var res_enemy: ResEnemy
var curr_health: float
var speed: int
var damage: float
var res_drop: ResDrop
var is_elite: bool = false

# Movement
var direction: Vector2
var target: Node2D

## READY
func _ready() -> void:
	# Physics Server
	var physics_server := PhysicsServer2D
	object = physics_server.body_create()
	
	# Body Creation
	shape = PhysicsServer2D.circle_shape_create() # Add a shape
	PhysicsServer2D.shape_set_data(shape, res_enemy.radius) # Set circle radius
	PhysicsServer2D.body_add_shape(object, shape) # Make sure to keep the shape reference!
	physics_server.body_set_space(object, get_world_2d().space) # Add body to the space
	
	# Setting param
	var trans := Transform2D(0, global_position)
	physics_server.body_set_state(object, PhysicsServer2D.BODY_STATE_TRANSFORM, trans) # Set current transform
	physics_server.body_set_mode(object, PhysicsServer2D.BODY_MODE_RIGID) # Act as a Rigid Body
	physics_server.body_set_param(object, PhysicsServer2D.BODY_PARAM_GRAVITY_SCALE, 0.0) # No gravity because is a Top-Down Game
	physics_server.body_set_collision_layer(object, 0b0100) # Set collision layer: 3
	physics_server.body_set_collision_mask(object, 0b0101) # Set collision mask: 1 and 3
	
	await get_tree().process_frame
	# Needed to detect the body with exteral Areas2D
	PhysicsServer2D.body_attach_object_instance_id(object, get_instance_id())
	
	# Set Enemy Data using a Custom Resource
	texture = res_enemy.sprite # Set sprite
	# Set health
	if is_elite:
		curr_health = res_enemy.health * 10
		material = load("res://VS-Like/Characters/Enemy/Utilities/Shader/material_elite.tres")
		res_drop = load("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Chest/res_chest.tres")
	else:
		curr_health = res_enemy.health 
		res_drop = load("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Point/res_point.tres")
	speed = res_enemy.speed # Set speed
	damage = res_enemy.damage # Set damage
	target = Global.player # Set target -> Player

## PROCESS
func _process(_delta: float) -> void:
	# Update Enemy position
	_move_character()
	var new_trans: Transform2D = PhysicsServer2D.body_get_state(object, PhysicsServer2D.BODY_STATE_TRANSFORM)
	global_position = new_trans.origin
	
	# Eliminate Enemy if it is too far from the player
	if global_position.distance_to(Global.player.global_position) >= 600:
		queue_free()

# Move Enemy towards the Player
func _move_character():
	if target:
		direction = global_position.direction_to(target.global_position).normalized()
		PhysicsServer2D.body_set_state(object, PhysicsServer2D.BODY_STATE_LINEAR_VELOCITY, direction * speed)

# Enemy takes damage
func hurt(dmg: float):
	curr_health -= dmg # Reduce current health
	audio_player_2d.play() # Play sound effect
	show_floating_text(round(dmg)) # Show damage text
	if curr_health <= 0:
		die() # If current health is equal/less than zero, enemy dies

# Damage Floating Text creation
func show_floating_text(dmg: int):
	var floating_text: FloatingText = FLOATIN_TEXT_SCENE.instantiate()
	floating_text.global_position = global_position
	floating_text.floating_number = dmg
	get_tree().current_scene.call_deferred("add_child", floating_text)

# Enemy dies
func die():
	if is_elite:
		# 100% chance of leaving a drop
		spawn_drop()
	else:
		# 75% chance of leaving a drop
		if randf() > 0.25:
			spawn_drop()
	
	# Remove Enemy
	queue_free()

# Drop creation
func spawn_drop():
	var drop: Drop = DROP_SCENE.instantiate()
	drop.resource = res_drop
	drop.global_position = global_position
	get_tree().current_scene.call_deferred("add_child", drop)

# Remove PhysicsServer2D body and emit signal when Enemy dies
func _exit_tree() -> void:
	died.emit()
	PhysicsServer2D.free_rid(object)
