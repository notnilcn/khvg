extends Resource
class_name ResPlayer

@export_category("Aspect")
@export var player_name: String
@export var sprite: Texture2D

@export_category("Stats")
@export var max_health: float = 100.0
@export var max_speed: float = 100.0
@export var res_weapon: ResItem
