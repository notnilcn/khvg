class_name PixelArtPipeline3D
extends Node
## Drives the whole 3D pixel-art render pipeline for one camera.
##
## Add this node anywhere in the scene and point [member camera] at your
## Camera3D (defaults to the viewport's current camera). It will:
##   * create a metadata SubViewport + camera that re-renders all pixelized
##     objects (render layer [member metadata_layer]) into a metadata buffer,
##   * attach a Compositor with the outline detection (PRE_OPAQUE) and
##     macro-pixel (POST_SKY) passes to the main camera,
##   * own the shared textures and push them to registered materials.
##
## Requires the Forward+ or Mobile renderer, MSAA off, scaling_3d_scale 1.0.
## Runtime only (not @tool). Only one PixelArtPipeline3D should exist per scene.

## Camera that renders the pixelized scene. Defaults to the current camera.
@export var camera: Camera3D

## Render layer (1-20) that marks pixelized objects. The metadata camera
## renders only this layer; your main camera must include it too.
@export_range(1, 20) var metadata_layer := 20:
	set(value):
		metadata_layer = value
		_metadata_mask = 1 << (value - 1)
		if is_inside_tree():
			_update_material_metadata()

## Export the metadata viewport's depth for correct depth ordering where
## pixelized objects overlap. Disable to fall back to the scene depth
## (cheaper, minor artifacts around overlaps).
@export var export_metadata_depth := true

@export_group("Outlines")
## Suppresses outlines where objects intersect.
@export var depth_test_intersections := true:
	set(value):
		depth_test_intersections = value
		if _outline_pass != null:
			_outline_pass.depth_test_intersections = value
@export var depth_test_threshold := 0.0001:
	set(value):
		depth_test_threshold = value
		if _outline_pass != null:
			_outline_pass.depth_test_threshold = value
## Detect creases from view-space normals in the metadata buffer.
@export var use_normal_edge_detection := true:
	set(value):
		use_normal_edge_detection = value
		if _outline_pass != null:
			_outline_pass.use_normal_edge_detection = value
## Higher values make crease detection less sensitive.
@export var normal_edge_detection_sensitivity := 3.5:
	set(value):
		normal_edge_detection_sensitivity = value
		if _outline_pass != null:
			_outline_pass.normal_edge_detection_sensitivity = value

@export_group("Color Grading")
## Optional palette LUT (baked by PixelPalette) applied to all pixelized
## pixels after lighting. Use TONE_MAPPER_LINEAR for exact colors.
@export var global_palette_lut: Texture2D:
	set(value):
		global_palette_lut = value
		if _macro_pixel_pass != null:
			_macro_pixel_pass.global_palette_lut = value

@export_group("Debug")
## Debug view: 0 = off, 1 = anchor map, 2 = metadata buffer,
## 3 = metadata depth.
@export var debug_view := 0:
	set(value):
		debug_view = value
		if _macro_pixel_pass != null:
			_macro_pixel_pass.debug_view = value

var _metadata_mask := 1 << 19

var _rd: RenderingDevice
var _subviewport: SubViewport
var _metadata_camera: Camera3D
var _compositor: Compositor
var _owned_compositor := false
var _outline_pass: PixelArtOutlinePass
var _macro_pixel_pass: PixelArtMacroPixelPass
var _metadata_compositor: Compositor

var _outlines_texture: RID
var _metadata_color_texture: RID
var _metadata_depth_texture: RID
var _outlines_texture_rd := Texture2DRD.new()
var _materials: Array = []

var _ready_done := false


func _enter_tree() -> void:
	add_to_group("pixel_art_pipeline")


func _ready() -> void:
	_rd = RenderingServer.get_rendering_device()
	if _rd == null:
		push_error("3DPixelArt: RenderingDevice is unavailable. Use the Forward+ or Mobile renderer (Compatibility renderer is not supported).")
		return
	if camera == null:
		camera = get_viewport().get_camera_3d()
	if camera == null:
		# Cameras often become current after siblings' _ready; retry lazily.
		set_process(true)
		return
	_setup()


