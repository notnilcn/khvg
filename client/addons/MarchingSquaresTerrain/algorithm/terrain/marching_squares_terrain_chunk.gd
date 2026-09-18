@tool
extends MeshInstance3D
class_name MarchingSquaresTerrainChunk


## Edge transition kinds, stored as 2 bits per direction in a hex record's "edges" int.
## Direction order matches MSTHexMath.neighbor_offset (server neighbor-delta order).
## INHERIT derives the kind at mesh time; the derived default is always CLIFF for now.
enum EdgeKind {INHERIT, CLIFF, TERRACE, SLOPE}

## Ground slot 15 marks a hex as void: its top fan is skipped (walls are still built).
const VOID_SLOT : int = 15

# Terrace/slope geometry tuning (fractions of hex_size unless noted)
const TERRACE_TREAD_DEPTH : float = 0.35 # Outward depth of one terrace tread
const SLOPE_INSET : float = 0.35 # How far a slope leans into the low hex's area
const MAX_TERRACE_INSET : float = 0.7 # Total inward bound for multi-step terraces
const EDGE_END_OVERLAP : float = 0.05 # Strip ends extend past the corners to mitigate junction seams
const SLOPE_TOP_EPSILON : float = 0.001 # Fraction of level_height the slope inset line floats above the low fan
const MAX_TERRACE_DELTA : int = 4 # Terraces beyond this many levels fall back to a cliff

# Fallbacks used when the chunk has no terrain system assigned yet.
const DEFAULT_HEX_SIZE : float = 2.0
const DEFAULT_LEVEL_HEIGHT : float = 2.0
const DEFAULT_CHUNK_RADIUS : int = 8

# These two need to be normal export vars or else godot's internal logic crashes the plugin
@export var terrain_system : MarchingSquaresTerrain
@export var chunk_coords : Vector2i = Vector2i.ZERO

# Per-hex records keyed by local axial offset from the chunk center hex.
# Each record: {elevation: int, ground: int, wall: int, grass: bool, edges: int}
# Persisted via MSTDataHandler, cleared from the scene file on save.
@export_storage var hex_data : Dictionary = {}

var grass_planter : MarchingSquaresGrassPlanter

var global_position_cached : Vector3 = Vector3.ZERO

var bake_material : ShaderMaterial = preload("uid://cbbvkbnwmr2em")

var st : SurfaceTool # The surfacetool used to construct the current terrain

var _skip_save_on_exit : bool = false # Set to true when chunk is removed temporarily (undo/redo)
var _data_dirty : bool = false # Set to true when source data changes, triggers save in MSTDataHandler

#region temporary storage vars
# Temporary storage for ephemeral resources during scene save
var _temp_mesh : ArrayMesh
var _temp_grass_multimesh : MultiMesh
var _temp_collision_shapes : Array[ConcavePolygonShape3D] = []
var _temp_hex_data : Dictionary = {} # Source data - saved to external storage, not scene file
#endregion


# Called by TerrainSystem parent
func initialize_terrain(should_regenerate_mesh: bool = true):
	if _use_squares():
		_ensure_mode_data()
	grass_planter = get_node_or_null("GrassPlanter")
	if not grass_planter:
		grass_planter = MarchingSquaresGrassPlanter.new()
		add_child(grass_planter)
		grass_planter.name = "GrassPlanter"
		EngineWrapper.instance.set_owner_recursive(grass_planter)
	grass_planter._chunk = self
	grass_planter.terrain_system = terrain_system
	# Baked mesh/grass are a runtime optimization - the editor always rebuilds
	# from the source hex records so stale bakes can never shadow data edits
	var in_editor := EngineWrapper.instance.is_editor()
	if _temp_grass_multimesh and not in_editor:
		grass_planter.multimesh = _temp_grass_multimesh
	if not grass_planter.multimesh:
		grass_planter.setup(self)
		if _use_squares():
			if grass_planter.has_method("regenerate_all_cells"):
				grass_planter.regenerate_all_cells()
		elif grass_planter.has_method("regenerate_grass"):
			grass_planter.regenerate_grass()
	elif terrain_system and terrain_system.grass_mesh:
		grass_planter.multimesh.mesh = terrain_system.grass_mesh
	
	_apply_terrain_material_settings()
	
	if should_regenerate_mesh and (in_editor or not mesh):
		regenerate_mesh()
	elif mesh:
		if terrain_system:
			mesh.surface_set_material(0, terrain_system.terrain_material)
		if not _temp_collision_shapes.is_empty():
			_recreate_collision_body()
		else:
			_recreate_collision_from_mesh()
	
	if not EngineWrapper.instance.is_editor() and terrain_system.enable_runtime_texture_baking:
		var baker := MarchingSquaresGeometryBaker.new()
		baker.polygon_texture_resolution = terrain_system.polygon_texture_resolution
		baker.finished.connect(func(mesh_: Mesh, _original: MeshInstance3D, img: Image):
			mesh = mesh_
			var mat : Material
			if terrain_system.bake_material_override: 
				mat = terrain_system.bake_material_override.duplicate()
			else:
				mat = bake_material.duplicate()
			
			if mat is StandardMaterial3D:
				mat.albedo_texture = ImageTexture.create_from_image(img)
			elif mat is ShaderMaterial:
				mat.set_shader_parameter("texture_albedo", ImageTexture.create_from_image(img))
			mesh.surface_set_material(0, mat)
		, CONNECT_ONE_SHOT)
		baker.bake_geometry_texture(self, get_tree())


