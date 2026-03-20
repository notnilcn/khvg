extends Resource
class_name ResEnemy

@export_category("Aspect")
@export var enemy_name: String 
@export var sprite: Texture2D
@export var radius: float = 7.0 # Body collision radius

@export_category("Stats")
@export var health: int = 10
@export var speed: int = 50
@export var damage: int = 5

@export_category("Bullet")
@export var bullet_damage:        float = 0.0   ## 0 = doesn't shoot.
@export var bullet_speed:         float = 150.0
@export var bullet_fire_interval: float = 2.0
