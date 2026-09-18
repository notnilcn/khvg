@tool
extends EditorPlugin
class_name MarchingSquaresTerrainPlugin


static var instance : MarchingSquaresTerrainPlugin

const EMPTY_TEXTURE_PRESET : MarchingSquaresTexturePreset = preload("uid://db4scsn2nqqyu")
const BrushPatternCalculator = preload("uid://bli1mnri3jwpa")

var vp_texture_names = preload("uid://dd7fens03aosa")

var gizmo_plugin := MarchingSquaresTerrainGizmoPlugin.new()
var toolbar := MarchingSquaresToolbar.new()
var tool_attributes := MarchingSquaresToolAttributes.new()
var active_tool : int = 0

var UI : Script = preload("uid://bmedudg6sllf8")
var ui : MarchingSquaresUI

var is_initialized : bool = false
var initialization_error : String = ""

var current_terrain_node : MarchingSquaresTerrain

var selected_chunk : MarchingSquaresTerrainChunk

# Flag to prevent _set_new_textures() when syncing preset from terrain node
var _syncing_from_terrain : bool = false

#region brush variables
var BrushMode : Dictionary = {
	"0" = preload("uid://cg3lvmu68oaaa"),
	"1" = preload("uid://b6uwsa1vjeb4"),
}

var BrushMat : Dictionary = {
	"0" = preload("uid://dtevocyixqsgv"),
	"1" = preload("uid://daofaifmtbyak"),
}

var current_brush_index : int = 0

var brush_position : Vector3

var BRUSH_VISUAL : Mesh = preload("uid://ch6cb07rh0m3l")
var BRUSH_RADIUS_VISUAL : Mesh = preload("uid://cg3lvmu68oaaa")
var BRUSH_RADIUS_MATERIAL : ShaderMaterial = preload("uid://dtevocyixqsgv")
@onready var falloff_curve : Curve = preload("uid://c0bexjsfvvcxb")
#endregion

#region tool_mode vars
enum TerrainToolMode {
	BRUSH = 0,
	LEVEL = 1,
	SMOOTH = 2,
	BRIDGE = 3,
	GRASS_MASK = 4,
	VERTEX_PAINTING = 5,
	DEBUG_BRUSH = 6,
	CHUNK_MANAGEMENT = 7,
	TERRAIN_SETTINGS = 8,
	IMPORT_EXPORT = 9,
}

var mode : TerrainToolMode = TerrainToolMode.BRUSH:
	set(value):
		mode = value
		current_draw_pattern.clear()
		if mode == TerrainToolMode.VERTEX_PAINTING:
			falloff = false
			BRUSH_RADIUS_MATERIAL.set_shader_parameter("falloff_visible", false)
#endregion

#region tool attribute vars
# Tool attribute variables
var brush_size : float = 15.0
var ease_value : float = -1.0 # No ease
var strength : float = 1.0
var height : float = 0.0
var flatten : bool = true
var falloff : bool = true

var should_mask_grass : bool = false

# Paths used by the IMPORT_EXPORT tool (res:// or absolute OS paths)
var export_path : String = "res://terrain_export.json"
var import_path : String = "res://terrain_export.json"

# Currently selected preset for vertex textures (DOES change the global terrain)
var current_texture_preset : MarchingSquaresTexturePreset = EMPTY_TEXTURE_PRESET.duplicate():
	set(value):
		current_texture_preset = value
		current_quick_paint = null
		if not _syncing_from_terrain:
			_set_new_textures(value)

# Currently selected preset for quick painting (does NOT change the global terrain)
var current_quick_paint : MarchingSquaresQuickPaint = null

# Toggle for painting walls vs ground in VERTEX_PAINTING mode
var paint_walls_mode : bool = false:
	set(value):
		paint_walls_mode = value

# VERTEX_PAINTING target: 0 = ground slots, 1 = wall slots, 2 = edge transitions
var paint_mode : int = 0

# Edge kind written by transition painting (matches MarchingSquaresTerrainChunk.EdgeKind)
var transition_kind : int = 0

var vertex_color_idx : int = 0:
	set(vc_idx):
		vertex_color_idx = vc_idx
		_set_vertex_colors(vc_idx)
var vertex_color_0 : Color = Color(1.0, 0.0, 0.0, 0.0)
var vertex_color_1 : Color = Color(1.0, 0.0, 0.0, 0.0)
#endregion

#region draw-related vars
# A dictionary with keys for each tile that is currently being drawn to with the brush 
# In brush mode, the value is the height that preview was drawn to, aka the height BEFORE it is set
# In ground texture mode, the value is the color of the point BEFORE the draw
var current_draw_pattern : Dictionary

var terrain_hovered : bool
var is_chunk_plane_hovered : bool
var current_hovered_chunk : Vector2i

# True if the mouse is currently held down to draw
var is_drawing : bool

# When the brush draws, if the gizmo sees the draw height is not set, it will set the draw height
var draw_height_set : bool

# Height of the current pattern that is being drawn at for the brush tool
var draw_height : float

# Is set to true when the user clicks on a tile that is part of the current draw pattern, will enter heightdrag setting mode
var is_setting : bool

var is_making_bridge : bool
var bridge_start_pos : Vector3

# The point where the height drag started
var base_position : Vector3
#endregion

#region raycast variables
# Use script-wide variables to provide data to the physics process function
var raycast_queued := false
var ray_origin : Vector3
var ray_dir : Vector3
var ray_camera : Camera3D
var queued_ray_result := {}
#endregion


func _enter_tree():
	instance = self
	call_deferred("_deferred_enter_tree")
	
	print_rich("Welcome to [color=MEDIUM_ORCHID][url=https://www.youtube.com/@yugen_seishin]Yūgen[/url][/color]'s [wave]Marching Squares Terrain Authoring Toolkit[/wave]\nThis plugin is under MIT license")


func _deferred_enter_tree() -> void:
	if not _safe_initialize():
		push_error("Failed to initialize plugin: " + initialization_error)
	else:
		print_verbose("[MarchingSquaresTerrainPlugin] initialized succesfully!")


func _safe_initialize() -> bool:
	if is_initialized:
		return true
	
	if not EngineWrapper.instance.is_editor():
		initialization_error = "Plugin was initialized during runtime"
		return false
	
	if not EditorInterface:
		initialization_error = "No EditorInterface detected"
		return false
	
	if not get_tree():
		initialization_error = "No tree detected while initializing"
		return false
	
	var terrain_script := preload("uid://cddg1xr5hye1d")
	var chunk_script := preload("uid://cql4d8s5t5xcx")
	var terrain_icon := preload("uid://jfugomwkrm54")
	var chunk_icon := preload("uid://dj8y22ded0j8r")
	
	if terrain_script and chunk_script:
		add_custom_type("MarchingSquaresTerrain", "Node3D", terrain_script, terrain_icon)
		add_custom_type("MarchingSquaresTerrainChunk", "MeshInstance3D", chunk_script, chunk_icon)
	else:
		initialization_error = "Failed to load algorithm scripts"
		return false
	
	if gizmo_plugin:
		add_node_3d_gizmo_plugin(gizmo_plugin)
	else:
		initialization_error = "Failed to create gizmo plugin"
		return false
	
	if not ui:
		ui = UI.new()
		if ui:
			ui.plugin = self
			add_child(ui)
		else:
			initialization_error = "Failed to create UI system"
			return false
	
	is_initialized = true
	return true


func _exit_tree():
	if ui:
		ui.queue_free()
		ui = null
	
	remove_custom_type("MarchingSquaresTerrain")
	remove_custom_type("MarchingSquaresTerrainChunk")
	
	if gizmo_plugin:
		remove_node_3d_gizmo_plugin(gizmo_plugin)
		gizmo_plugin = null
	
	is_initialized = false
	initialization_error = ""


func _ready():
	BRUSH_RADIUS_MATERIAL.set_shader_parameter("falloff_visible", falloff)


func _queue_raycast(origin: Vector3, dir: Vector3, cam: Camera3D) -> void:
	ray_origin = origin
	ray_dir = dir
	ray_camera = cam
	raycast_queued = true


func _on_chunk_dimensions_changed(value: Vector3i):
	brush_size *= ((value.x / 33) + (value.y / 33)) / 2.0

func _physics_process(delta: float) -> void:
	# Raycast inside the physics process function to prevent
	# crashes when "run physics on a different thread" is enabled.
	if not raycast_queued:
		return
	raycast_queued = false
	
	var world_3d := ray_camera.get_world_3d()
	var space_state := PhysicsServer3D.space_get_direct_state(world_3d.space)
	
	var ray_length := 10000.0 # Adjust ray length as needed
	var end := ray_origin + ray_dir * ray_length
	var collision_mask = 16 # only terrain
	var query := PhysicsRayQueryParameters3D.create(ray_origin, end, collision_mask)
	
	queued_ray_result = space_state.intersect_ray(query)


#region input-handlers

