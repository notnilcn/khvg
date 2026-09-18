# This gizmo will have a handle for every single height value (squares) / hex (hexes).
# Mostly for debugging.

extends EditorNode3DGizmo
class_name MarchingSquaresTerrainChunkGizmo


func _squares_mode() -> bool:
	var chunk : MarchingSquaresTerrainChunk = get_node_3d()
	return chunk.terrain_system and chunk.terrain_system.get("grid_mode") == 0


func _redraw():
	clear()

	var chunk : MarchingSquaresTerrainChunk = get_node_3d()

	# Only draw the gizmo if this is the only selected node
	if len(EditorInterface.get_selection().get_selected_nodes()) != 1:
		return
	if EditorInterface.get_selection().get_selected_nodes()[0] != chunk:
		return

	if _squares_mode():
		_redraw_squares(chunk)
		return

	# Handles for raising/lowering terrain (one per hex, at hex centers)
	var hex_size := chunk.get_hex_size()
	var level_height := chunk.get_level_height()
	var corners := PackedVector3Array()
	var ids := PackedInt32Array()
	var offsets := MSTHexMath.hex_range(chunk.get_chunk_radius())
	for i in range(offsets.size()):
		var center_xz := MSTHexMath.hex_to_world(offsets[i].x, offsets[i].y, hex_size)
		corners.append(Vector3(center_xz.x, chunk.get_elevation(offsets[i]) * level_height, center_xz.y))
		ids.append(i)
	add_handles(corners, get_plugin().get_material("handles", self), ids)


func _redraw_squares(chunk: MarchingSquaresTerrainChunk):
	var dx := (chunk.dimensions.x - 1) * chunk.cell_size.x
	var dz := (chunk.dimensions.z - 1) * chunk.cell_size.y
	# Handles for raising/lowering terrain (will probably be removed later in favor of brush)
	var corners := PackedVector3Array()
	var ids := PackedInt32Array()
	for z in range(chunk.dimensions.z):
		for x in range(chunk.dimensions.x):
			var y = chunk.height_map[z][x]
			corners.append(Vector3(x * chunk.cell_size.x, y, z * chunk.cell_size.y))
			ids.append(z * chunk.dimensions.x + x)
	add_handles(corners, get_plugin().get_material("handles", self), ids)


func forward_spatial_gui_input(index: int, camera: Camera3D, event: InputEvent) -> bool:
	return false


func _get_handle_value(handle_id: int, secondary: bool) -> Variant:
	var chunk : MarchingSquaresTerrainChunk = get_node_3d()
	if _squares_mode():
		var z = handle_id / chunk.dimensions.z
		var x = handle_id % chunk.dimensions.z
		return chunk.height_map[z][x];
	return chunk.get_elevation(_offset_for_handle(chunk, handle_id)) * chunk.get_level_height();


func _commit_handle(handle_id: int, secondary: bool, restore: Variant, cancel: bool) -> void:
	var chunk : MarchingSquaresTerrainChunk = get_node_3d()

	if cancel:
		move_terrain_point(chunk, handle_id, restore)
	else:
		var undo_redo := MarchingSquaresTerrainPlugin.instance.get_undo_redo()

		var do_value : float
		if _squares_mode():
			var z = handle_id / chunk.dimensions.z
			var x = handle_id % chunk.dimensions.z
			do_value = chunk.height_map[z][x]
		else:
			do_value = chunk.get_elevation(_offset_for_handle(chunk, handle_id)) * chunk.get_level_height()

		undo_redo.create_action("move terrain point")
		undo_redo.add_do_method(self, "move_terrain_point", chunk, handle_id, do_value)
		undo_redo.add_undo_method(self, "move_terrain_point", chunk, handle_id, restore)
		undo_redo.commit_action()

	chunk.update_gizmos()


func move_terrain_point(chunk: MarchingSquaresTerrainChunk, handle_id: int, height: float):
	if _squares_mode():
		var z = handle_id / chunk.dimensions.z
		var x = handle_id % chunk.dimensions.z
		chunk.height_map[z][x] = height
		chunk.mark_dirty()

		notify_needs_update(chunk, z, x)
		notify_needs_update(chunk, z, x - 1)
		notify_needs_update(chunk, z - 1, x)
		notify_needs_update(chunk, z - 1, x - 1)

		chunk.regenerate_mesh()
		chunk.update_gizmos()
		return
	chunk.draw_elevation(_offset_for_handle(chunk, handle_id), int(round(height / chunk.get_level_height())))
	chunk.regenerate_mesh()
	chunk.update_gizmos()


func notify_needs_update(chunk: MarchingSquaresTerrainChunk, z: int, x: int):
	if z < 0 or z >= chunk.dimensions.z - 1 or x < 0 or x >= chunk.dimensions.x - 1:
		return
	chunk.needs_update[z][x] = true


# Handle ids index into the deterministic MSTHexMath.hex_range order.
func _offset_for_handle(chunk: MarchingSquaresTerrainChunk, handle_id: int) -> Vector2i:
	var offsets := MSTHexMath.hex_range(chunk.get_chunk_radius())
	if handle_id < 0 or handle_id >= offsets.size():
		return Vector2i.ZERO
	return offsets[handle_id]


func _set_handle(handle_id: int, secondary: bool, camera: Camera3D, screen_pos: Vector2) -> void:
	var chunk : MarchingSquaresTerrainChunk = get_node_3d()
	if _squares_mode():
		var z = handle_id / chunk.dimensions.z
		var x = handle_id % chunk.dimensions.z
		var y = chunk.height_map[z][x]
		# Get handle position
		var handle_position = chunk.to_global(Vector3(x * chunk.cell_size.x, y, z * chunk.cell_size.y))

		# Convert mouse movement to 3D world coordinates using raycasting
		var ray_origin = camera.project_ray_origin(screen_pos)
		var ray_dir = camera.project_ray_normal(screen_pos)

		var plane := Plane(Vector3.UP, handle_position.y)

		var intersection = plane.intersects_ray(ray_origin, ray_dir)

		if intersection:
			intersection = chunk.to_local(intersection)
			chunk.height_map[z][x] = intersection.y
			chunk.mark_dirty()
			chunk.update_gizmos()
		return
	var offset := _offset_for_handle(chunk, handle_id)
	var hex_size := chunk.get_hex_size()
	var level_height := chunk.get_level_height()
	var center_xz := MSTHexMath.hex_to_world(offset.x, offset.y, hex_size)
	# Get handle position
	var handle_position = chunk.to_global(Vector3(center_xz.x, chunk.get_elevation(offset) * level_height, center_xz.y))

	# Convert mouse movement to 3D world coordinates using raycasting
	var ray_origin = camera.project_ray_origin(screen_pos)
	var ray_dir = camera.project_ray_normal(screen_pos)

	var plane := Plane(Vector3.UP, handle_position.y)

	var intersection = plane.intersects_ray(ray_origin, ray_dir)

	if intersection:
		intersection = chunk.to_local(intersection)
		chunk.draw_elevation(offset, int(round(intersection.y / level_height)))
		chunk.regenerate_mesh()
		chunk.update_gizmos()