func _notification(what: int) -> void:
	if not EngineWrapper.instance.is_editor():
		return
	
	match what:
		NOTIFICATION_EDITOR_PRE_SAVE:
			# Store source data and clear - source data saved to external storage, not scene
			_skip_save_on_exit = _skip_save_on_exit # Surpress warning
			_temp_hex_data = hex_data
			hex_data = {}
			_temp_height_map = height_map
			height_map = []
			
			# Store mesh and clear to prevent serialization
			_temp_mesh = mesh
			mesh = null
			
			# Store grass multimesh and clear
			if grass_planter and grass_planter.multimesh:
				_temp_grass_multimesh = grass_planter.multimesh
				grass_planter.multimesh = null
			
			# Handle ALL collision bodies (old scenes may have multiple duplicates!)
			_temp_collision_shapes.clear()
			var bodies_to_free : Array[StaticBody3D] = []
			for child in get_children():
				if child is StaticBody3D:
					for shape_child in child.get_children():
						if shape_child is CollisionShape3D and shape_child.shape is ConcavePolygonShape3D:
							_temp_collision_shapes.append(shape_child.shape)
							shape_child.shape = null  # Clear to prevent sub_resource save
						shape_child.owner = null
					child.owner = null
					bodies_to_free.append(child)
			# Free all bodies (after iteration to avoid modifying while iterating)
			for body in bodies_to_free:
				body.name += "_"
				body.queue_free()
		
		NOTIFICATION_EDITOR_POST_SAVE:
			# Restore source data (both grid modes)
			if _temp_hex_data:
				hex_data = _temp_hex_data
				_temp_hex_data = {}
			if _temp_height_map:
				height_map = _temp_height_map
				_temp_height_map = []
			
			# Restore mesh
			if _temp_mesh:
				mesh = _temp_mesh
				_temp_mesh = null
			
			# Restore grass multimesh
			if _temp_grass_multimesh and grass_planter:
				grass_planter.multimesh = _temp_grass_multimesh
				_temp_grass_multimesh = null
			
			# Recreate ONE collision body (only need one, even if old scene had duplicates)
			if not _temp_collision_shapes.is_empty():
				_recreate_collision_body.call_deferred()
		
		NOTIFICATION_PREDELETE:
			# Safety cleanup - clear owner on ALL collision nodes
			for child in get_children():
				if child is StaticBody3D:
					child.owner = null
					for shape_child in child.get_children():
						if shape_child is CollisionShape3D:
							shape_child.owner = null


func _enter_tree() -> void:
	if get_parent() != terrain_system:
		push_error("Chunk must remain within its parent!")
	terrain_system.chunks[chunk_coords] = self


func _exit_tree() -> void:
	# Clear temp references
	_temp_hex_data = {}
	_temp_height_map = []
	_temp_mesh = null
	_temp_grass_multimesh = null
	_temp_collision_shapes.clear()
	
	# Clear owner on ALL collision nodes to prevent serialization edge cases
	if EngineWrapper.instance.is_editor():
		for child in get_children():
			if child is StaticBody3D:
				child.owner = null
				for shape_child in child.get_children():
					if shape_child is CollisionShape3D:
						shape_child.owner = null
	
	# Only erase if terrain_system still has THIS chunk at chunk_coords
	if terrain_system and terrain_system.chunks.get(chunk_coords) == self:
		terrain_system.chunks.erase(chunk_coords)


#region terrain settings

# These fall back to the defaults when the chunk has no terrain system (e.g. while
# being constructed), so parsing and early initialization never touch a null terrain.

## Hex outer radius in world units.
func get_hex_size() -> float:
	return terrain_system.hex_size if terrain_system else DEFAULT_HEX_SIZE


## World-space Y height of one elevation level.
func get_level_height() -> float:
	return terrain_system.level_height if terrain_system else DEFAULT_LEVEL_HEIGHT


## Hexes from the chunk center to its edge (hex-shaped chunk).
func get_chunk_radius() -> int:
	return terrain_system.chunk_radius if terrain_system else DEFAULT_CHUNK_RADIUS

#endregion

#region hex data accessors

## Create a default hex record (flat ground at level 0).
static func create_hex_record(elevation: int = 0, ground: int = 0, wall: int = 0, grass: bool = false, edges: int = 0) -> Dictionary:
	return {
		"elevation": elevation,
		"ground": ground,
		"wall": wall,
		"grass": grass,
		"edges": edges,
	}


## Get the hex record at a local axial offset, or an empty Dictionary if untouched.
func get_hex(lhex: Vector2i) -> Dictionary:
	return hex_data.get(lhex, {})


## Get the hex record adjacent to lhex in direction dir, crossing chunk borders via the
## terrain system when needed. An empty Dictionary means void (level 0 baseline).
func get_neighbor_hex(lhex: Vector2i, dir: int) -> Dictionary:
	var neighbor_lhex := lhex + MSTHexMath.neighbor_offset(dir)
	var s := -neighbor_lhex.x - neighbor_lhex.y
	if maxi(maxi(absi(neighbor_lhex.x), absi(neighbor_lhex.y)), absi(s)) <= get_chunk_radius():
		return hex_data.get(neighbor_lhex, {})
	if terrain_system:
		var center := MSTHexMath.chunk_center(chunk_coords.x, chunk_coords.y, get_chunk_radius())
		var global_hex := center + neighbor_lhex
		return terrain_system.get_hex_global(global_hex.x, global_hex.y)
	return {}


func get_elevation(lhex: Vector2i) -> int:
	return get_hex(lhex).get("elevation", 0)


func draw_elevation(lhex: Vector2i, level: int) -> void:
	_get_or_create_hex(lhex)["elevation"] = level
	mark_dirty()


func get_ground_slot(lhex: Vector2i) -> int:
	return get_hex(lhex).get("ground", 0)


func draw_ground_slot(lhex: Vector2i, slot: int) -> void:
	_get_or_create_hex(lhex)["ground"] = slot
	mark_dirty()


func get_wall_slot(lhex: Vector2i) -> int:
	return get_hex(lhex).get("wall", 0)


func draw_wall_slot(lhex: Vector2i, slot: int) -> void:
	_get_or_create_hex(lhex)["wall"] = slot
	mark_dirty()


func get_grass(lhex: Vector2i) -> bool:
	return get_hex(lhex).get("grass", false)


func draw_grass(lhex: Vector2i, enabled: bool) -> void:
	_get_or_create_hex(lhex)["grass"] = enabled
	mark_dirty()


## Get the packed 6x2-bit edge transitions int of a hex (0 = everything derived).
func get_edge_transitions(lhex: Vector2i) -> int:
	return get_hex(lhex).get("edges", 0)


func draw_edge_transitions(lhex: Vector2i, edges: int) -> void:
	_get_or_create_hex(lhex)["edges"] = edges
	mark_dirty()