func _edit(object: Object) -> void:
	if not is_initialized:
		push_error("Plugin not yet initialized, calling _safe_initialize() as failsafe")
		if not _safe_initialize():
			push_error("Failed to initialize plugin for editing")
			return
	if object is MarchingSquaresTerrain:
		if ui:
			ui.set_visible(true)
			current_terrain_node = object
			
			# Sync plugin's preset from the selected terrain's saved preset
			# This ensures each terrain keeps its own preset on selection/reload
			if not current_terrain_node.chunk_dimensions_changed.is_connected(_on_chunk_dimensions_changed):
				current_terrain_node.chunk_dimensions_changed.connect(_on_chunk_dimensions_changed)
			
			_syncing_from_terrain = true
			current_texture_preset = object.current_texture_preset
			_syncing_from_terrain = false
	else:
		if ui:
			ui.set_visible(false)
		current_draw_pattern.clear()
		is_drawing = false
		draw_height_set = false
		gizmo_plugin.clear()


# This function handles the mouse click in the 3D viewport
func _forward_3d_gui_input(camera: Camera3D, event: InputEvent) -> int:
	if not is_initialized:
		return EditorPlugin.AFTER_GUI_INPUT_PASS
	
	var selected = EditorInterface.get_selection().get_selected_nodes()
	# Only proceed if exactly 1 terrain system is selected
	if not selected or len(selected) > 1:
		return EditorPlugin.AFTER_GUI_INPUT_PASS
	
	# Handle clicks
	if event is InputEventMouseButton or event is InputEventMouseMotion:
		return handle_mouse(camera, event)
	
	return EditorPlugin.AFTER_GUI_INPUT_PASS


func _handles(object: Object) -> bool:
	if not is_initialized:
		return false
	
	return object is MarchingSquaresTerrain


func handle_hotkey(keycode: int) -> bool:
	pass
	return false


func handle_mouse(camera: Camera3D, event: InputEvent) -> int:
	terrain_hovered = false
	var terrain : MarchingSquaresTerrain = EditorInterface.get_selection().get_selected_nodes()[0]
	
	var mouse_pos := camera.get_viewport().get_mouse_position()
	
	var _ray_origin := camera.project_ray_origin(mouse_pos)
	var _ray_dir := camera.project_ray_normal(mouse_pos)
	
	var shift_held := Input.is_key_pressed(KEY_SHIFT)
	
	# If not in a settings mode, perform terrain raycast
	if mode == TerrainToolMode.BRUSH or mode == TerrainToolMode.GRASS_MASK or mode == TerrainToolMode.LEVEL or mode == TerrainToolMode.SMOOTH or mode == TerrainToolMode.BRIDGE or mode == TerrainToolMode.VERTEX_PAINTING or mode == TerrainToolMode.DEBUG_BRUSH:
		var draw_position
		var draw_area_hovered : bool = false
		
		if is_setting and draw_height_set:
			var local_ray_dir := _ray_dir * terrain.transform
			var set_plane := Plane(Vector3(local_ray_dir.x, 0, local_ray_dir.z), base_position)
			var set_position := set_plane.intersects_ray(terrain.to_local(_ray_origin), local_ray_dir)
			if set_position:
				brush_position = set_position
		
		# If there is any pattern and flatten is enabled, draw along that height plane instead of the terrain intersection
		elif not current_draw_pattern.is_empty() and flatten:
			var chunk_plane := Plane(Vector3.UP, Vector3(0, draw_height, 0))
			draw_position = chunk_plane.intersects_ray(_ray_origin, _ray_dir)
			if draw_position:
				draw_position = terrain.to_local(draw_position)
				draw_area_hovered = true
		
		else:
			# Perform the raycast to check for intersection with a physics body (terrain)
			_queue_raycast(_ray_origin, _ray_dir, camera)
			if queued_ray_result and queued_ray_result.has("position"):
				draw_position = terrain.to_local(queued_ray_result.position)
				draw_area_hovered = true
			else:
				# FALLBACK: If we didn't hit a chunk, project onto a virtual plane at draw_height
				# This allows painting onto chunks while the mouse is in "negative space"
				var fallback_height := 0.0
				if is_drawing or is_setting or not current_draw_pattern.is_empty():
					fallback_height = draw_height
				
				var virtual_plane := Plane(Vector3.UP, Vector3(0, fallback_height, 0))
				var plane_pos := virtual_plane.intersects_ray(ray_origin, ray_dir)
				if plane_pos:
					draw_position = terrain.to_local(plane_pos)
					draw_area_hovered = true
		
		# ALT or Right Click to clear the current draw pattern. Don't clear while setting
		var _right_clicked : bool = (
			event is InputEventMouseButton and 
			event.button_index == MOUSE_BUTTON_RIGHT and 
			event.pressed
		)
		
		if not is_setting:
			if _right_clicked or Input.is_key_pressed(KEY_ALT):
				current_draw_pattern.clear()
		
		# Check for terrain collision
		if draw_area_hovered:
			terrain_hovered = true
			var chunk_coords : Vector2i
			if terrain.grid_mode == 0:
				var chunk_x : int = floor(draw_position.x / (terrain.dimensions.x * terrain.cell_size.x))
				var chunk_z : int = floor(draw_position.z / (terrain.dimensions.z * terrain.cell_size.y))
				chunk_coords = Vector2i(chunk_x, chunk_z)
			else:
				var draw_hex := MSTHexMath.world_to_hex(draw_position.x, draw_position.z, terrain.hex_size)
				chunk_coords = MSTHexMath.world_to_chunk(draw_hex.x, draw_hex.y, terrain.chunk_radius)
			
			is_chunk_plane_hovered = true
			current_hovered_chunk = chunk_coords
		
		if event is InputEventMouseButton and event.button_index == MouseButton.MOUSE_BUTTON_LEFT:
			if event.is_pressed() and draw_area_hovered:
				draw_height_set = false
				if mode == TerrainToolMode.BRIDGE and not is_making_bridge:
					flatten = false
					is_making_bridge = true
					bridge_start_pos = brush_position
				if mode == TerrainToolMode.SMOOTH and falloff == false:
					falloff = true
				if (mode == TerrainToolMode.GRASS_MASK or mode == TerrainToolMode.DEBUG_BRUSH) and falloff == true:
					falloff = false
				if (mode == TerrainToolMode.GRASS_MASK or mode == TerrainToolMode.VERTEX_PAINTING or mode == TerrainToolMode.DEBUG_BRUSH) and flatten == true:
					flatten = false
				if mode == TerrainToolMode.LEVEL and Input.is_key_pressed(KEY_CTRL):
					if terrain.grid_mode == 0:
						height = brush_position.y
					else:
						height = round(brush_position.y / terrain.level_height)
				elif Input.is_key_pressed(KEY_SHIFT):
					is_drawing = true
					brush_position = draw_position
				else:
					is_setting = true
					if not flatten:
						draw_height = draw_position.y
			elif event.is_released():
				if is_making_bridge:
					is_making_bridge = false
				if is_drawing:
					is_drawing = false
					if mode == TerrainToolMode.GRASS_MASK or mode == TerrainToolMode.LEVEL or mode == TerrainToolMode.BRIDGE or mode == TerrainToolMode.DEBUG_BRUSH:
						draw_pattern(terrain)
						current_draw_pattern.clear()
					if mode == TerrainToolMode.SMOOTH or mode == TerrainToolMode.VERTEX_PAINTING:
						current_draw_pattern.clear()
				if is_setting:
					is_setting = false
					draw_pattern(terrain)
					if Input.is_key_pressed(KEY_SHIFT):
						draw_height = brush_position.y
					else:
						current_draw_pattern.clear()
			gizmo_plugin.trigger_redraw(terrain)
			return EditorPlugin.AFTER_GUI_INPUT_STOP
		
		# Adjust brush size
		if event is InputEventMouseButton and Input.is_key_pressed(KEY_SHIFT):
			var size_scale_factor : float
			if terrain.grid_mode == 0:
				var cell_scale_factor := clamp(((terrain.cell_size.x + terrain.cell_size.y) / 4.0), 0.3, 1.0)
				var dimensions_scale_factor := clamp((((terrain.dimensions.x / 33) + (terrain.dimensions.z / 33)) / 2.0), 0.5, 2.0)
				size_scale_factor = dimensions_scale_factor * cell_scale_factor
			else:
				size_scale_factor = clamp(terrain.hex_size / 2.0, 0.3, 1.0)
			var factor : float = event.factor if event.factor else 1
			if event.button_index == MOUSE_BUTTON_WHEEL_UP:
				brush_size += (0.5 * size_scale_factor) * factor
				if brush_size > 50 * size_scale_factor:
					brush_size = 50 * size_scale_factor
				gizmo_plugin.trigger_redraw(terrain)
				return EditorPlugin.AFTER_GUI_INPUT_STOP
			elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
				brush_size -= (0.5 * size_scale_factor) * factor
				if brush_size < 1.0 * size_scale_factor:
					brush_size = 1.0 * size_scale_factor
				gizmo_plugin.trigger_redraw(terrain)
				return EditorPlugin.AFTER_GUI_INPUT_STOP
		
		if draw_area_hovered and event is InputEventMouseMotion:
			brush_position = draw_position
			if is_drawing and (mode == TerrainToolMode.SMOOTH or mode == TerrainToolMode.VERTEX_PAINTING or mode == TerrainToolMode.GRASS_MASK):
				draw_pattern(terrain)
				current_draw_pattern.clear()
		
		gizmo_plugin.trigger_redraw(terrain)
		return EditorPlugin.AFTER_GUI_INPUT_PASS
	
	# Check for hovering over/clicking a new chunk
	var chunk_plane := Plane(Vector3.UP, Vector3.ZERO)
	var intersection := chunk_plane.intersects_ray(_ray_origin, _ray_dir)
	
	if intersection:
		var chunk_coords : Vector2i
		if terrain.grid_mode == 0:
			var chunk_x : int = floor(intersection.x / ((terrain.dimensions.x - 1) * terrain.cell_size.x))
			var chunk_z : int = floor(intersection.z / ((terrain.dimensions.z - 1) * terrain.cell_size.y))
			chunk_coords = Vector2i(chunk_x, chunk_z)
		else:
			var hover_hex := MSTHexMath.world_to_hex(intersection.x, intersection.z, terrain.hex_size)
			chunk_coords = MSTHexMath.world_to_chunk(hover_hex.x, hover_hex.y, terrain.chunk_radius)
		var chunk = terrain.chunks.get(chunk_coords)
		
		current_hovered_chunk = chunk_coords
		is_chunk_plane_hovered = true
		
		# On click, add or remove chunk if in chunk_management mode
		if mode == TerrainToolMode.CHUNK_MANAGEMENT and event is InputEventMouseButton and event.is_pressed() and event.button_index == MouseButton.MOUSE_BUTTON_LEFT:
			# Select chunk
			if Input.is_key_pressed(KEY_CTRL):
				selected_chunk = terrain.chunks.get(current_hovered_chunk)
				ui.tool_attributes.show_tool_attributes(TerrainToolMode.CHUNK_MANAGEMENT)
				ui.tool_attributes.selected_chunk = selected_chunk
			
			# Remove chunk
			elif chunk:
				var removed_chunk = terrain.chunks[chunk_coords]
				get_undo_redo().create_action("remove chunk")
				get_undo_redo().add_do_method(terrain, "remove_chunk_from_tree", chunk_coords.x, chunk_coords.y, self)
				get_undo_redo().add_undo_method(terrain, "add_chunk", chunk_coords, removed_chunk, self)
				get_undo_redo().commit_action()
				return EditorPlugin.AFTER_GUI_INPUT_STOP
			
			# Add new chunk
			elif not chunk:
				# Can add a new chunk here if there is a neighbouring non-empty chunk
				# Also add if there are no chunks at all in the current terrain system
				var can_add_empty : bool
				if terrain.grid_mode == 0:
					can_add_empty = terrain.chunks.is_empty() or terrain.has_chunk(chunk_coords.x - 1, chunk_coords.y) or terrain.has_chunk(chunk_coords.x + 1, chunk_coords.y) or terrain.has_chunk(chunk_coords.x, chunk_coords.y - 1) or terrain.has_chunk(chunk_coords.x, chunk_coords.y + 1)
				else:
					can_add_empty = terrain.chunks.is_empty()
					for dir in range(6):
						if can_add_empty:
							break
						var neighbor := chunk_coords + MSTHexMath.neighbor_offset(dir)
						can_add_empty = terrain.has_chunk(neighbor.x, neighbor.y)
				if can_add_empty:
					get_undo_redo().create_action("add chunk")
					get_undo_redo().add_do_method(terrain, "add_new_chunk", chunk_coords.x, chunk_coords.y, self)
					get_undo_redo().add_undo_method(terrain, "remove_chunk", chunk_coords.x, chunk_coords.y, self)
					get_undo_redo().commit_action()
					return EditorPlugin.AFTER_GUI_INPUT_STOP
		
		gizmo_plugin.trigger_redraw(terrain)
	else:
		is_chunk_plane_hovered = false
	
	# Consume clicks but allow other click / mouse motion types to reach the gui, for camera movement, etc
	if event is InputEventMouseButton and event.is_pressed() and event.button_index == MouseButton.MOUSE_BUTTON_LEFT:
		return EditorPlugin.AFTER_GUI_INPUT_STOP
	
	return EditorPlugin.AFTER_GUI_INPUT_PASS

