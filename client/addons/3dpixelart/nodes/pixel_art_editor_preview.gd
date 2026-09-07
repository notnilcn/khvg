@tool
class_name PixelArtEditorPreview
extends Node3D
## Minimal editor preview helper (v1).
## The full pipeline stays runtime-only; this node does NOT make it @tool.
## In the editor it draws a texel-grid gizmo and reuses the pipeline's
## debug_view values at runtime. Enable [member preview_in_editor] to see
## the anchor lattice spacing in the editor viewport.

@export var preview_in_editor := false
@export var texel_size := 0.032
@export var grid_extent := 5.0


func _process(_delta: float) -> void:
	if Engine.is_editor_hint():
		queue_redraw() if is_inside_tree() and preview_in_editor else pass


func _enter_tree() -> void:
	if Engine.is_editor_hint():
		set_notify_transform(true)