## Extract the 2-bit edge kind of direction dir from a packed edges int.
static func get_edge_kind(edges: int, dir: int) -> int:
	return (edges >> (dir * 2)) & 0b11


## Return a copy of a packed edges int with direction dir set to kind.
static func set_edge_kind(edges: int, dir: int, kind: int) -> int:
	var shift := dir * 2
	return (edges & ~(0b11 << shift)) | ((kind & 0b11) << shift)


func _get_or_create_hex(lhex: Vector2i) -> Dictionary:
	if not hex_data.has(lhex):
		hex_data[lhex] = create_hex_record()
	return hex_data[lhex]


# Decide the effective edge kind for mesh building. An authored (non-INHERIT) kind
# always wins; inherited edges use the terrain's default_transition for Î” = 1
# and are always cliffs for Î” â‰¥ 2.
func _resolve_edge_kind(record: Dictionary, dir: int, delta: int) -> EdgeKind:
	var authored : int = get_edge_kind(record.get("edges", 0), dir)
	if authored != EdgeKind.INHERIT:
		return authored
	if delta >= 2:
		return EdgeKind.CLIFF
	if terrain_system and terrain_system.default_transition == 1:
		return EdgeKind.TERRACE
	return EdgeKind.CLIFF

#endregion

#region mesh generation

## Rebuild the whole chunk mesh from hex_data. Fan meshing is cheap
## (chunk_radius 8 = 217 hexes), so there is no incremental update tracking.
func regenerate_mesh(_use_threads: bool = false) -> void:
	if _use_squares():
		_ensure_mode_data()
		_regenerate_mesh_squares(_use_threads)
		if grass_planter and grass_planter.has_method("regenerate_all_cells"):
			grass_planter.regenerate_all_cells()
		return
	st = SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	st.set_custom_format(0, SurfaceTool.CUSTOM_RGBA_FLOAT)
	st.set_custom_format(1, SurfaceTool.CUSTOM_RGBA_FLOAT)
	st.set_custom_format(2, SurfaceTool.CUSTOM_RGBA_FLOAT)
	
	var start_time : int = Time.get_ticks_msec()
	
	global_position_cached = global_position if is_inside_tree() else position
	
	var hex_size := get_hex_size()
	var level_height := get_level_height()
	
	for lhex in MSTHexMath.hex_range(get_chunk_radius()):
		_build_hex_geometry(lhex, hex_size, level_height)
	
	mesh = st.commit()
	
	if mesh and terrain_system:
		mesh.surface_set_material(0, terrain_system.terrain_material)
	
	_recreate_collision_from_mesh()
	
	if grass_planter:
		grass_planter.regenerate_grass()
	
	var elapsed_time : int = Time.get_ticks_msec() - start_time
	print_verbose("Generated terrain in "+str(elapsed_time)+"ms")


## Whole-chunk regen for both grid modes (threshold setters + terrain node use this).
func regenerate_all_cells(_use_threads: bool = false) -> void:
	if _use_squares():
		_ensure_mode_data()
		_regenerate_all_cells_squares(_use_threads)
		return
	regenerate_mesh()


func _build_hex_geometry(lhex: Vector2i, hex_size: float, level_height: float) -> void:
	var record := get_hex(lhex)
	var elevation : int = record.get("elevation", 0)
	var ground_slot : int = record.get("ground", 0)
	var wall_slot : int = record.get("wall", 0)
	var has_grass : bool = record.get("grass", false)
	var edges : int = record.get("edges", 0)
	var top_y := elevation * level_height
	
	var center_xz := MSTHexMath.hex_to_world(lhex.x, lhex.y, hex_size)
	var hex_center := Vector3(center_xz.x, top_y, center_xz.y)
	
	# Gather the 6 neighbor records and elevation deltas once (server neighbor-delta order).
	var neighbors : Array[Dictionary] = []
	var deltas : Array[int] = []
	for dir in range(6):
		var neighbor := get_neighbor_hex(lhex, dir)
		neighbors.append(neighbor)
		deltas.append(elevation - int(neighbor.get("elevation", 0)))
	
	if ground_slot != VOID_SLOT:
		_build_top_fan(hex_center, hex_size, ground_slot, wall_slot, has_grass, deltas, neighbors)
	
	for dir in range(6):
		if deltas[dir] <= 0:
			continue # Equal heights share a plane; higher neighbors build their own walls.
		var kind := _resolve_edge_kind(record, dir, deltas[dir])
		match kind:
			EdgeKind.TERRACE:
				if deltas[dir] <= MAX_TERRACE_DELTA:
					_build_terrace(hex_center, top_y, dir, deltas[dir], level_height, hex_size, ground_slot, wall_slot)
				else:
					_build_cliff_wall(hex_center, top_y, dir, deltas[dir], level_height, hex_size, wall_slot)
			EdgeKind.SLOPE:
				_build_slope(hex_center, top_y, dir, deltas[dir], level_height, hex_size, ground_slot)
			_:
				_build_cliff_wall(hex_center, top_y, dir, deltas[dir], level_height, hex_size, wall_slot)


