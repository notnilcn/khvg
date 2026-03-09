extends CharacterBody2D
class_name Player

# Player Custom Resource
@export var resource: ResPlayer

# References
@onready var sprite_2d: Sprite2D = $Sprite2D
@onready var enemy_detection: Area2D = $EnemyDetection

## UI
# HUD
@onready var health_bar: Healthbar = $HealthBar # HEALTH BAR
@onready var progress_bar: ProgressBar = %ProgressBar # LEVEL BAR
@onready var weapons_container_ui: WeaponContainerUI = %WeaponsContainerUI # CURRENT WEAPONS IN HAND

# Add/Upgrade Weapon Menu (When Player levels up)
@onready var weapon_selection: WeaponSelectionUI = %WeaponSelection
# Chest Menu (When Player collects a Chest Drop)
@onready var chest_ui: ChestUI = %ChestUI

# Data
var max_health: float
var speed: float
var health: float : set = _set_health
var points: float = 0.0 : set = _set_points
var weapons: Array[Weapon] = []

# Passive weapons effects
var extra_exp: float = 1.0
var extra_damage: float = 1.0
var extra_speed: float = 1.0

## READY
func _ready() -> void:
	Global.player = self # Set Player to Global reference
	
	await get_tree().process_frame
	init_player()

func init_player():
	sprite_2d.texture = resource.sprite # Set Sprite
	max_health = resource.max_health # Set Max Health
	speed = resource.max_speed # Set Speed
	
	# Add inital weapon
	var weapon: Weapon = resource.res_weapon.weapon_scene.instantiate()
	weapon.resource = resource.res_weapon
	if weapon.attach_to_rotation:
		sprite_2d.add_child(weapon)
	else:
		add_child(weapon)
	weapons.append(weapon)
	weapons_container_ui.add_weapon(weapon)
	
	health = max_health # Set max health to current health

# Player takes damage
func hurt(dmg: float):
	health -= dmg # Reduce current health
	if health <= 0:
		# Restart Game when Player dies
		get_tree().call_deferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn")

## PHYSICS PROCESS
func _physics_process(_delta: float) -> void:
	_handle_movement()
	move_and_slide()

## MOVEMENT
func _handle_movement():
	var movement_input: Vector2 = Input.get_vector("Left", "Right", "Up", "Down").normalized()
	velocity = movement_input * (speed * extra_speed)
	
	# Horizontal Rotation
	if velocity.x > 0:
		sprite_2d.scale.x = 1
	elif velocity.x:
		sprite_2d.scale.x = -1

## SET CURRENT HEALTH
func _set_health(new_health: float):
	health = clamp(new_health, 0, max_health)
	health_bar.value = health

## SET POINTS
func _set_points(new_points: float):
	# Level up if current points are equal/more than progress bar max value (starts at 100)
	if new_points >= progress_bar.max_value:
		points = 0 # Reset points to zero
		progress_bar.max_value += 100 # Increment max value to level up (+100 each level)
		weapon_selection.show_selection() # Show Weapon Selection Menu
	else:
		points = new_points # Else just update points value
	
	progress_bar.value = points # Update progress bar value