func _exit_tree() -> void:
	_teardown()


func _process(_delta: float) -> void:
	if not _ready_done:
		if camera == null:
			camera = get_viewport().get_camera_3d()
		if camera == null:
			return
		_setup()
	# Keep the metadata camera glued to the main camera. When a
	# PixelArtCameraSnap is active it re-syncs after snapping, so this
	# per-frame sync only covers the unsnapped case.
	sync_metadata_camera()
	_check_depth_export()


# If the metadata viewport's compositor never runs (e.g. unsupported in
# SubViewports on this version), the exported textures would stay all-zero
# and silently disable pixelization; fall back to the scene depth instead.
var _frames_waited := 0

func _check_depth_export() -> void:
	if not export_metadata_depth or _frames_waited > 30:
		return
	_frames_waited += 1
	if _frames_waited == 30:
		if PixelArtSharedBuffers.metadata_depth_frames == 0:
			push_warning("3DPixelArt: metadata export did not run (SubViewport compositors may be unsupported); pixelization is disabled.")


func _setup() -> void:
	if _ready_done:
		return
	_ready_done = true

	var viewport := get_viewport()
	if viewport.msaa_3d != Viewport.MSAA_DISABLED:
		push_warning("3DPixelArt: MSAA 3D should be disabled (project setting rendering/anti_aliasing/quality/msaa_3d).")
	if viewport.scaling_3d_scale != 1.0:
		push_warning("3DPixelArt: scaling_3d_scale should be 1.0.")
	if not camera.get_cull_mask_value(metadata_layer):
		push_warning("3DPixelArt: the main camera's cull mask does not include layer %d; pixelized objects will be invisible." % metadata_layer)
	if camera.cull_mask == _metadata_mask:
		push_warning("3DPixelArt: the main camera cull mask equals the metadata mask; the shader cannot tell the cameras apart.")

	_create_metadata_viewport()
	_create_textures(Vector2i(viewport.get_visible_rect().size))
	_setup_compositors()
	_update_material_metadata()

	if not viewport.size_changed.is_connected(_on_viewport_size_changed):
		viewport.size_changed.connect(_on_viewport_size_changed)


func _teardown() -> void:
	# Stop the effects from binding anything first; freeing the viewport and
	# textures afterwards cannot race with in-flight dispatches then.
	PixelArtSharedBuffers.reset()

	var viewport := get_viewport()
	if viewport != null and viewport.size_changed.is_connected(_on_viewport_size_changed):
		viewport.size_changed.disconnect(_on_viewport_size_changed)

	if _metadata_camera != null:
		_metadata_camera.compositor = null
		_metadata_camera = null
	if _subviewport != null:
		_subviewport.queue_free()
		_subviewport = null
	if _compositor != null:
		var fx := _compositor.compositor_effects.duplicate()
		fx.erase(_outline_pass)
		fx.erase(_macro_pixel_pass)
		_compositor.compositor_effects = fx
		_compositor = null
	if _owned_compositor and camera != null:
		camera.compositor = null
	_owned_compositor = false
	_outline_pass = null
	_macro_pixel_pass = null
	_metadata_compositor = null

	if _rd != null:
		RenderingServer.call_on_render_thread(_free_textures_rt)
	_materials.clear()
	_ready_done = false


func _free_textures_rt() -> void:
	if _outlines_texture.is_valid():
		_rd.free_rid(_outlines_texture)
	if _metadata_color_texture.is_valid():
		_rd.free_rid(_metadata_color_texture)
	if _metadata_depth_texture.is_valid():
		_rd.free_rid(_metadata_depth_texture)
	_outlines_texture = RID()
	_metadata_color_texture = RID()
	_metadata_depth_texture = RID()


# -----------------------------------------------------------------------------
# Metadata viewport
# -----------------------------------------------------------------------------