# Top fan: 6 triangles at elevation * level_height, center + corners, normal +Y.
# A corner adjacent to a cliff edge gets the ridge flag (cliff going down) or the
# ledge flag (cliff going up) so the shader keeps its wall-texture-continuation effect.
func _build_top_fan(hex_center: Vector3, hex_size: float, ground_slot: int, wall_slot: int, has_grass: bool, deltas: Array[int], neighbors: Array[Dictionary]) -> void:
	var ground_colors := MSTHexMath.slot_to_color_pair(ground_slot)
	var mat_blend := _slot_to_mat_blend(ground_slot)
	var grass_value := 1.0 if has_grass else 0.0
	
	var corners : Array[Vector3] = []
	var corner_uvs : Array[Vector2] = []
	var corner_custom_1 : Array[Color] = []
	for dir in range(6):
		# Corner dir is shared by edge dir-1 and edge dir.
		var prev_dir := posmod(dir - 1, 6)
		var ridge := deltas[prev_dir] > 0 or deltas[dir] > 0
		var ledge := deltas[prev_dir] < 0 or deltas[dir] < 0
		var nearest_wall_slot := float(wall_slot)
		if ledge and not ridge:
			# The wall towering over this corner belongs to the higher neighbor.
			var ledge_dir := prev_dir if deltas[prev_dir] < 0 else dir
			nearest_wall_slot = float(int(neighbors[ledge_dir].get("wall", 0)))
		var corner_xz := MSTHexMath.hex_corner_offset(dir, hex_size)
		corners.append(Vector3(hex_center.x + corner_xz.x, hex_center.y, hex_center.z + corner_xz.y))
		corner_uvs.append(Vector2(1.0 if ridge else 0.0, 1.0 if ledge else 0.0))
		corner_custom_1.append(Color(grass_value, 1.0 if ridge else 0.0, 1.0 if ledge else 0.0, nearest_wall_slot))
	
	var center_custom_1 := Color(grass_value, 0.0, 0.0, float(wall_slot))
	for dir in range(6):
		var next_dir := (dir + 1) % 6
		_add_vertex(hex_center, Vector3.UP, Vector2.ZERO, _world_xz(hex_center), ground_colors[0], ground_colors[1], center_custom_1, mat_blend)
		_add_vertex(corners[dir], Vector3.UP, corner_uvs[dir], _world_xz(corners[dir]), ground_colors[0], ground_colors[1], corner_custom_1[dir], mat_blend)
		_add_vertex(corners[next_dir], Vector3.UP, corner_uvs[next_dir], _world_xz(corners[next_dir]), ground_colors[0], ground_colors[1], corner_custom_1[next_dir], mat_blend)


# Vertical cliff quad on the edge toward a lower (or void) neighbor.
func _build_cliff_wall(hex_center: Vector3, top_y: float, dir: int, delta: int, level_height: float, hex_size: float, wall_slot: int) -> void:
	_build_riser(_edge_endpoints(hex_center, dir, hex_size), Vector2.ZERO, top_y, top_y - delta * level_height, dir, wall_slot)


# Terraced descent leaning into the low hex's area: alternating risers (wall slot)
# and treads (ground slot). Î” = 1 uses a half-level step (riser, tread, riser);
# Î” â‰¥ 2 builds Î” full-level steps with the tread depth bounded so the whole strip
# stays inside the low hex. Treads sit strictly above the low fan plane, and the
# final riser meets the fan along a single line, so there is no z-fighting.
func _build_terrace(hex_center: Vector3, top_y: float, dir: int, delta: int, level_height: float, hex_size: float, ground_slot: int, wall_slot: int) -> void:
	var ends := _edge_endpoints(hex_center, dir, hex_size)
	var strip_ends := _edge_endpoints(hex_center, dir, hex_size, true)
	var out_dir := _outward_direction(dir)
	if delta == 1:
		var mid_y := top_y - level_height * 0.5
		var inset := out_dir * (TERRACE_TREAD_DEPTH * hex_size)
		_build_riser(ends, Vector2.ZERO, top_y, mid_y, dir, wall_slot)
		_build_strip(strip_ends, Vector2.ZERO, inset, mid_y, mid_y, ground_slot)
		_build_riser(ends, inset, mid_y, top_y - level_height, dir, wall_slot)
	else:
		var tread_depth : float = minf(TERRACE_TREAD_DEPTH * hex_size, MAX_TERRACE_INSET * hex_size / delta)
		for i in range(delta):
			_build_riser(ends, out_dir * (i * tread_depth), top_y - i * level_height, top_y - (i + 1) * level_height, dir, wall_slot)
			if i < delta - 1:
				var tread_y := top_y - (i + 1) * level_height
				_build_strip(strip_ends, out_dir * (i * tread_depth), out_dir * ((i + 1) * tread_depth), tread_y, tread_y, ground_slot)


# Slanted quad from the high top edge down to an inset line inside the low hex.
# The inset line floats a hair above the low fan plane so the two never coincide.
func _build_slope(hex_center: Vector3, top_y: float, dir: int, delta: int, level_height: float, hex_size: float, ground_slot: int) -> void:
	var ends := _edge_endpoints(hex_center, dir, hex_size, true)
	var inset := _outward_direction(dir) * (SLOPE_INSET * hex_size)
	var bottom_y := top_y - delta * level_height + level_height * SLOPE_TOP_EPSILON
	_build_strip(ends, Vector2.ZERO, inset, top_y, bottom_y, ground_slot)


# Vertical quad (2 triangles) on the edge-parallel line at the given outward offset.
# The edge in direction dir spans corner dir and corner dir+1; the normal is the
# horizontal outward direction (angle 60*dir degrees, matching hex_to_world).
# Wall slot on the wall channel contract.
func _build_riser(ends: Array, offset: Vector2, top_y: float, bottom_y: float, dir: int, wall_slot: int) -> void:
	var top_a := Vector3(ends[0].x + offset.x, top_y, ends[0].z + offset.y)
	var top_b := Vector3(ends[1].x + offset.x, top_y, ends[1].z + offset.y)
	var bottom_a := Vector3(top_a.x, bottom_y, top_a.z)
	var bottom_b := Vector3(top_b.x, bottom_y, top_b.z)
	
	var angle := deg_to_rad(60.0 * dir)
	var normal := Vector3(cos(angle), 0.0, sin(angle))
	
	var wall_colors := MSTHexMath.slot_to_color_pair(wall_slot)
	var mat_blend := _slot_to_mat_blend(wall_slot)
	var custom_1 := Color(0.0, 0.0, 0.0, float(wall_slot))
	
	# Front faces wind clockwise seen from outside (normals are set explicitly).
	_add_wall_vertex(top_a, normal, wall_colors, custom_1, mat_blend)
	_add_wall_vertex(bottom_a, normal, wall_colors, custom_1, mat_blend)
	_add_wall_vertex(top_b, normal, wall_colors, custom_1, mat_blend)
	_add_wall_vertex(top_b, normal, wall_colors, custom_1, mat_blend)
	_add_wall_vertex(bottom_a, normal, wall_colors, custom_1, mat_blend)
	_add_wall_vertex(bottom_b, normal, wall_colors, custom_1, mat_blend)