#endregion

#region draw-related functions

# Calculates brush pattern and updates current_draw_pattern
func update_draw_pattern(b_pos: Vector3):
	if current_terrain_node and current_terrain_node.grid_mode == 0:
		_update_draw_pattern_squares(b_pos)
		return
	var terrain_system : MarchingSquaresTerrain = current_terrain_node
	
	var brush_hexes : Dictionary = BrushPatternCalculator.hexes_in_brush(
		terrain_system, b_pos, brush_size, current_brush_index, falloff, falloff_curve
	)
	
	for chunk_coords : Vector2i in brush_hexes:
		if not current_draw_pattern.has(chunk_coords):
			current_draw_pattern[chunk_coords] = {}
		var chunk_dict : Dictionary = brush_hexes[chunk_coords]
		for local_hex : Vector2i in chunk_dict:
			var sample : float = chunk_dict[local_hex]
			# Store largest sample
			if current_draw_pattern[chunk_coords].has(local_hex):
				var prev_sample = current_draw_pattern[chunk_coords][local_hex]
				if sample > prev_sample:
					current_draw_pattern[chunk_coords][local_hex] = sample
			else:
				current_draw_pattern[chunk_coords][local_hex] = sample


# Resolve the edge nearest to the cursor for transition painting.
# Edges are owned by the higher hex of the pair, so the write is redirected to the
# neighbor's opposite-direction edge when the picked hex is the lower one.
# Returns [owner_chunk_coords, owner_local_hex, owner_dir].
func _get_transition_edge(terrain: MarchingSquaresTerrain, chunk_coords: Vector2i, local_hex: Vector2i) -> Array:
	var center := MSTHexMath.chunk_center(chunk_coords.x, chunk_coords.y, terrain.chunk_radius)
	var global_hex := center + local_hex
	var hex_world := MSTHexMath.hex_to_world(global_hex.x, global_hex.y, terrain.hex_size)
	var to_cursor := Vector2(brush_position.x, brush_position.z) - hex_world
	var dir := posmod(int(round(rad_to_deg(atan2(to_cursor.y, to_cursor.x)) / 60.0)), 6)
	
	var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
	var neighbor_global := global_hex + MSTHexMath.neighbor_offset(dir)
	var neighbor := terrain.get_hex_global(neighbor_global.x, neighbor_global.y)
	
	# Void neighbor: the picked hex owns the edge (the wall drops to the baseline)
	if neighbor.is_empty():
		return [chunk_coords, local_hex, dir]
	
	var neighbor_elevation : int = neighbor.get("elevation", 0)
	if neighbor_elevation > chunk.get_elevation(local_hex):
		# The higher hex owns the edge: redirect to its opposite-direction edge
		var owner_chunk_coords := MSTHexMath.world_to_chunk(neighbor_global.x, neighbor_global.y, terrain.chunk_radius)
		if not terrain.chunks.has(owner_chunk_coords):
			return [chunk_coords, local_hex, dir]
		return [owner_chunk_coords, MSTHexMath.hex_to_local(neighbor_global.x, neighbor_global.y, terrain.chunk_radius), posmod(dir + 3, 6)]
	return [chunk_coords, local_hex, dir]