func _create_metadata_viewport() -> void:
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	# White background marks "not pixelized" (pixel size decodes to 0).
	env.background_color = Color(1.0, 1.0, 1.0, 1.0)
	# Linear tonemapping so packed metadata values pass through unchanged.
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	env.glow_enabled = false
	env.fog_enabled = false
	env.volumetric_fog_enabled = false
	env.ssao_enabled = false
	env.ssil_enabled = false
	env.sdfgi_enabled = false
	env.ambient_light_source = Environment.AMBIENT_SOURCE_DISABLED

	_metadata_camera = Camera3D.new()
	_metadata_camera.name = "PixelArtMetadataCamera"
	_metadata_camera.cull_mask = _metadata_mask
	_metadata_camera.environment = env
	_metadata_camera.current = true
	# The export pass ships the packed metadata color + depth to the shared
	# textures every frame.
	_metadata_compositor = Compositor.new()
	_metadata_compositor.compositor_effects = [PixelArtMetadataExportPass.new()]
	_metadata_camera.compositor = _metadata_compositor

	_subviewport = SubViewport.new()
	_subviewport.name = "PixelArtMetadataViewport"
	_subviewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_subviewport.msaa_3d = Viewport.MSAA_DISABLED
	_subviewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_DISABLED
	_subviewport.use_taa = false
	_subviewport.positional_shadow_atlas_size = 0
	_subviewport.size = Vector2i(get_viewport().get_visible_rect().size)
	_subviewport.add_child(_metadata_camera)
	add_child(_subviewport)


func sync_metadata_camera() -> void:
	if _metadata_camera == null or camera == null:
		return
	_metadata_camera.global_transform = camera.global_transform
	_metadata_camera.projection = camera.projection
	_metadata_camera.fov = camera.fov
	_metadata_camera.size = camera.size
	_metadata_camera.near = camera.near
	_metadata_camera.far = camera.far
	_metadata_camera.frustum_offset = camera.frustum_offset
	_metadata_camera.h_offset = camera.h_offset
	_metadata_camera.v_offset = camera.v_offset


# -----------------------------------------------------------------------------
# Shared textures
# -----------------------------------------------------------------------------

# RenderingDevice is only safe to touch from the rendering thread, so all
# texture creation happens via RenderingServer.call_on_render_thread.
func _create_textures(size: Vector2i) -> void:
	size.x = maxi(size.x, 2)
	size.y = maxi(size.y, 2)
	RenderingServer.call_on_render_thread(_create_textures_rt.bind(size))


func _create_textures_rt(size: Vector2i) -> void:
	if _outlines_texture.is_valid():
		_rd.free_rid(_outlines_texture)
	if _metadata_color_texture.is_valid():
		_rd.free_rid(_metadata_color_texture)
	if _metadata_depth_texture.is_valid():
		_rd.free_rid(_metadata_depth_texture)

	var format := RDTextureFormat.new()
	format.texture_type = RenderingDevice.TEXTURE_TYPE_2D
	format.width = size.x
	format.height = size.y
	format.depth = 1
	format.array_layers = 1
	format.mipmaps = 1
	format.samples = RenderingDevice.TEXTURE_SAMPLES_1
	format.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT \
		| RenderingDevice.TEXTURE_USAGE_STORAGE_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT

	format.format = RenderingDevice.DATA_FORMAT_R8G8B8A8_UNORM
	_outlines_texture = _rd.texture_create(format, RDTextureView.new(), [])
	# 16-bit: the G channel packs id (8 bits) + pixel size (3 bits), which
	# does not survive 8-bit quantization.
	format.format = RenderingDevice.DATA_FORMAT_R16G16B16A16_UNORM
	_metadata_color_texture = _rd.texture_create(format, RDTextureView.new(), [])

	format.format = RenderingDevice.DATA_FORMAT_R32_SFLOAT
	_metadata_depth_texture = _rd.texture_create(format, RDTextureView.new(), [])

	_outlines_texture_rd.texture_rd_rid = _outlines_texture
	PixelArtSharedBuffers.outlines = _outlines_texture
	PixelArtSharedBuffers.outlines_size = size
	PixelArtSharedBuffers.metadata_color = _metadata_color_texture
	PixelArtSharedBuffers.metadata_depth = _metadata_depth_texture
	PixelArtSharedBuffers.metadata_depth_size = size
	PixelArtSharedBuffers.use_exported_depth = export_metadata_depth