# Flat or slanted quad strip (2 triangles) between the edge line (inner offset) and an
# outward line (outer offset), with the inner and outer ends at the given heights.
# Normal is +Y for flat treads, the plane normal for slanted strips; ground slot on
# the floor channel contract.
func _build_strip(ends: Array, inner_offset: Vector2, outer_offset: Vector2, inner_y: float, outer_y: float, ground_slot: int) -> void:
	var inner_a := Vector3(ends[0].x + inner_offset.x, inner_y, ends[0].z + inner_offset.y)
	var inner_b := Vector3(ends[1].x + inner_offset.x, inner_y, ends[1].z + inner_offset.y)
	var outer_a := Vector3(ends[0].x + outer_offset.x, outer_y, ends[0].z + outer_offset.y)
	var outer_b := Vector3(ends[1].x + outer_offset.x, outer_y, ends[1].z + outer_offset.y)
	
	var normal := (inner_b - inner_a).cross(outer_a - inner_a)
	if normal.y < 0.0:
		normal = -normal
	normal = normal.normalized()
	
	var ground_colors := MSTHexMath.slot_to_color_pair(ground_slot)
	var mat_blend := _slot_to_mat_blend(ground_slot)
	var custom_1 := Color(0.0, 0.0, 0.0, 0.0)
	
	# Front faces wind clockwise seen from above (normals are set explicitly).
	_add_vertex(inner_a, normal, Vector2.ZERO, _world_xz(inner_a), ground_colors[0], ground_colors[1], custom_1, mat_blend)
	_add_vertex(outer_a, normal, Vector2.ZERO, _world_xz(outer_a), ground_colors[0], ground_colors[1], custom_1, mat_blend)
	_add_vertex(inner_b, normal, Vector2.ZERO, _world_xz(inner_b), ground_colors[0], ground_colors[1], custom_1, mat_blend)
	_add_vertex(inner_b, normal, Vector2.ZERO, _world_xz(inner_b), ground_colors[0], ground_colors[1], custom_1, mat_blend)
	_add_vertex(outer_a, normal, Vector2.ZERO, _world_xz(outer_a), ground_colors[0], ground_colors[1], custom_1, mat_blend)
	_add_vertex(outer_b, normal, Vector2.ZERO, _world_xz(outer_b), ground_colors[0], ground_colors[1], custom_1, mat_blend)


# Endpoints of the edge facing direction dir (corner dir to corner dir+1, y = 0).
# Risers use the exact corners: adjacent risers already meet along the shared corner
# line, so extending them just juts fins past convex corners. Strips (terrace treads,
# slopes) pass extend = true so their ends overlap slightly at triple junctions
# instead of leaving seams (per-edge strips stay independent, no corner triangulation).
func _edge_endpoints(hex_center: Vector3, dir: int, hex_size: float, extend: bool = false) -> Array:
	var a_xz := MSTHexMath.hex_corner_offset(dir, hex_size)
	var b_xz := MSTHexMath.hex_corner_offset(dir + 1, hex_size)
	if extend:
		var edge_dir := (b_xz - a_xz).normalized()
		a_xz -= edge_dir * (EDGE_END_OVERLAP * hex_size)
		b_xz += edge_dir * (EDGE_END_OVERLAP * hex_size)
	return [
		Vector3(hex_center.x + a_xz.x, 0.0, hex_center.z + a_xz.y),
		Vector3(hex_center.x + b_xz.x, 0.0, hex_center.z + b_xz.y),
	]


# Horizontal outward direction of edge dir in XZ (angle 60*dir degrees).
func _outward_direction(dir: int) -> Vector2:
	var angle := deg_to_rad(60.0 * dir)
	return Vector2(cos(angle), sin(angle))


func _add_wall_vertex(pos: Vector3, normal: Vector3, colors: Array, custom_1: Color, mat_blend: Color) -> void:
	var global_pos := global_position_cached + pos
	# Walls always have UV (1, 1); UV2 keeps the old triplanar-friendly world projection.
	_add_vertex(pos, normal, Vector2.ONE, Vector2(global_pos.x + global_pos.z, global_pos.y * 2.0), colors[0], colors[1], custom_1, mat_blend)


func _add_vertex(pos: Vector3, normal: Vector3, uv: Vector2, uv2: Vector2, color_0: Color, color_1: Color, custom_1: Color, mat_blend: Color) -> void:
	st.set_normal(normal)
	st.set_uv(uv)
	st.set_uv2(uv2)
	st.set_color(color_0)
	st.set_custom(0, color_1)
	st.set_custom(1, custom_1)
	st.set_custom(2, mat_blend)
	st.add_vertex(pos)


# Floor UV2 = raw world XZ: with chunk_size (2,2,2) and cell_size (1,1) the shader's
# tiling factors become 1.0, giving continuous 1-unit tiling across chunk borders.
func _world_xz(pos: Vector3) -> Vector2:
	return Vector2(global_position_cached.x + pos.x, global_position_cached.z + pos.z)


# CUSTOM2 encoding: R = packed mat_a/mat_b byte, G = mat_c / 15, B = weight_a, A = weight_b.
# A hex has a single slot, so all three materials are the slot with weight_a = 1.
func _slot_to_mat_blend(slot: int) -> Color:
	var packed := float(slot + slot * 16) / 255.0
	return Color(packed, float(slot) / 15.0, 1.0, 0.0)

#endregion

## Mark chunk as having modified source data - triggers save in MSTDataHandler.
func mark_dirty() -> void:
	_data_dirty = true


func _apply_terrain_material_settings() -> void:
	if not terrain_system or not terrain_system.terrain_material:
		return
	if _use_squares():
		terrain_system.terrain_material.set_shader_parameter("chunk_size", terrain_system.dimensions)
		terrain_system.terrain_material.set_shader_parameter("cell_size", terrain_system.cell_size)
		return
	# Hex terrain tiles textures by world unit, so UV2 can be raw world coords.
	terrain_system.terrain_material.set_shader_parameter("chunk_size", Vector3i(2, 2, 2))
	terrain_system.terrain_material.set_shader_parameter("cell_size", Vector2(1, 1))


