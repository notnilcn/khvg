@tool
## chunk data for external storage with source data needed to reconstruct terrain at runtime.
class_name MSTChunkData
extends Resource


## Chunk coordinates in the chunk grid
@export var chunk_coords : Vector2i

## Merge mode setting (squares mode)
@export var merge_mode : int

## Height values as 2D array (squares mode)
@export var height_map : Array

## Ground texture indices (squares mode)
@export var ground_texture_idx : PackedByteArray

## Wall texture indices (squares mode)
@export var wall_texture_idx : PackedByteArray

## Grass mask (squares mode)
@export var grass_mask : PackedByteArray

# Legacy format (V1.1) for backward compatibility during migration
@export var color_map_0 : PackedColorArray
@export var color_map_1 : PackedColorArray
@export var wall_color_map_0 : PackedColorArray
@export var wall_color_map_1 : PackedColorArray
@export var grass_mask_map : PackedColorArray

## Hexes from the chunk center to its edge at save time (hex mode)
@export var chunk_radius : int

## Local axial offsets of each stored hex, interleaved q, r
@export var local_offsets : PackedInt32Array

## Elevation level per hex
@export var elevations : PackedInt32Array

## Ground texture slot (0-15) per hex
@export var ground_idx : PackedByteArray

## Wall texture slot (0-15) per hex
@export var wall_idx : PackedByteArray

## Grass flag per hex
@export var grass : PackedByteArray

## Packed 6x2-bit edge transitions per hex
@export var edges : PackedByteArray

# Ephemeral data saved for caching but regenerated on load if missing
@export var mesh : Mesh
@export var collision_faces : PackedVector3Array
@export var grass_multimesh : MultiMesh


## Helper to set collision from a ConcavePolygonShape3D
func set_collision_from_shape(shape: ConcavePolygonShape3D) -> void:
	if shape:
		collision_faces = shape.get_faces()


## Helper to create ConcavePolygonShape3D from stored collision data
func get_collision_shape() -> ConcavePolygonShape3D:
	if collision_faces.is_empty():
		return null
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(collision_faces)
	return shape


## Check if this is V3 hex format.
func is_v3_format() -> bool:
	return local_offsets.size() == elevations.size() * 2


## Check if this carries V2 square-grid data (compact byte arrays) vs V1 legacy (color arrays).
func is_v2_format() -> bool:
	return not ground_texture_idx.is_empty()
