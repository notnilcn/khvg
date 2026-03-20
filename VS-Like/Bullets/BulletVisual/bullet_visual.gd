extends Node2D
class_name BulletVisual
## bullet_visual.gd  —  Client-side visual for a parametric enemy bullet.
##
## Pooled: BulletManager grabs nodes from the pool on spawn and returns them
## on despawn rather than creating/freeing nodes every time.
## Position is updated each frame by BulletManager._update_visuals().

const RADIUS: float    = 4.0
const COLOR:  Color    = Color(1.0, 0.85, 0.1)  # yellow-orange


func _draw() -> void:
	draw_circle(Vector2.ZERO, RADIUS, COLOR)


func _ready() -> void:
	queue_redraw()