func draw_pattern(terrain: MarchingSquaresTerrain):
	if terrain.grid_mode == 0:
		_draw_pattern_squares(terrain)
		return
	var undo_redo := MarchingSquaresTerrainPlugin.instance.get_undo_redo()
	
	var pattern := {}
	var restore_pattern := {}
	
	for draw_chunk_coords: Vector2i in current_draw_pattern.keys():
		pattern[draw_chunk_coords] = {}
		restore_pattern[draw_chunk_coords] = {}
		var draw_chunk_dict = current_draw_pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_hex_coords: Vector2i in draw_chunk_dict:
			var sample : float = clamp(draw_chunk_dict[draw_hex_coords], 0.001, 0.999)
			var restore_value
			var draw_value
			if mode == TerrainToolMode.GRASS_MASK:
				restore_value = chunk.get_grass(draw_hex_coords)
				draw_value = not should_mask_grass
			elif mode == TerrainToolMode.LEVEL:
				restore_value = chunk.get_elevation(draw_hex_coords)
				draw_value = int(round(lerpf(restore_value, height, sample)))
			elif mode == TerrainToolMode.SMOOTH:
				# Weighted average of the pattern hexes' levels
				var total := 0
				var count := 0
				for smooth_chunk_coords in current_draw_pattern.keys():
					var smooth_chunk : MarchingSquaresTerrainChunk = terrain.chunks[smooth_chunk_coords]
					for smooth_hex in current_draw_pattern[smooth_chunk_coords]:
						total += smooth_chunk.get_elevation(smooth_hex)
						count += 1
				var avg_level := float(total) / maxf(count, 1)
				restore_value = chunk.get_elevation(draw_hex_coords)
				draw_value = int(round(lerpf(restore_value, avg_level, sample * strength)))
			elif mode == TerrainToolMode.BRIDGE:
				var b_end := Vector2(brush_position.x, brush_position.z)
				var b_start := Vector2(bridge_start_pos.x, bridge_start_pos.z)
				var bridge_length := (b_end - b_start).length()
				if bridge_length < 0.5 or draw_chunk_dict.size() < 3: # Skip small bridges so the terrain doesn't glitch
					return
				
				# Convert hex to world-space
				var center := MSTHexMath.chunk_center(draw_chunk_coords.x, draw_chunk_coords.y, terrain.chunk_radius)
				var hex_world := MSTHexMath.hex_to_world(center.x + draw_hex_coords.x, center.y + draw_hex_coords.y, terrain.hex_size)
				
				# Calculate the 2D bridge direction vector
				var bridge_dir := (b_end - b_start) / bridge_length
				var progress := clamp((hex_world - b_start).dot(bridge_dir) / bridge_length, 0.0, 1.0)
				
				if ease_value != -1.0:
					progress = ease(progress, ease_value)
				var start_level := bridge_start_pos.y / terrain.level_height
				var end_level := brush_position.y / terrain.level_height
				
				restore_value = chunk.get_elevation(draw_hex_coords)
				draw_value = int(round(lerpf(start_level, end_level, progress)))
			elif mode == TerrainToolMode.VERTEX_PAINTING:
				if paint_mode == 2:
					# Transition painting: set the edge nearest to the cursor, owned by the higher hex
					var edge_info := _get_transition_edge(terrain, draw_chunk_coords, draw_hex_coords)
					var owner_chunk_coords : Vector2i = edge_info[0]
					var owner_local : Vector2i = edge_info[1]
					var owner_dir : int = edge_info[2]
					var owner_chunk : MarchingSquaresTerrainChunk = terrain.chunks[owner_chunk_coords]
					if not pattern.has(owner_chunk_coords):
						pattern[owner_chunk_coords] = {}
					if not restore_pattern.has(owner_chunk_coords):
						restore_pattern[owner_chunk_coords] = {}
					restore_value = owner_chunk.get_edge_transitions(owner_local)
					draw_value = MarchingSquaresTerrainChunk.set_edge_kind(restore_value, owner_dir, transition_kind)
					restore_pattern[owner_chunk_coords][owner_local] = restore_value
					pattern[owner_chunk_coords][owner_local] = draw_value
					continue
				if paint_mode == 1 or paint_walls_mode:
					restore_value = chunk.get_wall_slot(draw_hex_coords)
				else:
					restore_value = chunk.get_ground_slot(draw_hex_coords)
				draw_value = vertex_color_idx
			elif mode == TerrainToolMode.DEBUG_BRUSH:
				print("DEBUG INFO: chunk = " + str(draw_chunk_coords) +
					", local hex = " + str(draw_hex_coords) +
					", elevation = " + str(chunk.get_elevation(draw_hex_coords)) +
					", ground slot = " + str(chunk.get_ground_slot(draw_hex_coords)) +
					", wall slot = " + str(chunk.get_wall_slot(draw_hex_coords)) +
					", grass = " + str(chunk.get_grass(draw_hex_coords)) +
					", edges = " + str(chunk.get_edge_transitions(draw_hex_coords)))
				continue
			else: # Brush tool
				restore_value = chunk.get_elevation(draw_hex_coords)
				var target_level : int
				if flatten:
					target_level = int(round(brush_position.y / terrain.level_height))
				else:
					target_level = restore_value + int(round((brush_position.y - draw_height) / terrain.level_height))
				draw_value = int(round(lerpf(restore_value, target_level, sample)))
			
			restore_pattern[draw_chunk_coords][draw_hex_coords] = restore_value
			pattern[draw_chunk_coords][draw_hex_coords] = draw_value
	if mode == TerrainToolMode.DEBUG_BRUSH:
		return
	
	if mode == TerrainToolMode.VERTEX_PAINTING:
		# Per-hex slot painting (ground or walls) or per-edge transition painting
		var slot_key := "edges" if paint_mode == 2 else ("wall_slot" if paint_mode == 1 or paint_walls_mode else "ground_slot")
		var do_patterns := {slot_key: pattern}
		var undo_patterns := {slot_key: restore_pattern}
		
		var action_name := "terrain transition paint" if paint_mode == 2 else ("terrain wall paint" if paint_mode == 1 or paint_walls_mode else "terrain vertex paint")
		undo_redo.create_action(action_name)
		undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
		undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
		undo_redo.commit_action()
	elif mode == TerrainToolMode.GRASS_MASK:
		undo_redo.create_action("terrain grass mask draw")
		undo_redo.add_do_method(self, "draw_grass_mask_pattern_action", terrain, pattern)
		undo_redo.add_undo_method(self, "draw_grass_mask_pattern_action", terrain, restore_pattern)
		undo_redo.commit_action()
	else:
		# Handle BRUSH, LEVEL, SMOOTH, BRIDGE modes
		if current_quick_paint:
			# QUICK PAINT MODE: height + wall slot + grass + ground slot as ONE atomic undo/redo action
			var wall_slot_pattern := {}
			var wall_slot_restore := {}
			var ground_slot_pattern := {}
			var ground_slot_restore := {}
			var grass_pattern := {}
			var grass_restore := {}
			
			for chunk_coords in pattern:
				wall_slot_pattern[chunk_coords] = {}
				wall_slot_restore[chunk_coords] = {}
				ground_slot_pattern[chunk_coords] = {}
				ground_slot_restore[chunk_coords] = {}
				grass_pattern[chunk_coords] = {}
				grass_restore[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for hex_coords in pattern[chunk_coords]:
					wall_slot_restore[chunk_coords][hex_coords] = chunk.get_wall_slot(hex_coords)
					wall_slot_pattern[chunk_coords][hex_coords] = current_quick_paint.wall_texture_slot
					ground_slot_restore[chunk_coords][hex_coords] = chunk.get_ground_slot(hex_coords)
					ground_slot_pattern[chunk_coords][hex_coords] = current_quick_paint.ground_texture_slot
					grass_restore[chunk_coords][hex_coords] = chunk.get_grass(hex_coords)
					grass_pattern[chunk_coords][hex_coords] = current_quick_paint.has_grass
			
			# Create ONE composite action for the whole quick paint
			var do_patterns := {
				"height": pattern,
				"wall_slot": wall_slot_pattern,
				"grass_mask": grass_pattern,
				"ground_slot": ground_slot_pattern
			}
			var undo_patterns := {
				"height": restore_pattern,
				"wall_slot": wall_slot_restore,
				"grass_mask": grass_restore,
				"ground_slot": ground_slot_restore
			}
			
			undo_redo.create_action("terrain brush with quick paint")
			undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
			undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
			undo_redo.commit_action()
		else:
			# NON-QUICK PAINT MODE: Apply height + default wall slot
			var wall_slot_pattern := {}
			var wall_slot_restore := {}
			
			for chunk_coords in pattern:
				wall_slot_pattern[chunk_coords] = {}
				wall_slot_restore[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for hex_coords in pattern[chunk_coords]:
					wall_slot_restore[chunk_coords][hex_coords] = chunk.get_wall_slot(hex_coords)
					wall_slot_pattern[chunk_coords][hex_coords] = terrain.default_wall_texture
			
			# Create composite action with height + wall slots
			var do_patterns := {
				"height": pattern,
				"wall_slot": wall_slot_pattern
			}
			var undo_patterns := {
				"height": restore_pattern,
				"wall_slot": wall_slot_restore
			}
			
			undo_redo.create_action("terrain height draw")
			undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
			undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
			undo_redo.commit_action()


# For each hex in pattern, set the elevation level
func draw_height_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_coords: Vector2i in draw_chunk_dict:
			var v = draw_chunk_dict[draw_coords]
			if terrain.grid_mode == 0 or v is float:
				chunk.draw_height(draw_coords.x, draw_coords.y, float(v))
			else:
				chunk.draw_elevation(draw_coords, int(v))
		chunk.regenerate_mesh()


func draw_grass_mask_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_coords: Vector2i in draw_chunk_dict:
			var v = draw_chunk_dict[draw_coords]
			if v is Color:
				chunk.draw_grass_mask(draw_coords.x, draw_coords.y, v)
			else:
				chunk.draw_grass(draw_coords, bool(v))
		chunk.regenerate_mesh()


# Applies all terrain patterns  (for quick paint brush and vertex painting operations)
func apply_composite_pattern_action(terrain: MarchingSquaresTerrain, patterns: Dictionary) -> void:
	var affected_chunks : Dictionary = {}  # chunk_coords -> chunk reference
	
	var composite_disabled := false
	if mode == TerrainToolMode.SMOOTH and current_quick_paint == null:
		composite_disabled = true
	
	# Apply wall colors FIRST (squares mode, before height changes that create ridge vertices)
	if patterns.has("wall_color_0") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.wall_color_0:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for cell_coords: Vector2i in patterns.wall_color_0[chunk_coords]:
					chunk.draw_wall_color_0(cell_coords.x, cell_coords.y, patterns.wall_color_0[chunk_coords][cell_coords])
	
	if patterns.has("wall_color_1") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.wall_color_1:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for cell_coords: Vector2i in patterns.wall_color_1[chunk_coords]:
					chunk.draw_wall_color_1(cell_coords.x, cell_coords.y, patterns.wall_color_1[chunk_coords][cell_coords])
	
	# Apply wall slots FIRST (hex mode, before height changes that create cliffs)
	if patterns.has("wall_slot") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.wall_slot:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for hex_coords: Vector2i in patterns.wall_slot[chunk_coords]:
					chunk.draw_wall_slot(hex_coords, patterns.wall_slot[chunk_coords][hex_coords])
	
	# Apply height changes
	if patterns.has("height"):
		for chunk_coords: Vector2i in patterns.height:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for hex_coords: Vector2i in patterns.height[chunk_coords]:
					chunk.draw_elevation(hex_coords, patterns.height[chunk_coords][hex_coords])
	
	# Apply grass mask
	if patterns.has("grass_mask") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.grass_mask:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for hex_coords: Vector2i in patterns.grass_mask[chunk_coords]:
					chunk.draw_grass(hex_coords, patterns.grass_mask[chunk_coords][hex_coords])
	
	# Apply edge transitions
	if patterns.has("edges") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.edges:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for hex_coords: Vector2i in patterns.edges[chunk_coords]:
					chunk.draw_edge_transitions(hex_coords, patterns.edges[chunk_coords][hex_coords])
	
	# Apply ground colors LAST (squares mode)
	if patterns.has("color_0") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.color_0:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for cell_coords: Vector2i in patterns.color_0[chunk_coords]:
					chunk.draw_color_0(cell_coords.x, cell_coords.y, patterns.color_0[chunk_coords][cell_coords])
	
	if patterns.has("color_1") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.color_1:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for cell_coords: Vector2i in patterns.color_1[chunk_coords]:
					chunk.draw_color_1(cell_coords.x, cell_coords.y, patterns.color_1[chunk_coords][cell_coords])
	
	# Apply ground slots LAST (hex mode)
	if patterns.has("ground_slot") and not composite_disabled:
		for chunk_coords: Vector2i in patterns.ground_slot:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks.get(chunk_coords)
			if chunk:
				affected_chunks[chunk_coords] = chunk
				for hex_coords: Vector2i in patterns.ground_slot[chunk_coords]:
					chunk.draw_ground_slot(hex_coords, patterns.ground_slot[chunk_coords][hex_coords])
	
	# Regenerate mesh ONCE for each affected chunk (instead of 6 times!)
	for chunk in affected_chunks.values():
		chunk.regenerate_mesh()

#endregion

#region vertex/texture setters and getters

func _set_new_textures(_preset: MarchingSquaresTexturePreset) -> void:
	if _preset == null:
		_preset = EMPTY_TEXTURE_PRESET.duplicate()
	
	# Set BatchUpdate flag to avoid indivudal setters triggering updates
	current_terrain_node.is_batch_updating = true
	
	for i in range(5): # The range is 5 because MarchingSquaresTextureList has 5 export variables (terrain textures, texture scales, grass sprites, grass colors, has_grass)
		match i:
			0: # terrain_textures (unified for both floor and wall painting)
				for i_tex in range(_preset.new_textures.terrain_textures.size()):
					var tex : Texture2D = _preset.new_textures.terrain_textures[i_tex]
					match i_tex:
						0:
							current_terrain_node.texture_1 = tex
						1:
							current_terrain_node.texture_2 = tex
						2:
							current_terrain_node.texture_3 = tex
						3:
							current_terrain_node.texture_4 = tex
						4:
							current_terrain_node.texture_5 = tex
						5:
							current_terrain_node.texture_6 = tex
						6:
							current_terrain_node.texture_7 = tex
						7:
							current_terrain_node.texture_8 = tex
						8:
							current_terrain_node.texture_9 = tex
						9:
							current_terrain_node.texture_10 = tex
						10:
							current_terrain_node.texture_11 = tex
						11:
							current_terrain_node.texture_12 = tex
						12:
							current_terrain_node.texture_13 = tex
						13:
							current_terrain_node.texture_14 = tex
						14: # texture_15 is reserved for VOID
							current_terrain_node.texture_15 = tex
			1: # texture_scales
				for i_tex_scale in range(_preset.new_textures.texture_scales.size()):
					var scale : float = _preset.new_textures.texture_scales[i_tex_scale]
					match i_tex_scale:
						0:
							current_terrain_node.texture_scale_1 = scale
						1:
							current_terrain_node.texture_scale_2 = scale
						2:
							current_terrain_node.texture_scale_3 = scale
						3:
							current_terrain_node.texture_scale_4 = scale
						4:
							current_terrain_node.texture_scale_5 = scale
						5:
							current_terrain_node.texture_scale_6 = scale
						6:
							current_terrain_node.texture_scale_7 = scale
						7:
							current_terrain_node.texture_scale_8 = scale
						8:
							current_terrain_node.texture_scale_9 = scale
						9:
							current_terrain_node.texture_scale_10 = scale
						10:
							current_terrain_node.texture_scale_11 = scale
						11:
							current_terrain_node.texture_scale_12 = scale
						12:
							current_terrain_node.texture_scale_13 = scale
						13:
							current_terrain_node.texture_scale_14 = scale
						14:
							current_terrain_node.texture_scale_15 = scale
			2: # grass_sprites
				for i_grass_tex in range(_preset.new_textures.grass_sprites.size()):
					var tex : Texture2D = _preset.new_textures.grass_sprites[i_grass_tex]
					if tex == null:
						continue
					match i_grass_tex:
						0:
							current_terrain_node.grass_sprite_tex_1 = tex
						1:
							current_terrain_node.grass_sprite_tex_2 = tex
						2:
							current_terrain_node.grass_sprite_tex_3 = tex
						3:
							current_terrain_node.grass_sprite_tex_4 = tex
						4:
							current_terrain_node.grass_sprite_tex_5 = tex
						5:
							current_terrain_node.grass_sprite_tex_6 = tex
			3: # grass_colors
				for i_grass_col in range(_preset.new_textures.grass_colors.size()):
					var col : Color = _preset.new_textures.grass_colors[i_grass_col]
					if col == null:
						continue
					match i_grass_col:
						0:
							current_terrain_node.texture_albedo_1 = col
						1:
							current_terrain_node.texture_albedo_2 = col
						2:
							current_terrain_node.texture_albedo_3 = col
						3:
							current_terrain_node.texture_albedo_4 = col
						4:
							current_terrain_node.texture_albedo_5 = col
						5:
							current_terrain_node.texture_albedo_6 = col
			4: # has_grass
				for i_has_grass in range(_preset.new_textures.has_grass.size()):
					var val : bool = _preset.new_textures.has_grass[i_has_grass]
					match i_has_grass:
						0:
							current_terrain_node.tex2_has_grass = val
						1:
							current_terrain_node.tex3_has_grass = val
						2:
							current_terrain_node.tex4_has_grass = val
						3:
							current_terrain_node.tex5_has_grass = val
						4:
							current_terrain_node.tex6_has_grass = val
	
	vp_texture_names.texture_names = _preset.new_tex_names.texture_names
	
	# Apply a batch update
	current_terrain_node.force_batch_update()
	
	# Mark scene as modified so user knows to save
	EditorInterface.mark_scene_as_unsaved()
	
	# Store current preset
	current_terrain_node.current_texture_preset = _preset
	
	# Set batch update to false, to allow setters to work individually
	current_terrain_node.is_batch_updating = false
	
	# Ensure the Editor is updated live (trick it to redraw - There might be an easier way but this works)
	EditorInterface.inspect_object(current_terrain_node)

#endregion


#region SQUARES mode brush logic (merged from original)
## Original marching-squares brush pipeline, used when terrain.grid_mode == SQUARES.

func _update_draw_pattern_squares(b_pos: Vector3):
	var terrain_system : MarchingSquaresTerrain = current_terrain_node
	
	var bounds := BrushPatternCalculator.calculate_bounds_squares(b_pos, brush_size, terrain_system)
	var max_distance : float = BrushPatternCalculator.calculate_max_distance(brush_size, current_brush_index)
	var brush_pos : Vector2 = Vector2(b_pos.x, b_pos.z)
	
	for chunk_z in range(bounds.chunk_tl.y, bounds.chunk_br.y + 1):
		for chunk_x in range(bounds.chunk_tl.x, bounds.chunk_br.x + 1):
			var cursor_chunk_coords : Vector2i = Vector2i(chunk_x, chunk_z)
			if not terrain_system.chunks.has(cursor_chunk_coords):
				continue
			
			var cell_range : Dictionary = BrushPatternCalculator.get_cell_range_for_chunk(cursor_chunk_coords, bounds, terrain_system)
			
			for z in range(cell_range.z_min, cell_range.z_max):
				for x in range(cell_range.x_min, cell_range.x_max):
					var cursor_cell_coords : Vector2i = Vector2i(x, z)
					var world_pos : Vector2 = BrushPatternCalculator.cell_to_world_pos(cursor_chunk_coords, cursor_cell_coords, terrain_system)
					
					var sample : float = BrushPatternCalculator.calculate_falloff_sample(
						world_pos, brush_pos, brush_size, current_brush_index,
						max_distance, falloff, falloff_curve
					)
					
					if sample < 0:
						continue  # Outside brush
					
					# Store largest sample
					if not current_draw_pattern.has(cursor_chunk_coords):
						current_draw_pattern[cursor_chunk_coords] = {}
					if current_draw_pattern[cursor_chunk_coords].has(cursor_cell_coords):
						var prev_sample = current_draw_pattern[cursor_chunk_coords][cursor_cell_coords]
						if sample > prev_sample:
							current_draw_pattern[cursor_chunk_coords][cursor_cell_coords] = sample
					else:
						current_draw_pattern[cursor_chunk_coords][cursor_cell_coords] = sample



func _draw_pattern_squares(terrain: MarchingSquaresTerrain):
	var undo_redo := MarchingSquaresTerrainPlugin.instance.get_undo_redo()
	
	var pattern := {}
	var pattern_cc := {}
	var restore_pattern := {}
	var restore_pattern_cc := {}
	
	# Ensure points on both sides of chunk borders are updated
	var first_chunk = null
	for draw_chunk_coords: Vector2i in current_draw_pattern.keys():
		if first_chunk == null:
			first_chunk = draw_chunk_coords
		pattern[draw_chunk_coords] = {}
		restore_pattern[draw_chunk_coords] = {}
		pattern_cc[draw_chunk_coords] = {}
		restore_pattern_cc[draw_chunk_coords] = {}
		var draw_chunk_dict = current_draw_pattern[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
			var sample : float = clamp(draw_chunk_dict[draw_cell_coords], 0.001, 0.999)
			var restore_value
			var draw_value
			var restore_value_cc
			var draw_value_cc
			if mode == TerrainToolMode.GRASS_MASK:
				restore_value = chunk.get_grass_mask(draw_cell_coords)
				draw_value = Color(0.0, 0.0, 0.0, 0.0) if should_mask_grass else Color(1.0, 0.0, 0.0, 0.0)
			elif mode == TerrainToolMode.LEVEL:
				restore_value = chunk.get_height(draw_cell_coords)
				draw_value = lerp(restore_value, height, sample)
			elif mode == TerrainToolMode.SMOOTH:
				var heights : Array[float] = []
				var global_cells : Array[Vector2i] = []
				
				for chunk_coords in current_draw_pattern.keys():
					var chunk_dict = current_draw_pattern[chunk_coords]
					for cell_coords in chunk_dict.keys():
						var global_x = chunk_coords.x * terrain.dimensions.x + cell_coords.x
						var global_y = chunk_coords.y * terrain.dimensions.z + cell_coords.y
						global_cells.append(Vector2i(global_x, global_y))
				
				for global_cell in global_cells:
					var current_chunk_coords := Vector2i(floor(float(global_cell.x) / terrain.dimensions.x), floor(float(global_cell.y) / terrain.dimensions.z))
					if not terrain.chunks.has(current_chunk_coords):
						continue
					var current_chunk = terrain.chunks[current_chunk_coords]
					var local_cell := Vector2i(posmod(global_cell.x, terrain.dimensions.x), posmod(global_cell.y, terrain.dimensions.z))
					heights.append(current_chunk.get_height(local_cell))
				
				var avg_height := 0.0
				for h in heights:
					avg_height += h
				avg_height /= heights.size()
				
				for global_cell in global_cells:
					var current_chunk_coords := Vector2i(floor(float(global_cell.x) / terrain.dimensions.x), floor(float(global_cell.y) / terrain.dimensions.z))
					if not terrain.chunks.has(current_chunk_coords):
						continue
					var current_chunk = terrain.chunks[current_chunk_coords]
					var local_cell := Vector2i(posmod(global_cell.x, terrain.dimensions.x), posmod(global_cell.y, terrain.dimensions.z))
					
					if not restore_pattern.has(current_chunk_coords):
						restore_pattern[current_chunk_coords] = {}
					if not pattern.has(current_chunk_coords):
						pattern[current_chunk_coords] = {}
					
					# Overwrite sample var with neighbouring chunks' data included
					sample = clamp(current_draw_pattern.get(current_chunk_coords, {}).get(local_cell, sample), 0.001, 0.999)
					restore_value = current_chunk.get_height(local_cell)
					draw_value = lerp(restore_value, avg_height, sample * strength)
					
					restore_pattern[current_chunk_coords][local_cell] = restore_value
					pattern[current_chunk_coords][local_cell] = draw_value
			elif mode == TerrainToolMode.BRIDGE:
				var b_end := Vector2(brush_position.x, brush_position.z)
				var b_start := Vector2(bridge_start_pos.x, bridge_start_pos.z)
				var bridge_length := (b_end - b_start).length()
				if bridge_length < 0.5 or draw_chunk_dict.size() < 3: # Skip small bridges so the terrain doesn't glitch
					return
				
				# Convert cell to world-space
				var global_cell := Vector2(
					(draw_chunk_coords.x * terrain.dimensions.x + draw_cell_coords.x) * terrain.cell_size.x,
					(draw_chunk_coords.y * terrain.dimensions.z + draw_cell_coords.y) * terrain.cell_size.y)
				
				if draw_chunk_coords != first_chunk:
					global_cell.x += (first_chunk.x - draw_chunk_coords.x) * terrain.cell_size.x
				if draw_chunk_coords != first_chunk:
					global_cell.y += (first_chunk.y - draw_chunk_coords.y) * terrain.cell_size.y
				
				# Calculate the 2D bridge direction vector
				var bridge_dir := (b_end - b_start) / bridge_length
				var cell_vec := global_cell - b_start
				var linear_offset := cell_vec.dot(bridge_dir)
				var progress := clamp(linear_offset / bridge_length, 0.0, 1.0)
				
				if ease_value != -1.0:
					progress = ease(progress, ease_value)
				var bridge_height := lerpf(bridge_start_pos.y, brush_position.y, progress)
				
				restore_value = chunk.get_height(draw_cell_coords)
				draw_value = bridge_height
			elif mode == TerrainToolMode.VERTEX_PAINTING:
				if paint_walls_mode:
					restore_value = chunk.get_wall_color_0(draw_cell_coords)
					restore_value_cc = chunk.get_wall_color_1(draw_cell_coords)
				else:
					restore_value = chunk.get_color_0(draw_cell_coords)
					restore_value_cc = chunk.get_color_1(draw_cell_coords)
				draw_value = vertex_color_0
				draw_value_cc = vertex_color_1
			elif mode == TerrainToolMode.DEBUG_BRUSH:
				var g_pos := chunk.to_global(Vector3(float(draw_cell_coords.x), chunk.get_height(draw_cell_coords), float(draw_cell_coords.y)))
				var normal := get_cell_normal(chunk, draw_cell_coords)
				print("DEBUG INFO: global pos = " + str(g_pos) +
					", color id = " + str(chunk.get_color_0(draw_cell_coords)) + " " + str(chunk.get_color_1(draw_cell_coords)) +
					", normal = " + str(normal))
				continue
			else: # Brush tool
				restore_value = chunk.get_height(draw_cell_coords)
				if flatten:
					draw_value = lerp(restore_value, brush_position.y, sample)
				else:
					var height_diff := brush_position.y - draw_height
					draw_value = lerp(restore_value, restore_value + height_diff, sample)
			
			restore_pattern[draw_chunk_coords][draw_cell_coords] = restore_value
			pattern[draw_chunk_coords][draw_cell_coords] = draw_value
			if mode == TerrainToolMode.VERTEX_PAINTING:
				restore_pattern_cc[draw_chunk_coords][draw_cell_coords] = restore_value_cc
				pattern_cc[draw_chunk_coords][draw_cell_coords] = draw_value_cc
	if mode == TerrainToolMode.DEBUG_BRUSH:
		return
	for draw_chunk_coords: Vector2i in current_draw_pattern.keys():
		var draw_chunk_dict = current_draw_pattern[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var sample : float = clamp(draw_chunk_dict[draw_cell_coords], 0.001, 0.999)
			for cx in range(-1, 2):
				for cz in range(-1, 2):
					if (cx == 0 and cz == 0):
						continue
					
					var adjacent_chunk_coords = Vector2i(draw_chunk_coords.x + cx, draw_chunk_coords.y + cz)
					if not terrain.chunks.has(adjacent_chunk_coords):
						continue
					
					var x : int = draw_cell_coords.x
					var z : int = draw_cell_coords.y
					
					if cx == -1:
						if x == 0: x = terrain.dimensions.x-1
						else: continue
					elif cx == 1:
						if x == terrain.dimensions.x-1: x = 0
						else: continue
					
					if cz == -1:
						if z == 0: z = terrain.dimensions.z-1
						else: continue
					elif cz == 1:
						if z == terrain.dimensions.z-1: z = 0
						else: continue
					
					var adjacent_cell_coords := Vector2i(x, z)
					
					if not pattern.has(adjacent_chunk_coords):
						pattern[adjacent_chunk_coords] = {}
					if not restore_pattern.has(adjacent_chunk_coords):
						restore_pattern[adjacent_chunk_coords] = {}
					
					var draw_value_cc
					var restore_value_cc
					if mode == TerrainToolMode.VERTEX_PAINTING:
						if not pattern_cc.has(adjacent_chunk_coords):
							pattern_cc[adjacent_chunk_coords] = {}
						if not restore_pattern_cc.has(adjacent_chunk_coords):
							restore_pattern_cc[adjacent_chunk_coords] = {}
						draw_value_cc = pattern_cc[draw_chunk_coords][draw_cell_coords]
						restore_value_cc = restore_pattern_cc[draw_chunk_coords][draw_cell_coords]
					
					var draw_value = pattern[draw_chunk_coords][draw_cell_coords]
					var restore_value = restore_pattern[draw_chunk_coords][draw_cell_coords]
					
					var adj_draw_value
					var adj_draw_value_cc
					if current_draw_pattern.has(adjacent_chunk_coords) and current_draw_pattern[adjacent_chunk_coords].has(adjacent_cell_coords) and current_draw_pattern[adjacent_chunk_coords][adjacent_cell_coords] > sample:
						adj_draw_value = pattern[adjacent_chunk_coords][adjacent_cell_coords]
						if mode == TerrainToolMode.VERTEX_PAINTING:
							adj_draw_value_cc = pattern_cc[adjacent_chunk_coords][adjacent_cell_coords]
					else:
						adj_draw_value = draw_value
						if mode == TerrainToolMode.VERTEX_PAINTING:
							adj_draw_value_cc = draw_value_cc
					
					pattern[adjacent_chunk_coords][adjacent_cell_coords] = adj_draw_value
					restore_pattern[adjacent_chunk_coords][adjacent_cell_coords] = restore_value
					if mode == TerrainToolMode.VERTEX_PAINTING:
						pattern_cc[adjacent_chunk_coords][adjacent_cell_coords] = adj_draw_value_cc
						restore_pattern_cc[adjacent_chunk_coords][adjacent_cell_coords] = restore_value_cc
	
	if mode == TerrainToolMode.VERTEX_PAINTING:
		# Standard 2D painting (ground or walls)
		# Create ONE composite action instead of 2 separate actions
		# Use wall_color keys when painting walls, color keys when painting ground
		var color_key_0 := "wall_color_0" if paint_walls_mode else "color_0"
		var color_key_1 := "wall_color_1" if paint_walls_mode else "color_1"
		var do_patterns := {
			color_key_0: pattern,
			color_key_1: pattern_cc
		}
		var undo_patterns := {
			color_key_0: restore_pattern,
			color_key_1: restore_pattern_cc
		}
		
		var action_name := "terrain wall paint" if paint_walls_mode else "terrain vertex paint"
		undo_redo.create_action(action_name)
		undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
		undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
		undo_redo.commit_action()
	elif mode == TerrainToolMode.GRASS_MASK:
		undo_redo.create_action("terrain grass mask draw")
		undo_redo.add_do_method(self, "draw_grass_mask_pattern_action", terrain, pattern)
		undo_redo.add_undo_method(self, "draw_grass_mask_pattern_action", terrain, restore_pattern)
		undo_redo.commit_action()
	else:
		# Handle BRUSH, LEVEL, SMOOTH, BRIDGE modes
		if current_quick_paint:
			# QUICK PAINT MODE: Apply all changes as ONE atomic undo/redo action
			# This fixes the issue where 6 separate actions are created
			_set_vertex_colors(current_quick_paint.wall_texture_slot)
			
			var wall_color_pattern := {}
			var wall_color_pattern_cc := {}
			var wall_color_restore := {}
			var wall_color_restore_cc := {}
			
			# First pass: collect all cells in the pattern
			for chunk_coords in pattern:
				wall_color_pattern[chunk_coords] = {}
				wall_color_pattern_cc[chunk_coords] = {}
				wall_color_restore[chunk_coords] = {}
				wall_color_restore_cc[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for cell_coords in pattern[chunk_coords]:
					wall_color_restore[chunk_coords][cell_coords] = chunk.get_wall_color_0(cell_coords)
					wall_color_restore_cc[chunk_coords][cell_coords] = chunk.get_wall_color_1(cell_coords)
					wall_color_pattern[chunk_coords][cell_coords] = vertex_color_0
					wall_color_pattern_cc[chunk_coords][cell_coords] = vertex_color_1
			
			# Second pass: expand to adjacent cells (walls appear at boundaries between cells)
			# This ensures uniform wall color by painting adjacent cells that share wall corners
			for chunk_coords in pattern:
				for cell_coords in pattern[chunk_coords]:
					# Check all 8 adjacent cells
					for dx in range(-1, 2):
						for dz in range(-1, 2):
							if dx == 0 and dz == 0:
								continue
							
							var adj_x : int = cell_coords.x + dx
							var adj_z : int = cell_coords.y + dz
							var adj_chunk_coords : Vector2i = chunk_coords
							
							# Handle chunk boundary crossings
							if adj_x < 0:
								adj_chunk_coords = Vector2i(chunk_coords.x - 1, chunk_coords.y)
								adj_x = terrain.dimensions.x - 1
							elif adj_x >= terrain.dimensions.x:
								adj_chunk_coords = Vector2i(chunk_coords.x + 1, chunk_coords.y)
								adj_x = 0
							
							if adj_z < 0:
								adj_chunk_coords = Vector2i(adj_chunk_coords.x, chunk_coords.y - 1)
								adj_z = terrain.dimensions.z - 1
							elif adj_z >= terrain.dimensions.z:
								adj_chunk_coords = Vector2i(adj_chunk_coords.x, chunk_coords.y + 1)
								adj_z = 0
							
							# Skip if chunk doesn't exist
							if not terrain.chunks.has(adj_chunk_coords):
								continue
							
							var adj_cell := Vector2i(adj_x, adj_z)
							
							# Skip if already in pattern
							if wall_color_pattern.has(adj_chunk_coords) and wall_color_pattern[adj_chunk_coords].has(adj_cell):
								continue
							
							# Add adjacent cell
							if not wall_color_pattern.has(adj_chunk_coords):
								wall_color_pattern[adj_chunk_coords] = {}
								wall_color_pattern_cc[adj_chunk_coords] = {}
								wall_color_restore[adj_chunk_coords] = {}
								wall_color_restore_cc[adj_chunk_coords] = {}
							
							var adj_chunk : MarchingSquaresTerrainChunk = terrain.chunks[adj_chunk_coords]
							wall_color_restore[adj_chunk_coords][adj_cell] = adj_chunk.get_wall_color_0(adj_cell)
							wall_color_restore_cc[adj_chunk_coords][adj_cell] = adj_chunk.get_wall_color_1(adj_cell)
							wall_color_pattern[adj_chunk_coords][adj_cell] = vertex_color_0
							wall_color_pattern_cc[adj_chunk_coords][adj_cell] = vertex_color_1
			
			# Build grass mask patterns
			var grass_pattern := {}
			var grass_restore := {}
			for chunk_coords in pattern:
				grass_pattern[chunk_coords] = {}
				grass_restore[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for cell_coords in pattern[chunk_coords]:
					grass_restore[chunk_coords][cell_coords] = chunk.get_grass_mask(cell_coords)
					if current_quick_paint.has_grass:
						grass_pattern[chunk_coords][cell_coords] = Color(1, 1, 0, 0)
					else:
						grass_pattern[chunk_coords][cell_coords] = Color(0, 0, 0, 0)
			
			# Build ground color patterns
			_set_vertex_colors(current_quick_paint.ground_texture_slot)
			
			var color_pattern := {}
			var color_pattern_cc := {}
			var color_restore := {}
			var color_restore_cc := {}
			
			for chunk_coords in pattern:
				color_pattern[chunk_coords] = {}
				color_pattern_cc[chunk_coords] = {}
				color_restore[chunk_coords] = {}
				color_restore_cc[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for cell_coords in pattern[chunk_coords]:
					color_restore[chunk_coords][cell_coords] = chunk.get_color_0(cell_coords)
					color_restore_cc[chunk_coords][cell_coords] = chunk.get_color_1(cell_coords)
					color_pattern[chunk_coords][cell_coords] = vertex_color_0
					color_pattern_cc[chunk_coords][cell_coords] = vertex_color_1
			
			# Create ONE composite action instead of 6 separate actions
			var do_patterns := {
				"height": pattern,
				"wall_color_0": wall_color_pattern,
				"wall_color_1": wall_color_pattern_cc,
				"grass_mask": grass_pattern,
				"color_0": color_pattern,
				"color_1": color_pattern_cc
			}
			var undo_patterns := {
				"height": restore_pattern,
				"wall_color_0": wall_color_restore,
				"wall_color_1": wall_color_restore_cc,
				"grass_mask": grass_restore,
				"color_0": color_restore,
				"color_1": color_restore_cc
			}
			
			undo_redo.create_action("terrain brush with quick paint")
			undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
			undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
			undo_redo.commit_action()
		else:
			# NON-QUICK PAINT MODE: Apply height + default wall texture
			# Use the terrain's default_wall_texture for wall colors
			_set_vertex_colors(terrain.default_wall_texture)
			
			var wall_color_pattern := {}
			var wall_color_pattern_cc := {}
			var wall_color_restore := {}
			var wall_color_restore_cc := {}
			
			# First pass: collect all cells in the pattern
			for chunk_coords in pattern:
				wall_color_pattern[chunk_coords] = {}
				wall_color_pattern_cc[chunk_coords] = {}
				wall_color_restore[chunk_coords] = {}
				wall_color_restore_cc[chunk_coords] = {}
				var chunk : MarchingSquaresTerrainChunk = terrain.chunks[chunk_coords]
				for cell_coords in pattern[chunk_coords]:
					wall_color_restore[chunk_coords][cell_coords] = chunk.get_wall_color_0(cell_coords)
					wall_color_restore_cc[chunk_coords][cell_coords] = chunk.get_wall_color_1(cell_coords)
					wall_color_pattern[chunk_coords][cell_coords] = vertex_color_0
					wall_color_pattern_cc[chunk_coords][cell_coords] = vertex_color_1
			
			# Second pass: expand to adjacent cells (walls appear at boundaries between cells)
			for chunk_coords in pattern:
				for cell_coords in pattern[chunk_coords]:
					for dx in range(-1, 2):
						for dz in range(-1, 2):
							if dx == 0 and dz == 0:
								continue
							
							var adj_x : int = cell_coords.x + dx
							var adj_z : int = cell_coords.y + dz
							var adj_chunk_coords : Vector2i = chunk_coords
							
							if adj_x < 0:
								adj_chunk_coords = Vector2i(chunk_coords.x - 1, chunk_coords.y)
								adj_x = terrain.dimensions.x - 1
							elif adj_x >= terrain.dimensions.x:
								adj_chunk_coords = Vector2i(chunk_coords.x + 1, chunk_coords.y)
								adj_x = 0
							
							if adj_z < 0:
								adj_chunk_coords = Vector2i(adj_chunk_coords.x, chunk_coords.y - 1)
								adj_z = terrain.dimensions.z - 1
							elif adj_z >= terrain.dimensions.z:
								adj_chunk_coords = Vector2i(adj_chunk_coords.x, chunk_coords.y + 1)
								adj_z = 0
							
							if not terrain.chunks.has(adj_chunk_coords):
								continue
							
							var adj_cell := Vector2i(adj_x, adj_z)
							
							if wall_color_pattern.has(adj_chunk_coords) and wall_color_pattern[adj_chunk_coords].has(adj_cell):
								continue
							
							if not wall_color_pattern.has(adj_chunk_coords):
								wall_color_pattern[adj_chunk_coords] = {}
								wall_color_pattern_cc[adj_chunk_coords] = {}
								wall_color_restore[adj_chunk_coords] = {}
								wall_color_restore_cc[adj_chunk_coords] = {}
							
							var adj_chunk : MarchingSquaresTerrainChunk = terrain.chunks[adj_chunk_coords]
							wall_color_restore[adj_chunk_coords][adj_cell] = adj_chunk.get_wall_color_0(adj_cell)
							wall_color_restore_cc[adj_chunk_coords][adj_cell] = adj_chunk.get_wall_color_1(adj_cell)
							wall_color_pattern[adj_chunk_coords][adj_cell] = vertex_color_0
							wall_color_pattern_cc[adj_chunk_coords][adj_cell] = vertex_color_1
			
			# Create composite action with height + wall colors
			var do_patterns := {
				"height": pattern,
				"wall_color_0": wall_color_pattern,
				"wall_color_1": wall_color_pattern_cc
			}
			var undo_patterns := {
				"height": restore_pattern,
				"wall_color_0": wall_color_restore,
				"wall_color_1": wall_color_restore_cc
			}
			
			undo_redo.create_action("terrain height draw")
			undo_redo.add_do_method(self, "apply_composite_pattern_action", terrain, do_patterns)
			undo_redo.add_undo_method(self, "apply_composite_pattern_action", terrain, undo_patterns)
			undo_redo.commit_action()


# For each cell in pattern, raise/lower by y delta

func draw_color_0_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var color : Color = draw_chunk_dict[draw_cell_coords]
			chunk.draw_color_0(draw_cell_coords.x, draw_cell_coords.y, color)
		chunk.regenerate_mesh()


func draw_color_1_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var color : Color = draw_chunk_dict[draw_cell_coords]
			chunk.draw_color_1(draw_cell_coords.x, draw_cell_coords.y, color)
		chunk.regenerate_mesh()



func draw_wall_color_0_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var color : Color = draw_chunk_dict[draw_cell_coords]
			chunk.draw_wall_color_0(draw_cell_coords.x, draw_cell_coords.y, color)
		chunk.regenerate_mesh()


func draw_wall_color_1_pattern_action(terrain: MarchingSquaresTerrain, pattern: Dictionary):
	for draw_chunk_coords: Vector2i in pattern:
		var draw_chunk_dict = pattern[draw_chunk_coords]
		var chunk : MarchingSquaresTerrainChunk = terrain.chunks[draw_chunk_coords]
		for draw_cell_coords: Vector2i in draw_chunk_dict:
			var color : Color = draw_chunk_dict[draw_cell_coords]
			chunk.draw_wall_color_1(draw_cell_coords.x, draw_cell_coords.y, color)
		chunk.regenerate_mesh()


# Applies all terrain patterns  (for quick paint brush and vertex painting operations)

func _set_vertex_colors(vc_idx: int) -> void:
	match vc_idx:
		0: #rr
			vertex_color_0 = Color(1.0, 0.0, 0.0, 0.0)
			vertex_color_1 = Color(1.0, 0.0, 0.0, 0.0)
		1: #rg
			vertex_color_0 = Color(1.0, 0.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 1.0, 0.0, 0.0)
		2: #rb
			vertex_color_0 = Color(1.0, 0.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 1.0, 0.0)
		3: #ra
			vertex_color_0 = Color(1.0, 0.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 0.0, 1.0)
		4: #gr
			vertex_color_0 = Color(0.0, 1.0, 0.0, 0.0)
			vertex_color_1 = Color(1.0, 0.0, 0.0, 0.0)
		5: #gg
			vertex_color_0 = Color(0.0, 1.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 1.0, 0.0, 0.0)
		6: #gb
			vertex_color_0 = Color(0.0, 1.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 1.0, 0.0)
		7: #ga
			vertex_color_0 = Color(0.0, 1.0, 0.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 0.0, 1.0)
		8: #br
			vertex_color_0 = Color(0.0, 0.0, 1.0, 0.0)
			vertex_color_1 = Color(1.0, 0.0, 0.0, 0.0)
		9: #bg
			vertex_color_0 = Color(0.0, 0.0, 1.0, 0.0)
			vertex_color_1 = Color(0.0, 1.0, 0.0, 0.0)
		10: #bb
			vertex_color_0 = Color(0.0, 0.0, 1.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 1.0, 0.0)
		11: #ba
			vertex_color_0 = Color(0.0, 0.0, 1.0, 0.0)
			vertex_color_1 = Color(0.0, 0.0, 0.0, 1.0)
		12: #ar
			vertex_color_0 = Color(0.0, 0.0, 0.0, 1.0)
			vertex_color_1 = Color(1.0, 0.0, 0.0, 0.0)
		13: #ag
			vertex_color_0 = Color(0.0, 0.0, 0.0, 1.0)
			vertex_color_1 = Color(0.0, 1.0, 0.0, 0.0)
		14: #ab
			vertex_color_0 = Color(0.0, 0.0, 0.0, 1.0)
			vertex_color_1 = Color(0.0, 0.0, 1.0, 0.0)
		15: #aa
			vertex_color_0 = Color(0.0, 0.0, 0.0, 1.0)
			vertex_color_1 = Color(0.0, 0.0, 0.0, 1.0)



func get_cell_normal(chunk: MarchingSquaresTerrainChunk, cell: Vector2i) -> Vector3:
	var h_c := chunk.get_height(cell)
	
	var x0 := max(cell.x - 1, 0)
	var x1 := min(cell.x + 1, chunk.dimensions.x - 1)
	var y0 := max(cell.y - 1, 0)
	var y1 := min(cell.y + 1, chunk.dimensions.y - 1)
	
	var h_left := chunk.get_height(Vector2i(x0, cell.y))
	var h_right := chunk.get_height(Vector2i(x1, cell.y))
	var h_below := chunk.get_height(Vector2i(cell.x, y0))
	var h_above := chunk.get_height(Vector2i(cell.x, y1))
	
	var sx := (h_right - h_left) / (2.0 * current_terrain_node.cell_size.x)
	var sz := (h_above - h_below) / (2.0 * current_terrain_node.cell_size.y)
	
	var normal := Vector3(-sx, 1.0, -sz).normalized()
	return normal

#endregion

#endregion