func _recreate_collision_from_mesh() -> void:
	for child in get_children():
		if child is StaticBody3D:
			child.free()
	create_trimesh_collision()
	for child in get_children():
		if child is StaticBody3D:
			child.collision_layer = 17
			child.set_collision_layer_value(terrain_system.extra_collision_layer, true)
			for _child in child.get_children():
				if _child is CollisionShape3D:
					_child.set_visible(false)


## Recreate collision body after scene save (deferred call for proper physics refresh).
func _recreate_collision_body() -> void:
	if not is_inside_tree() or _temp_collision_shapes.is_empty():
		_temp_collision_shapes.clear()
		return
		
	for child in get_children():
		if child is StaticBody3D:
			child.free()
	
	# Only create ONE body with the FIRST shape
	var shape : ConcavePolygonShape3D = _temp_collision_shapes[0]
	_temp_collision_shapes.clear()
	
	var body := StaticBody3D.new()
	body.name = name + "_col"
	body.collision_layer = 17
	if terrain_system:
		body.set_collision_layer_value(terrain_system.extra_collision_layer, true)
	
	var col_shape := CollisionShape3D.new()
	col_shape.name = "CollisionShape3D"
	col_shape.shape = shape
	col_shape.visible = false
	body.add_child(col_shape)
	add_child(body)
	
	# Set owner for editor visibility at first, but we clear it later
	if EngineWrapper.instance.is_editor():
		var scene_root = EngineWrapper.instance.get_root_for_node(self)
		if scene_root:
			body.owner = scene_root
			col_shape.owner = scene_root
		for group in get_groups():
			if group.begins_with("navmesh_"):
				body.add_to_group(group)


@export_tool_button("Export GLB") var bake = func():
	var tree := get_tree()
	
	var baker = MarchingSquaresGeometryBaker.new()
	baker.polygon_texture_resolution = terrain_system.polygon_texture_resolution
	
	var f := func(bakedMesh: Mesh, original: MeshInstance3D, bakedTexture: Image):
		var dialog := FileDialog.new()
		get_tree().root.add_child(dialog)
		dialog.file_mode = FileDialog.FILE_MODE_SAVE_FILE
		dialog.access = FileDialog.ACCESS_FILESYSTEM
		
		var inst := MeshInstance3D.new()
		inst.mesh = bakedMesh
		var mat := StandardMaterial3D.new()
		mat.albedo_texture = ImageTexture.create_from_image(bakedTexture)
		inst.mesh.surface_set_material(0, mat)
		var file_selected := func(path: String):
			var state := GLTFState.new()
			var doc := GLTFDocument.new()
			doc.append_from_scene(inst, state)
			doc.write_to_filesystem(state, path)
			dialog.queue_free()
		dialog.add_filter("*.glb", "GLB file")
		dialog.connect("file_selected", file_selected)
		dialog.popup_centered()
	
	baker.finished.connect(f, CONNECT_ONE_SHOT)
	baker.bake_geometry_texture(self, tree)


#region SQUARES mode (merged from original marching-squares implementation)
## Original marching-squares data model + mesher, kept for grid_mode == SQUARES.
## Hex path above is used when grid_mode == HEXES.

enum Mode {CUBIC, POLYHEDRON, ROUNDED_POLYHEDRON, SEMI_ROUND, SPHERICAL}

const MERGE_MODE = {
	Mode.CUBIC: 0.6,
	Mode.POLYHEDRON: 1.3,
	Mode.ROUNDED_POLYHEDRON: 2.1,
	Mode.SEMI_ROUND: 5.0,
	Mode.SPHERICAL: 20.0,
}

@export_custom(PROPERTY_HINT_NONE, "", PROPERTY_USAGE_STORAGE) var merge_mode : Mode = Mode.POLYHEDRON: # The max height distance between points before a wall is created between them
	set(mode):
		merge_mode = mode
		if is_inside_tree() and grass_planter and grass_planter.multimesh:
			var grass_mat : ShaderMaterial = grass_planter.multimesh.mesh.material as ShaderMaterial
			if mode == Mode.SEMI_ROUND or mode == Mode.SPHERICAL:
				grass_mat.set_shader_parameter("is_merge_round", true)
			else:
				grass_mat.set_shader_parameter("is_merge_round", false)
			merge_threshold = MERGE_MODE[mode]
			regenerate_all_cells(true)
@export_storage var height_map : Array # Stores the heights from the heightmap
#region cell_geometry storage
# Color maps are now ephemeral and created at runtime
# Persisted via MSTDataHandler
var color_map_0 : PackedColorArray # Stores the colors from vertex_color_0 (ground)
var color_map_1 : PackedColorArray # Stores the colors from vertex_color_1 (ground)
var wall_color_map_0 : PackedColorArray # Stores the colors for wall vertices (slot encoding channel 0)
var wall_color_map_1 : PackedColorArray # Stores the colors for wall vertices (slot encoding channel 1)
var grass_mask_map : PackedColorArray # Stores if a cell should have grass or not
#endregion

var merge_threshold : float = MERGE_MODE[Mode.POLYHEDRON]

var cell_generation_mutex : Mutex = Mutex.new()

#region chunk variables
# Size of the 2 dimensional cell array (xz value) and y scale (y value)
var dimensions : Vector3i:
	get:
		if terrain_system:
			return terrain_system.dimensions
		return Vector3i(33, 32, 33)
# Unit XZ size of a single cell
var cell_size : Vector2:
	get:
		if terrain_system:
			return terrain_system.cell_size
		return Vector2(2.0, 2.0)
#endregion

var cell_geometry : Dictionary = {} # Stores all generated tiles so that their geometry can quickly be reused

var needs_update : Array[Array] # Stores which tiles need to be updated because one of their corners' heights was changed.

var _temp_height_map : Array  # Source data (squares) - saved to external storage, not scene file

#region blend option vars
# Terrain blend options to allow for smooth color and height blend influence at transitions and at different heights 
var lower_thresh : float = 0.3 # Sharp bands: < 0.3 = lower color
var upper_thresh : float = 0.7 #, > 0.7 = upper color, middle = blend
var blend_zone := upper_thresh - lower_thresh
#endregion

