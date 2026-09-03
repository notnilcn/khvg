@tool
extends Node3D
# Throwaway smoke-test scene builder for the MST hex terrain overhaul — delete after use.


const JSON_PATH := "user://mst_smoke_test.json"

var terrain : MarchingSquaresTerrain


func _ready() -> void:
	call_deferred("_build")


func _build() -> void:
	if terrain:
		return
	
	terrain = MarchingSquaresTerrain.new()
	terrain.name = "MarchingSquaresTerrain"
	terrain.enable_runtime_texture_baking = false
	add_child(terrain)
	
	var light := DirectionalLight3D.new()
	light.rotation_degrees = Vector3(-50, -35, 0)
	light.shadow_enabled = true
	add_child(light)
	
	var env := WorldEnvironment.new()
	var environment := Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.sky = Sky.new()
	environment.sky.sky_material = ProceduralSkyMaterial.new()
	env.environment = environment
	add_child(env)
	
	var camera := Camera3D.new()
	camera.position = Vector3(22, 55, 35)
	camera.look_at(Vector3(22, 0, -12), Vector3.UP)
	camera.current = true
	add_child(camera)
	
	# Two hills in neighbouring chunks so cross-chunk cliff walls show
	terrain.add_new_chunk(0, 0, null)
	terrain.add_new_chunk(1, 0, null)
	
	for chunk_coords in terrain.chunks:
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
		var center := MSTHexMath.chunk_center(chunk_coords.x, chunk_coords.y, terrain.chunk_radius)
		for local_hex in MSTHexMath.hex_range(terrain.chunk_radius):
			var dist := _hex_distance(local_hex)
			var elevation := 0
			if dist <= 2:
				elevation = 3
			elif dist <= 4:
				elevation = 2
			elif dist <= 6:
				elevation = 1
			if elevation == 0:
				continue
			chunk.draw_elevation(local_hex, elevation)
			chunk.draw_ground_slot(local_hex, 2 * elevation - 1)
			chunk.draw_wall_slot(local_hex, 5)
			chunk.draw_grass(local_hex, elevation <= 1)
	
	# Authored transitions on the plateau boundary of chunk (0,0):
	# alternating terrace and slope toward the elevation 2 ring
	var chunk_a : MarchingSquaresTerrainChunk = terrain.chunks[Vector2i(0, 0)]
	for local_hex in MSTHexMath.hex_range(terrain.chunk_radius):
		if chunk_a.get_elevation(local_hex) != 3:
			continue
		for dir in range(6):
			var neighbor := local_hex + MSTHexMath.neighbor_offset(dir)
			if chunk_a.get_elevation(neighbor) == 2:
				var kind := 2 if posmod(local_hex.x + local_hex.y, 2) == 0 else 3
				chunk_a.draw_edge_transitions(local_hex, MarchingSquaresTerrainChunk.set_edge_kind(chunk_a.get_edge_transitions(local_hex), dir, kind))
	
	for chunk in terrain.chunks.values():
		chunk.regenerate_mesh()


func _hex_distance(offset: Vector2i) -> int:
	return (absi(offset.x) + absi(offset.y) + absi(offset.x + offset.y)) / 2