## Debug helper: dumps the shared metadata color/depth textures as PNGs.
func save_shared_textures_debug(color_path: String, depth_path: String) -> void:
	RenderingServer.call_on_render_thread(_save_shared_textures_debug_rt.bind(color_path, depth_path))


func _save_shared_textures_debug_rt(color_path: String, depth_path: String) -> void:
	var size := PixelArtSharedBuffers.metadata_depth_size
	if size.x <= 0 or not _metadata_color_texture.is_valid():
		print("save_shared_textures_debug: no shared textures yet")
		return
	var data := _rd.texture_get_data(_metadata_color_texture, 0)
	# RGBA16 unorm; force alpha to 1 (the engine clears with alpha=0).
	var img := Image.create(size.x, size.y, false, Image.FORMAT_RGBA8)
	var words := data.size() / 2
	for y in size.y:
		for x in size.x:
			var i := (x + y * size.x) * 4
			if i + 3 >= words:
				break
			img.set_pixel(x, y, Color(
					data.decode_u16(i * 2) / 65535.0,
					data.decode_u16((i + 1) * 2) / 65535.0,
					data.decode_u16((i + 2) * 2) / 65535.0,
					1.0))
	print("shared metadata color saved: ", img.save_png(color_path))

	var ddata := _rd.texture_get_data(_metadata_depth_texture, 0)
	var dimg := Image.create(size.x, size.y, false, Image.FORMAT_L8)
	var floats := ddata.size() / 4
	for y in size.y:
		for x in size.x:
			var i := x + y * size.x
			if i >= floats:
				break
			var d := ddata.decode_float(i * 4)
			dimg.set_pixel(x, y, Color(clampf(d, 0.0, 1.0), 0, 0))
	print("shared metadata depth saved: ", dimg.save_png(depth_path))


func _on_viewport_size_changed() -> void:
	var size := Vector2i(get_viewport().get_visible_rect().size)
	if _subviewport != null:
		_subviewport.size = size
	_create_textures(size)


# -----------------------------------------------------------------------------
# Compositors
# -----------------------------------------------------------------------------

func _setup_compositors() -> void:
	if camera.compositor == null:
		_compositor = Compositor.new()
		camera.compositor = _compositor
		_owned_compositor = true
	else:
		_compositor = camera.compositor

	_outline_pass = PixelArtOutlinePass.new()
	_outline_pass.depth_test_intersections = depth_test_intersections
	_outline_pass.depth_test_threshold = depth_test_threshold
	_outline_pass.use_normal_edge_detection = use_normal_edge_detection
	_outline_pass.normal_edge_detection_sensitivity = normal_edge_detection_sensitivity

	_macro_pixel_pass = PixelArtMacroPixelPass.new()
	_macro_pixel_pass.global_palette_lut = global_palette_lut
	_macro_pixel_pass.debug_view = debug_view

	var fx := _compositor.compositor_effects.duplicate()
	fx.append(_outline_pass)
	fx.append(_macro_pixel_pass)
	_compositor.compositor_effects = fx


# -----------------------------------------------------------------------------
# Material registry
# -----------------------------------------------------------------------------

## Called by PixelArtObject3D so this manager can feed the outline texture
## and the metadata cull mask into every pixelized material.
func register_material(material: Material) -> void:
	if material == null or _materials.has(material):
		return
	_materials.append(material)
	material.set_shader_parameter("pixel_art_outlines", _outlines_texture_rd)
	material.set_shader_parameter("metadata_cull_mask", _metadata_mask)


func unregister_material(material: Material) -> void:
	_materials.erase(material)


func _update_material_metadata() -> void:
	for i in range(_materials.size() - 1, -1, -1):
		var material: Material = _materials[i]
		if material == null:
			_materials.remove_at(i)
			continue
		material.set_shader_parameter("pixel_art_outlines", _outlines_texture_rd)
		material.set_shader_parameter("metadata_cull_mask", _metadata_mask)
