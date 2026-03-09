extends Node2D

# Scenes to spawn
const DROP_SCENE: PackedScene = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn")
const RES_FOOD: ResDropFood = preload("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/res_food.tres")
const ENEMY_SCENE: PackedScene = preload("res://VS-Like/Characters/Enemy/enemy.tscn")

const MIN_SPAWN_RADIUS: int = 400 # Min. radius to spawn anything
const MAX_ENEMIES: int = 300 # Max. enemies to be on the scene simultaneously

# All Enemy Custom Resources
# Add each new Enemy Resource you create to spawn here
@export var all_res_enemy: Array[ResEnemy]

@onready var spawn_timer: Timer = $SpawnTimer # Spawn Timer (1 sec.)

var enemies_count: int = 0 : set = _set_enemies_count # Enemies on the scene
var minutes: int : set = _set_minutes
var seconds: int : set = _set_seconds

## SPAWN SINGLE ENEMY
func _spawn_single_enemy(is_elite: bool = false):
	var spawn_pos: Vector2 = _get_random_spawn_pos(Global.player.global_position, MIN_SPAWN_RADIUS)
	_create_enemy(spawn_pos, _get_enemy(), is_elite)

## SPAWN CIRCLE PATTERN
func _spawn_cirlce_pattern(enemies_to_spawn: int):
	var player_pos: Vector2 = Global.player.global_position
	var angle_step: float = TAU / enemies_to_spawn
	for i in range(enemies_to_spawn):
		var angle: float = i * angle_step
		var spawn_pos: Vector2 = player_pos + Vector2(cos(angle), sin(angle)) * MIN_SPAWN_RADIUS
		_create_enemy(spawn_pos, all_res_enemy[0])

## CREATE ENEMY
func _create_enemy(spawn_pos: Vector2, res_enemy: ResEnemy, is_elite: bool = false):
	var enemy: Enemy = ENEMY_SCENE.instantiate()
	enemy.res_enemy = res_enemy
	enemy.is_elite = is_elite
	enemy.global_position = spawn_pos
	enemy.died.connect(enemy_died)
	add_child(enemy)
	enemies_count += 1

## GET A RANDOM POSITION AROUND THE PLAYER
func _get_random_spawn_pos(global_pos: Vector2, radius_distance: int):
	var angle = randf() * TAU
	var distance = radius_distance + randf() * 100
	var offset = Vector2(cos(angle), sin(angle)) * distance
	return global_pos + offset

## GET ENEMY RESOURCE
func _get_enemy() -> ResEnemy:
	return all_res_enemy.pick_random()

## UPDATE ENEMIES_COUNT WHEN AN ENEMY DIES
func enemy_died():
	enemies_count -= 1

## ADD ENEMY RESOURCES (NEW ENEMY)
func _add_enemy_type(minute: int):
	match minute:
		1:
			var res_zombie: ResEnemy = load("res://VS-Like/Characters/Enemy/Types/Zombie/res_zombie.tres")
			all_res_enemy.append(res_zombie)
			spawn_timer.wait_time = 0.75
		2:
			var res_bat: ResEnemy = load("res://VS-Like/Characters/Enemy/Types/Bat/res_bat.tres")
			all_res_enemy.append(res_bat)
			spawn_timer.wait_time = 0.5
		3:
			var res_cyclops: ResEnemy = load("res://VS-Like/Characters/Enemy/Types/Cyclops/res_cyclops.tres")
			all_res_enemy.append(res_cyclops)
			spawn_timer.wait_time = 0.35

## UPDATE ENEMIES COUNT LABEL (DEBUG)
func _set_enemies_count(value: int):
	enemies_count = value
	%EnemiesCountLabel.text = "ENEMIES: " + str(value)

## SET MINUTES VALUES
func _set_minutes(value: int):
	minutes = value
	_add_enemy_type(minutes)
	%Minutes.text = _set_digits(minutes)

## SET SECONDS VALUE
func _set_seconds(value: int):
	seconds = value
	if seconds >= 60:
		seconds = 0
		minutes += 1
	%Seconds.text = _set_digits(seconds)

# If it has only 1 digit, add a 0 before (Example: Minutes = 1, Seconds = 9 -> 01:09)
func _set_digits(n: int) -> String:
	var txt: String = ""
	var digits_lenght: int = str(abs(n)).length()
	
	if digits_lenght == 1:
		txt = "0" + str(n)
	else:
		txt = str(n)
	
	return txt

## UPDATE SECONDS
func _on_timer_timeout() -> void:
	seconds += 1


## SPAWN SINGLE ENEMY TIMER
func _on_spawn_timer_timeout() -> void:
	if enemies_count < MAX_ENEMIES:
		_spawn_single_enemy()


## SPAWN CIRCLE PATTERN TIMER
func _on_pattern_timer_timeout() -> void:
	if enemies_count < MAX_ENEMIES:
		if enemies_count + 40 < MAX_ENEMIES:
			_spawn_cirlce_pattern(40)
		else:
			var count_diff: int = MAX_ENEMIES - enemies_count
			_spawn_cirlce_pattern(count_diff)


## SPAWN ELITE ENEMY TIMER
func _on_elite_timer_timeout() -> void:
	_spawn_single_enemy(true)

## SPAWN FOOD TIMER
func _on_food_timer_timeout() -> void:
	var spawn_pos: Vector2 = _get_random_spawn_pos(
		Global.player.global_position, 
		randi_range(MIN_SPAWN_RADIUS, 600)
	)
	
	var food: Drop = DROP_SCENE.instantiate()
	food.resource = RES_FOOD
	food.global_position = spawn_pos
	add_child(food)
	