func _regenerate_mesh_squares(use_threads: bool = false) -> void:
	_ensure_mode_data()
	st = SurfaceTool.new()
	if mesh:
		st.create_from(mesh, 0)
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	st.set_custom_format(0, SurfaceTool.CUSTOM_RGBA_FLOAT)
	st.set_custom_format(1, SurfaceTool.CUSTOM_RGBA_FLOAT)
	st.set_custom_format(2, SurfaceTool.CUSTOM_RGBA_FLOAT)
	
	var start_time : int = Time.get_ticks_msec()
	
	generate_terrain_cells(use_threads)
	
	st.generate_normals()
	st.index()
	# Create a new mesh out of floor, and add the wall surface to it
	mesh = st.commit()
	
	if mesh and terrain_system:
		mesh.surface_set_material(0, terrain_system.terrain_material)
	
	for child in get_children():
		if child is StaticBody3D:
			child.free()
	create_trimesh_collision()
	for child in get_children():
		if child is StaticBody3D:
			child.collision_layer = 17
			child.set_collision_layer_value(terrain_system.extra_collision_layer, true)
			for _child in child.get_children():
				if _child is CollisionShape3D:
					_child.set_visible(false)
	
	var elapsed_time : int = Time.get_ticks_msec() - start_time
	print_verbose("Generated terrain in "+str(elapsed_time)+"ms")



func generate_terrain_cells(use_threads: bool):
	if not cell_geometry:
		cell_geometry = {}
	
	global_position_cached = global_position if is_inside_tree() else position
	var thread_pool := MarchingSquaresThreadPool.new(max(1, OS.get_processor_count()))
	
	for z in range(dimensions.z - 1):
		for x in range(dimensions.x - 1):
			var cell_coords = Vector2i(x, z)
			var work_load : Callable
			# If geometry did not change, copy already generated geometry and skip this cell
			if not needs_update[z][x]:
				work_load = func():
					cell_generation_mutex.lock()
					var verts = cell_geometry[cell_coords]["verts"]
					var uvs = cell_geometry[cell_coords]["uvs"]
					var uv2s = cell_geometry[cell_coords]["uv2s"]
					var color_0s = cell_geometry[cell_coords]["color_0s"]
					var color_1s = cell_geometry[cell_coords]["color_1s"]
					var custom_1_values = cell_geometry[cell_coords]["custom_1_values"]
					var mat_blend = cell_geometry[cell_coords]["mat_blend"]
					var is_floor = cell_geometry[cell_coords]["is_floor"]
					
					for i in range(len(verts)):
						st.set_smooth_group(0 if is_floor[i] == true else -1)
						st.set_uv(uvs[i])
						st.set_uv2(uv2s[i])
						st.set_color(color_0s[i])
						st.set_custom(0, color_1s[i])
						st.set_custom(1, custom_1_values[i])
						st.set_custom(2, mat_blend[i])
						st.add_vertex(verts[i])
					cell_generation_mutex.unlock()
				if use_threads:
					thread_pool.enqueue(work_load)
				else:
					work_load.call()
				continue
			
			# Cell is now being updated
			needs_update[z][x] = false
			
			# If geometry did change or none exists yet, 
			# Create an entry for this cell (will also override any existing one)
			cell_geometry[cell_coords] = {
				"verts": PackedVector3Array(),
				"uvs": PackedVector2Array(),
				"uv2s": PackedVector2Array(),
				"color_0s": PackedColorArray(),
				"color_1s": PackedColorArray(),
				"custom_1_values": PackedColorArray(),
				"mat_blend": PackedColorArray(),
				"is_floor": [],
			}
			
			var color_helper := MarchingSquaresTerrainVertexColorHelper.new()
			var cell := MarchingSquaresTerrainCell.new(self, color_helper, height_map[z][x], height_map[z][x+1], height_map[z+1][x], height_map[z+1][x+1], merge_threshold)
			color_helper.chunk = self
			color_helper.cell = cell
			
			work_load = func():
				cell.generate_geometry(cell_coords)
				if grass_planter and grass_planter.terrain_system:
					grass_planter.generate_grass_on_cell(cell_coords)
			if use_threads:
				thread_pool.enqueue(work_load)
			else:
				work_load.call()
	
	if use_threads:
		thread_pool.start()
		thread_pool.wait()


func add_polygons(
	cell_coords : Vector2i, 
	pts : PackedVector3Array,
	uvs : PackedVector2Array,
	uv2s : PackedVector2Array,
	color_0s : PackedColorArray,
	color_1s : PackedColorArray,
	custom_1_values : PackedColorArray,
	mat_blends : PackedColorArray,
	floors : PackedByteArray,
	):
		assert(pts.size() % 3 == 0)
		assert(pts.size() == uvs.size())
		assert(pts.size() == uv2s.size())
		assert(pts.size() == color_0s.size())
		assert(pts.size() == color_1s.size())
		assert(pts.size() == custom_1_values.size())
		assert(pts.size() == mat_blends.size())
		assert(pts.size() == floors.size())
		
		cell_generation_mutex.lock()
		var floor_mode : bool = true
		st.set_smooth_group(0)
		for i in range(pts.size()):
			if floor_mode and not floors[i]:
				floor_mode = false
				st.set_smooth_group(-1)
			elif not floor_mode and floors[i]:
				floor_mode = true
				st.set_smooth_group(0)
			_add_point(cell_coords, pts[i], uvs[i], uv2s[i], color_0s[i], color_1s[i], custom_1_values[i], mat_blends[i], floors[i])
		cell_generation_mutex.unlock()


# Adds a point. Coordinates are relative to the top-left corner (not mesh origin relative)
# UV.x is closeness to the bottom of an edge. UV.Y is closeness to the edge of a cliff
func _add_point(cell_coords: Vector2i, vert: Vector3, uv: Vector2, uv2: Vector2, color_0: Color, color_1: Color, custom_1_value: Color, mat_blend: Color, is_floor: bool):
	st.set_color(color_0)
	st.set_custom(0, color_1)
	st.set_custom(1, custom_1_value)
	st.set_custom(2, mat_blend)
	st.set_uv(uv)
	st.set_uv2(uv2)
	st.add_vertex(vert)
	
	cell_geometry[cell_coords]["verts"].append(vert)
	cell_geometry[cell_coords]["uvs"].append(uv)
	cell_geometry[cell_coords]["uv2s"].append(uv2)
	cell_geometry[cell_coords]["color_0s"].append(color_0)
	cell_geometry[cell_coords]["color_1s"].append(color_1)
	cell_geometry[cell_coords]["custom_1_values"].append(custom_1_value)
	cell_geometry[cell_coords]["mat_blend"].append(mat_blend)
	cell_geometry[cell_coords]["is_floor"].append(is_floor)

