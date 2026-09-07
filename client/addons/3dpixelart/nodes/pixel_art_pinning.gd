class_name PixelArtPinning
extends Node
## Ownership-tracked subject pinning manager.
##
## Pinned subjects keep stencil id [member pin_outline_id] (default 255,
## reserved — never use it as a regular outline_id) in the metadata buffer.
## The macro-pixel pass probes a dilated neighbourhood for that id and forces
## Shift = 0 there, so pinned subjects ignore the smooth-scroll shift while
## the background moves. Original outline IDs are remembered in an ownership
## dict and restored on unpin.
##
## Auto-pin: every PixelArtObject3D under a member of [member auto_pin_group]
## (default "pixel_art_pin_target", e.g. the view target / player pawn) is
## pinned on ready. PixelArtPipeline3D drives this manager for its
## auto_pin/auto_pin_target exports; you can also place one manually.

## Reserved outline ID marking pinned subjects (stencil 255).
@export var pin_outline_id := 255
## Group whose members' PixelArtObject3D descendants are auto-pinned.
@export var auto_pin_group := &"pixel_art_pin_target"

## instance_id of PixelArtObject3D -> original outline_id before pinning.
var _ownership := {}


func _ready() -> void:
	add_to_group("pixel_art_pinning")
	call_deferred("_auto_pin_deferred")


func _auto_pin_deferred() -> void:
	for node in get_tree().get_nodes_in_group(auto_pin_group):
		if node is Node:
			pin_subtree(node)


## Pin a single PixelArtObject3D (remembers its outline ID for restore).
func pin(obj: PixelArtObject3D) -> void:
	if obj == null:
		return
	var key := obj.get_instance_id()
	if not _ownership.has(key):
		_ownership[key] = obj.outline_id
	obj.set_pinned(true, pin_outline_id)


## Unpin a single PixelArtObject3D, restoring its original outline ID.
func unpin(obj: PixelArtObject3D) -> void:
	if obj == null:
		return
	var key := obj.get_instance_id()
	if _ownership.has(key):
		obj.set_pinned(false)
		obj.outline_id = int(_ownership[key])
		_ownership.erase(key)
	else:
		obj.set_pinned(false)


## Pin every PixelArtObject3D under [param root] (inclusive).
func pin_subtree(root: Node) -> void:
	if root == null:
		return
	if root is PixelArtObject3D:
		pin(root as PixelArtObject3D)
	for child in root.get_children():
		pin_subtree(child)


## Configure a freshly created primitive for pinned rendering: pin it and
## disable euler snapping (a pinned subject must never quantize rotation).
func configure_pinned_primitive(obj: PixelArtObject3D) -> void:
	if obj == null:
		return
	obj.snap_euler_angles = false
	pin(obj)


## Unpin everything this manager owns, restoring original outline IDs.
func unpin_all() -> void:
	for key in _ownership.keys():
		var obj := instance_from_id(key) as PixelArtObject3D
		if obj != null and is_instance_valid(obj):
			obj.set_pinned(false)
			obj.outline_id = int(_ownership[key])
	_ownership.clear()
