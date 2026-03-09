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