#region cell_geometry generators (on being empty)


func generate_height_map():
	height_map = []
	height_map.resize(dimensions.z)
	for z in range(dimensions.z):
		height_map[z] = []
		height_map[z].resize(dimensions.x)
		for x in range(dimensions.x):
			height_map[z][x] = 0.0
	
	var noise := terrain_system.noise_hmap
	if noise:
		for z in range(dimensions.z):
			for x in range(dimensions.x):
				var noise_x = (chunk_coords.x * (dimensions.x - 1)) + x
				var noise_z = (chunk_coords.y * (dimensions.z -1)) + z
				var noise_sample = noise.get_noise_2d(noise_x, noise_z)
				height_map[z][x] = noise_sample * dimensions.y


func generate_color_maps():
	color_map_0 = PackedColorArray()
	color_map_1 = PackedColorArray()
	color_map_0.resize(dimensions.z * dimensions.x)
	color_map_1.resize(dimensions.z * dimensions.x)
	for z in range(dimensions.z):
		for x in range(dimensions.x):
			color_map_0[z*dimensions.x + x] = Color(0,0,0,0)
			color_map_1[z*dimensions.x + x] = Color(0,0,0,0)


func generate_wall_color_maps():
	wall_color_map_0 = PackedColorArray()
	wall_color_map_1 = PackedColorArray()
	wall_color_map_0.resize(dimensions.z * dimensions.x)
	wall_color_map_1.resize(dimensions.z * dimensions.x)
	for z in range(dimensions.z):
		for x in range(dimensions.x):
			wall_color_map_0[z*dimensions.x + x] = Color(1,0,0,0)  # Default to texture slot 0
			wall_color_map_1[z*dimensions.x + x] = Color(1,0,0,0)


func generate_grass_mask_map():
	grass_mask_map = PackedColorArray()
	grass_mask_map.resize(dimensions.z * dimensions.x)
	for z in range(dimensions.z):
		for x in range(dimensions.x):
			grass_mask_map[z*dimensions.x + x] = Color(1.0, 1.0, 1.0, 1.0)

#endregion

#region cell_geometry getters


func get_height(cc: Vector2i) -> float:
	return height_map[cc.y][cc.x]


func get_color_0(cc: Vector2i) -> Color:
	return color_map_0[cc.y*dimensions.x + cc.x]


func get_color_1(cc: Vector2i) -> Color:
	return color_map_1[cc.y*dimensions.x + cc.x]


func get_wall_color_0(cc: Vector2i) -> Color:
	return wall_color_map_0[cc.y*dimensions.x + cc.x]


func get_wall_color_1(cc: Vector2i) -> Color:
	return wall_color_map_1[cc.y*dimensions.x + cc.x]


func get_grass_mask(cc: Vector2i) -> Color:
	return grass_mask_map[cc.y*dimensions.x + cc.x]

#endregion

#region cell_geometry setters

# Draw to height.
# Returns the coordinates of all additional chunks affected by this height change.
# Empty for inner points, neightoring edge for non-corner edges, and 3 other corners for corner points.

func draw_height(x: int, z: int, y: float):
	# Contains chunks that were updated
	height_map[z][x] = y
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)


func draw_color_0(x: int, z: int, color: Color):
	color_map_0[z*dimensions.x + x] = color
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)


func draw_color_1(x: int, z: int, color: Color):
	color_map_1[z*dimensions.x + x] = color
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)


func draw_wall_color_0(x: int, z: int, color: Color):
	wall_color_map_0[z*dimensions.x + x] = color
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)


func draw_wall_color_1(x: int, z: int, color: Color):
	wall_color_map_1[z*dimensions.x + x] = color
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)


func draw_grass_mask(x: int, z: int, masked: Color):
	grass_mask_map[z*dimensions.x + x] = masked
	mark_dirty()
	notify_needs_update(z, x)
	notify_needs_update(z, x-1)
	notify_needs_update(z-1, x)
	notify_needs_update(z-1, x-1)

#endregion


func notify_needs_update(z: int, x: int):
	if z < 0 or z >= terrain_system.dimensions.z-1 or x < 0 or x >= terrain_system.dimensions.x-1:
		return
	
	needs_update[z][x] = true


func _regenerate_all_cells_squares(use_threads: bool = false) -> void:
	_ensure_mode_data()
	for z in range(dimensions.z-1):
		for x in range(dimensions.x-1):
			needs_update[z][x] = true
	
	_regenerate_mesh_squares(use_threads)

## True when this chunk should use the marching-squares path.
## Follows the terrain's Grid Mode setting (Terrain Settings tool) so the
## toggle always controls what renders. Legacy squares-only terrains are
## auto-switched to SQUARES on load (see _initialize_data_directory).
func _use_squares() -> bool:
	return terrain_system and terrain_system.get("grid_mode") == 0


## Unified grass refresh used by the terrain node: branches on grid mode.
func refresh_grass() -> void:
	if not grass_planter:
		return
	if _use_squares():
		if grass_planter.has_method("regenerate_all_cells"):
			grass_planter.regenerate_all_cells()
	else:
		if grass_planter.has_method("regenerate_grass"):
			grass_planter.regenerate_grass()


## Make sure the data store for the active grid mode exists.
func _ensure_mode_data() -> void:
	if _use_squares():
		if not height_map:
			generate_height_map()
		if not color_map_0 or not color_map_1:
			generate_color_maps()
		if not wall_color_map_0 or not wall_color_map_1:
			generate_wall_color_maps()
		if not grass_mask_map:
			generate_grass_mask_map()
		if needs_update.is_empty():
			needs_update = []
			for z in range(dimensions.z - 1):
				needs_update.append([])
				for x in range(dimensions.x - 1):
					needs_update[z].append(true)

#endregion
