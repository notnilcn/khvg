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
## Designed for ORTHOGRAPHIC cameras; perspective is left as-is and
## unsupported (snapping and the anchor lattice assume an ortho projection).
## Requires the Forward+ or Mobile renderer, MSAA off, scaling_3d_scale 1.0.
## Runtime only (not @tool). Only one PixelArtPipeline3D should exist per scene.

## Master toggle. When false, outlines/rays/pixelization are bypassed
## (the outline buffer is cleared to black, other passes early-out).
@export var enabled := true:
	set(value):
		enabled = value
		_propagate_enabled()

## Camera that renders the pixelized scene. Defaults to the current camera.
@export var camera: Camera3D

## When true, this pipeline's [member camera] is forced current every frame.
@export var override_view := false

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

@export_group("Resolution")
## Internal pixel-art resolution target, e.g. 960x540 (ortho width =
## ResX * (1/PPU); see [method get_ortho_width]). Informational:
## there is no true downsample step, the anchor lattice works at full res.
@export var internal_resolution := Vector2i(960, 540)
## Screen pixels per world unit at [member zoom_scale] 1.
@export var pixels_per_unit := 0.5
## Zoom multiplier. Scales [member pixels_per_unit].
@export var zoom_scale := 1.0:
	set(value):
		zoom_scale = value
		_push_stability_params()
## When true, force the viewport size to [member internal_resolution] on setup.
## Off by default (never resize silently).
@export var fix_internal := false

@export_group("Camera")
## Optional camera snap node. When assigned, [method get_units_per_pixel]
## reads the measured texel size from it and the per-frame sub-texel
## remainder for [member smooth_scroll] comes from it.
@export var camera_snap: PixelArtCameraSnap

@export_group("Pixel Stability")
## Sub-texel camera remainder is applied as an integer anchor shift
## (Shift = round(remainder * scale)). Off by default; on, slow pans look
## smoother but pixels visibly crawl.
@export var smooth_scroll := false:
	set(value):
		smooth_scroll = value
		_push_stability_params()
## Sharp-bilinear vs nearest upscale. Off = exact
## texel fetch; on = linear-filtered anchor sample. Subtle by design: with
## no true low-res downsample step both sample texel centers.
@export var upscale_aa := false:
	set(value):
		upscale_aa = value
		_push_stability_params()
## Run [method apply_recommended_settings] automatically on setup.
@export var apply_recommended := true

@export_group("Outlines")
## Master outline toggle (writes black when off).
@export var enable_outline := true:
	set(value):
		enable_outline = value
		if _outline_pass != null:
			_outline_pass.pass_enabled = value
## Per-ID exterior/interior outline colors (256x2 HDR palette).
## When null, per-object outline_color is used (legacy behavior).
@export var outline_palette: PixelOutlinePalette:
	set(value):
		outline_palette = value
		if is_inside_tree():
			_rebuild_outline_palette()
			_update_material_metadata()
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
## Second-derivative (FarCurve) depth outlines on top of ID edges. Off by
## default; enable for silhouette outlines where same-ID surfaces fold away.
@export var outline_depth_curve := false:
	set(value):
		outline_depth_curve = value
		if _outline_pass != null:
			_outline_pass.depth_curve_enabled = value
@export var outline_depth_curve_threshold := 0.001:
	set(value):
		outline_depth_curve_threshold = value
		if _outline_pass != null:
			_outline_pass.depth_curve_threshold = value

@export_group("Subject Pinning")
## Auto-pin every PixelArtObject3D under [member auto_pin_target]
## Pinned subjects (stencil 255) ignore the
## smooth-scroll shift while the background moves.
@export var auto_pin := false:
	set(value):
		auto_pin = value
		if is_inside_tree():
			_update_auto_pin()
@export var auto_pin_target: Node3D:
	set(value):
		auto_pin_target = value
		if is_inside_tree():
			_update_auto_pin()
## Reserved outline ID marking pinned subjects (stencil 255).
## Do not use 255 as a regular outline_id.
@export_range(0, 255) var pin_stencil_id := 255:
	set(value):
		pin_stencil_id = value
		_push_stability_params()
## Pin-mask dilation radius in pixels.
@export_range(0.0, 4.0, 0.5) var pin_dilate := 2.0:
	set(value):
		pin_dilate = value
		_push_stability_params()
## Tint pinned pixels red (debug).
@export var debug_pin_mask := false:
	set(value):
		debug_pin_mask = value
		_push_stability_params()

@export_group("Color Grading")
## Optional palette LUT (baked by PixelPalette) applied to all pixelized
## pixels after lighting. Use TONE_MAPPER_LINEAR for exact colors.
@export var global_palette_lut: Texture2D:
	set(value):
		global_palette_lut = value
		if _macro_pixel_pass != null:
			_macro_pixel_pass.global_palette_lut = value

@export_group("Clouds")
## Banded cloud shadows on pixelized materials (pushed as shader uniforms).
## Only materials using the pixel-art shader receive them.
@export var clouds_enabled := false:
	set(value):
		clouds_enabled = value
		_push_cloud_params()
## Coverage noise, sampled with filter_nearest on a scrolling grid.
@export var cloud_noise: Texture2D:
	set(value):
		cloud_noise = value
		_push_cloud_params()
## Sun that casts the cloud shadows (and the god rays). Its forward
## direction is pushed to materials every frame.
@export var cloud_sun: DirectionalLight3D
## Height of the imaginary cloud plane the shadows/rays are projected from.
@export var cloud_height := 10.0:
	set(value):
		cloud_height = value
		_push_cloud_params()
@export var cloud_noise_scale := 0.05:
	set(value):
		cloud_noise_scale = value
		_push_cloud_params()
## Noise value above which a cloud blocks the sun.
@export_range(0.0, 1.0) var cloud_threshold := 0.5:
	set(value):
		cloud_threshold = value
		_push_cloud_params()
## Stepped banding of the noise before thresholding.
@export var cloud_bands := 3.0:
	set(value):
		cloud_bands = value
		_push_cloud_params()
## Coverage curve tightness: exponent on the shaped
## coverage. 1.0 = unchanged, higher = narrower cloud cores.
@export_range(0.1, 4.0, 0.05) var cloud_tightness := 1.0:
	set(value):
		cloud_tightness = value
		_push_cloud_params()
## Amplitude falloff per FBM octave for the procedural detail.
@export_range(0.1, 1.0, 0.05) var cloud_octave_drop := 0.5:
	set(value):
		cloud_octave_drop = value
		_push_cloud_params()
## Erodes the ray-march cloud gaps. 0 = unchanged.
@export_range(0.0, 0.45, 0.01) var cloud_gap_erosion := 0.0:
	set(value):
		cloud_gap_erosion = value
		_push_cloud_params()
## Mix procedural FBM detail into the texture noise (0 = texture only).
@export_range(0.0, 1.0, 0.05) var cloud_detail_strength := 0.0:
	set(value):
		cloud_detail_strength = value
		_push_cloud_params()
## Wind direction * speed; scrolls the noise over time.
@export var cloud_wind := Vector2(0.02, 0.0):
	set(value):
		cloud_wind = value
		_push_cloud_params()
## How dark the toon ramp gets under a cloud (ambient is untouched).
@export_range(0.0, 1.0) var cloud_shadow_strength := 0.6:
	set(value):
		cloud_shadow_strength = value
		_push_cloud_params()
## Multi-level banded shadows (hard contour rings) vs smooth soft shadows.
@export var cloud_shadow_banding_enabled := true:
	set(value):
		cloud_shadow_banding_enabled = value
		_push_cloud_params()
## Darkness levels when banding; 1 = binary hard shadow.
@export_range(1.0, 8.0, 1.0) var cloud_shadow_levels := 3.0:
	set(value):
		cloud_shadow_levels = value
		_push_cloud_params()
## Width of the coverage gradient below the threshold: the shadow ramps from
## faint to full across [cloud_threshold - softness, cloud_threshold]. 0 = hard
## edge, 1 = the penumbra spans the whole noise range below the threshold.
@export_range(0.0, 1.0) var cloud_shadow_softness := 0.4:
	set(value):
		cloud_shadow_softness = value
		_push_cloud_params()
## God rays through the cloud gaps, added to the scene color *before*
## pixelization so the rays get macro-pixels too. Tuning lives on the
## PixelArtGodRayPass (see [method get_god_ray_pass]).
@export var god_rays_enabled := false:
	set(value):
		god_rays_enabled = value
		if _god_ray_pass != null:
			_god_ray_pass.rays_enabled = value

@export_group("Debug")
## Debug view: 0 = off, 1 = anchor map, 2 = metadata buffer,
## 3 = metadata depth.
@export var debug_view := 0:
	set(value):
		debug_view = value
		if _macro_pixel_pass != null:
			_macro_pixel_pass.debug_view = value
## Show the measured pipeline values on a runtime HUD (needs
## demo_debug_hud.gd in the scene, or prints to stdout when absent).
@export var debug_overlay := false

var _metadata_mask := 1 << 19

var _rd: RenderingDevice
var _subviewport: SubViewport
var _metadata_camera: Camera3D
var _compositor: Compositor
var _owned_compositor := false
var _outline_pass: PixelArtOutlinePass
var _god_ray_pass: PixelArtGodRayPass
var _macro_pixel_pass: PixelArtMacroPixelPass
var _metadata_compositor: Compositor

var _outlines_texture: RID
var _metadata_color_texture: RID
var _metadata_depth_texture: RID
var _outline_palette_texture: RID
var _outlines_texture_rd := Texture2DRD.new()
var _outline_palette_rd := Texture2DRD.new()
var _materials: Array = []
# Non-pixelized materials that still receive the cloud-shadow uniforms
# (terrain, water): registered via register_cloud_material.
var _cloud_materials: Array = []

var _ready_done := false
var _pin_manager: PixelArtPinning


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
	if override_view and camera != null and not camera.current:
		camera.make_current()
	# Keep the metadata camera glued to the main camera. When a
	# PixelArtCameraSnap is active it re-syncs after snapping, so this
	# per-frame sync only covers the unsnapped case.
	sync_metadata_camera()
	_check_depth_export()
	_update_smooth_shift()
	if clouds_enabled and cloud_sun != null:
		_push_cloud_sun_dir(-cloud_sun.global_transform.basis.z)
	if debug_overlay:
		_print_debug_overlay()


# -----------------------------------------------------------------------------
# Public helpers
# -----------------------------------------------------------------------------

## World units per screen pixel at the current zoom. Reads the measured
## texel size from [member camera_snap] when assigned, otherwise derives it
## from [member pixels_per_unit] x [member zoom_scale].
func get_units_per_pixel() -> float:
	if camera_snap != null and is_instance_valid(camera_snap):
		return camera_snap.get_units_per_texel()
	return 1.0 / maxf(pixels_per_unit, 0.00001) / maxf(zoom_scale, 0.00001)


## Effective ortho width of the internal pixel-art resolution
## (ortho width = ResX * (1/PPU) at the current zoom).
func get_ortho_width() -> float:
	return float(internal_resolution.x) * get_units_per_pixel()


## Sub-texel camera remainder in texel units [0, 1), from [member camera_snap]
## (zero when no snap node is assigned).
func get_remainder_texels() -> Vector2:
	if camera_snap != null and is_instance_valid(camera_snap):
		return camera_snap.remainder_texels
	return Vector2.ZERO


## Enforces the renderer
## state this pipeline needs (MSAA off, TAA off, scaling 1.0 BILINEAR).
## Prints warnings for anything it cannot fix from here.
func apply_recommended_settings() -> void:
	var viewport := get_viewport()
	if viewport == null:
		return
	if viewport.msaa_3d != Viewport.MSAA_DISABLED:
		viewport.msaa_3d = Viewport.MSAA_DISABLED
		print("3DPixelArt: msaa_3d forced off (recommended).")
	if viewport.use_taa:
		viewport.use_taa = false
		print("3DPixelArt: TAA forced off (recommended).")
	if viewport.scaling_3d_scale != 1.0:
		viewport.scaling_3d_scale = 1.0
		print("3DPixelArt: scaling_3d_scale forced to 1.0 (recommended).")
	if viewport.scaling_3d_mode != Viewport.SCALING_3D_MODE_BILINEAR:
		viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
		print("3DPixelArt: scaling_3d_mode forced to BILINEAR (recommended).")
	if camera != null and camera.projection != Camera3D.PROJECTION_ORTHOGONAL:
		push_warning("3DPixelArt: pipeline is designed for orthographic cameras; perspective is unsupported.")


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
	if apply_recommended:
		apply_recommended_settings()
	else:
		if viewport.msaa_3d != Viewport.MSAA_DISABLED:
			push_warning("3DPixelArt: MSAA 3D should be disabled (project setting rendering/anti_aliasing/quality/msaa_3d).")
		if viewport.scaling_3d_scale != 1.0:
			push_warning("3DPixelArt: scaling_3d_scale should be 1.0.")
		if viewport.use_taa:
			push_warning("3DPixelArt: TAA should be disabled.")
	if not camera.get_cull_mask_value(metadata_layer):
		push_warning("3DPixelArt: the main camera's cull mask does not include layer %d; pixelized objects will be invisible." % metadata_layer)
	if camera.cull_mask == _metadata_mask:
		push_warning("3DPixelArt: the main camera cull mask equals the metadata mask; the shader cannot tell the cameras apart.")
	if camera.projection != Camera3D.PROJECTION_ORTHOGONAL:
		push_warning("3DPixelArt: designed for orthographic cameras; perspective left as-is, unsupported.")
	if fix_internal:
		var vp_size := Vector2i(viewport.get_visible_rect().size)
		if vp_size != internal_resolution:
			viewport.size = internal_resolution
			print("3DPixelArt: fix_internal resized viewport %s -> %s." % [vp_size, internal_resolution])

	_create_metadata_viewport()
	_create_textures(Vector2i(viewport.get_visible_rect().size))
	_setup_compositors()
	_update_material_metadata()
	_update_auto_pin()
	_push_stability_params()

	if not viewport.size_changed.is_connected(_on_viewport_size_changed):
		viewport.size_changed.connect(_on_viewport_size_changed)


func _teardown() -> void:
	# Stop the effects from binding anything first; freeing the viewport and
	# textures afterwards cannot race with in-flight dispatches then.
	PixelArtSharedBuffers.reset()

	var viewport := get_viewport()
	if viewport != null and viewport.size_changed.is_connected(_on_viewport_size_changed):
		viewport.size_changed.disconnect(_on_viewport_size_changed)

	if _pin_manager != null:
		_pin_manager.unpin_all()
		_pin_manager.queue_free()
		_pin_manager = null
	if _metadata_camera != null:
		_metadata_camera.compositor = null
		_metadata_camera = null
	if _subviewport != null:
		_subviewport.queue_free()
		_subviewport = null
	if _compositor != null:
		var fx := _compositor.compositor_effects.duplicate()
		fx.erase(_outline_pass)
		fx.erase(_god_ray_pass)
		fx.erase(_macro_pixel_pass)
		_compositor.compositor_effects = fx
		_compositor = null
	if _owned_compositor and camera != null:
		camera.compositor = null
	_owned_compositor = false
	_outline_pass = null
	_god_ray_pass = null
	_macro_pixel_pass = null
	_metadata_compositor = null

	if _rd != null:
		RenderingServer.call_on_render_thread(_free_textures_rt)
	_materials.clear()
	_cloud_materials.clear()
	_ready_done = false


func _free_textures_rt() -> void:
	if _outlines_texture.is_valid():
		_rd.free_rid(_outlines_texture)
	if _metadata_color_texture.is_valid():
		_rd.free_rid(_metadata_color_texture)
	if _metadata_depth_texture.is_valid():
		_rd.free_rid(_metadata_depth_texture)
	if _outline_palette_texture.is_valid():
		_rd.free_rid(_outline_palette_texture)
	_outlines_texture = RID()
	_metadata_color_texture = RID()
	_metadata_depth_texture = RID()
	_outline_palette_texture = RID()


func _propagate_enabled() -> void:
	if _outline_pass != null:
		_outline_pass.pass_enabled = enabled
	if _macro_pixel_pass != null:
		_macro_pixel_pass.pass_enabled = enabled
	if _god_ray_pass != null:
		_god_ray_pass.rays_enabled = god_rays_enabled and enabled


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
	var palette_bytes := PackedByteArray()
	if outline_palette != null:
		palette_bytes = outline_palette.bake_bytes()
	RenderingServer.call_on_render_thread(_create_textures_rt.bind(size, palette_bytes))


func _create_textures_rt(size: Vector2i, palette_bytes: PackedByteArray) -> void:
	# Invalidate the shared RIDs BEFORE freeing the old textures: the compositor
	# passes early-return on invalid shared textures, so this closes the window
	# where a pass builds a uniform set with a just-freed RID ("Texture (binding:
	# N) is not a valid texture" errors on viewport resize).
	PixelArtSharedBuffers.outlines = RID()
	PixelArtSharedBuffers.metadata_color = RID()
	PixelArtSharedBuffers.metadata_depth = RID()
	PixelArtSharedBuffers.outline_palette = RID()
	if _outlines_texture.is_valid():
		_rd.free_rid(_outlines_texture)
	if _metadata_color_texture.is_valid():
		_rd.free_rid(_metadata_color_texture)
	if _metadata_depth_texture.is_valid():
		_rd.free_rid(_metadata_depth_texture)
	if _outline_palette_texture.is_valid():
		_rd.free_rid(_outline_palette_texture)

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

	# 256x2 HDR outline palette (16-bit float RGBA): row 0 =
	# exterior colors, row 1 = interior colors, alpha = multiply flag.
	var palette_format := RDTextureFormat.new()
	palette_format.texture_type = RenderingDevice.TEXTURE_TYPE_2D
	palette_format.width = 256
	palette_format.height = 2
	palette_format.depth = 1
	palette_format.array_layers = 1
	palette_format.mipmaps = 1
	palette_format.samples = RenderingDevice.TEXTURE_SAMPLES_1
	palette_format.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT
	palette_format.format = RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT
	var palette_data := palette_bytes
	if palette_data.size() != 256 * 2 * 8:
		palette_data = PixelOutlinePalette.default_bytes()
	_outline_palette_texture = _rd.texture_create(palette_format, RDTextureView.new(), [palette_data])

	_outlines_texture_rd.texture_rd_rid = _outlines_texture
	_outline_palette_rd.texture_rd_rid = _outline_palette_texture
	PixelArtSharedBuffers.outlines = _outlines_texture
	PixelArtSharedBuffers.outlines_size = size
	PixelArtSharedBuffers.metadata_color = _metadata_color_texture
	PixelArtSharedBuffers.metadata_depth = _metadata_depth_texture
	PixelArtSharedBuffers.metadata_depth_size = size
	PixelArtSharedBuffers.use_exported_depth = export_metadata_depth
	PixelArtSharedBuffers.outline_palette = _outline_palette_texture


## Rebuilds the shared 256x2 outline-palette texture from [member outline_palette].
func _rebuild_outline_palette() -> void:
	if _rd == null or not _ready_done:
		return
	var palette_bytes := PackedByteArray()
	if outline_palette != null:
		palette_bytes = outline_palette.bake_bytes()
	RenderingServer.call_on_render_thread(_rebuild_outline_palette_rt.bind(palette_bytes))


func _rebuild_outline_palette_rt(palette_bytes: PackedByteArray) -> void:
	PixelArtSharedBuffers.outline_palette = RID()
	if _outline_palette_texture.is_valid():
		_rd.free_rid(_outline_palette_texture)
	var palette_format := RDTextureFormat.new()
	palette_format.texture_type = RenderingDevice.TEXTURE_TYPE_2D
	palette_format.width = 256
	palette_format.height = 2
	palette_format.depth = 1
	palette_format.array_layers = 1
	palette_format.mipmaps = 1
	palette_format.samples = RenderingDevice.TEXTURE_SAMPLES_1
	palette_format.usage_bits = RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT \
		| RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT
	palette_format.format = RenderingDevice.DATA_FORMAT_R16G16B16A16_SFLOAT
	var palette_data := palette_bytes
	if palette_data.size() != 256 * 2 * 8:
		palette_data = PixelOutlinePalette.default_bytes()
	_outline_palette_texture = _rd.texture_create(palette_format, RDTextureView.new(), [palette_data])
	_outline_palette_rd.texture_rd_rid = _outline_palette_texture
	PixelArtSharedBuffers.outline_palette = _outline_palette_texture


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
	_outline_pass.pass_enabled = enabled
	_outline_pass.depth_test_intersections = depth_test_intersections
	_outline_pass.depth_test_threshold = depth_test_threshold
	_outline_pass.use_normal_edge_detection = use_normal_edge_detection
	_outline_pass.normal_edge_detection_sensitivity = normal_edge_detection_sensitivity
	_outline_pass.depth_curve_enabled = outline_depth_curve
	_outline_pass.depth_curve_threshold = outline_depth_curve_threshold

	_god_ray_pass = PixelArtGodRayPass.new()
	_god_ray_pass.rays_enabled = god_rays_enabled and enabled

	_macro_pixel_pass = PixelArtMacroPixelPass.new()
	_macro_pixel_pass.pass_enabled = enabled
	_macro_pixel_pass.global_palette_lut = global_palette_lut
	_macro_pixel_pass.debug_view = debug_view

	# The god-ray pass must precede the macro-pixel pass in the array: both
	# are POST_SKY effects and run in array order, and the rays must be in
	# the color layer before copy_color so they get pixelated too.
	var fx := _compositor.compositor_effects.duplicate()
	fx.append(_outline_pass)
	fx.append(_god_ray_pass)
	fx.append(_macro_pixel_pass)
	_compositor.compositor_effects = fx
	_push_cloud_params()


## The god-ray compositor pass (for ray tuning: steps, intensity, decay...).
func get_god_ray_pass() -> PixelArtGodRayPass:
	return _god_ray_pass


# -----------------------------------------------------------------------------
# Smooth scroll + upscale (per-frame stability uniforms)
# -----------------------------------------------------------------------------

func _update_smooth_shift() -> void:
	if _macro_pixel_pass == null:
		return
	var shift := Vector2i.ZERO
	if smooth_scroll and camera_snap != null and is_instance_valid(camera_snap):
		# Smooth scroll: Shift = round(remainder * scale); the
		# remainder is already in texel units, so scale is 1.
		var remainder := camera_snap.remainder_texels
		shift = Vector2i(int(roundf(remainder.x)), int(roundf(remainder.y)))
	_macro_pixel_pass.pixel_shift = shift
	_macro_pixel_pass.smooth_scroll_enabled = smooth_scroll


func _push_stability_params() -> void:
	if _macro_pixel_pass == null:
		return
	_macro_pixel_pass.upscale_aa = upscale_aa
	_macro_pixel_pass.smooth_scroll_enabled = smooth_scroll
	_macro_pixel_pass.pin_dilate = pin_dilate
	_macro_pixel_pass.pin_stencil_id = pin_stencil_id
	_macro_pixel_pass.debug_pin_mask = debug_pin_mask


# -----------------------------------------------------------------------------
# Subject pinning
# -----------------------------------------------------------------------------

func _update_auto_pin() -> void:
	if not _ready_done:
		return
	if _pin_manager == null:
		_pin_manager = PixelArtPinning.new()
		_pin_manager.pin_outline_id = pin_stencil_id
		add_child(_pin_manager)
	_pin_manager.pin_outline_id = pin_stencil_id
	if auto_pin and auto_pin_target != null:
		_pin_manager.pin_subtree(auto_pin_target)
	else:
		_pin_manager.unpin_all()


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
	material.set_shader_parameter("pixel_art_outline_palette", _outline_palette_rd)
	material.set_shader_parameter("use_outline_palette", outline_palette != null)
	material.set_shader_parameter("metadata_cull_mask", _metadata_mask)
	_push_cloud_params_to(material)


func unregister_material(material: Material) -> void:
	_materials.erase(material)


## Registers a non-pixelized material (e.g. terrain, water) to receive ONLY
## the cloud-shadow uniforms — no outline texture or metadata mask. The
## material's shader must implement the cloud coverage itself (same uniform
## names as pixel_art_object.gdshader).
func register_cloud_material(material: Material) -> void:
	if material == null or _cloud_materials.has(material):
		return
	_cloud_materials.append(material)
	_push_cloud_params_to(material)
	if clouds_enabled and cloud_sun != null:
		material.set_shader_parameter("cloud_sun_dir", -cloud_sun.global_transform.basis.z)


func unregister_cloud_material(material: Material) -> void:
	_cloud_materials.erase(material)


func _update_material_metadata() -> void:
	for i in range(_materials.size() - 1, -1, -1):
		var material: Material = _materials[i]
		if material == null:
			_materials.remove_at(i)
			continue
		material.set_shader_parameter("pixel_art_outlines", _outlines_texture_rd)
		material.set_shader_parameter("pixel_art_outline_palette", _outline_palette_rd)
		material.set_shader_parameter("use_outline_palette", outline_palette != null)
		material.set_shader_parameter("metadata_cull_mask", _metadata_mask)


# -----------------------------------------------------------------------------
# Clouds
# -----------------------------------------------------------------------------

func _push_cloud_params() -> void:
	for i in range(_materials.size() - 1, -1, -1):
		var material: Material = _materials[i]
		if material == null:
			_materials.remove_at(i)
			continue
		_push_cloud_params_to(material)
	for i in range(_cloud_materials.size() - 1, -1, -1):
		var cloud_material: Material = _cloud_materials[i]
		if cloud_material == null:
			_cloud_materials.remove_at(i)
			continue
		_push_cloud_params_to(cloud_material)
	if _god_ray_pass != null:
		_god_ray_pass.cloud_noise = cloud_noise
		_god_ray_pass.cloud_height = cloud_height
		_god_ray_pass.cloud_noise_scale = cloud_noise_scale
		_god_ray_pass.cloud_threshold = cloud_threshold
		_god_ray_pass.cloud_bands = cloud_bands
		_god_ray_pass.cloud_tightness = cloud_tightness
		_god_ray_pass.cloud_softness = cloud_shadow_softness
		_god_ray_pass.cloud_gap_erosion = cloud_gap_erosion
		_god_ray_pass.cloud_detail_strength = cloud_detail_strength
		_god_ray_pass.cloud_octave_drop = cloud_octave_drop
		_god_ray_pass.cloud_wind = cloud_wind
	if clouds_enabled and cloud_sun != null:
		_push_cloud_sun_dir(-cloud_sun.global_transform.basis.z)


func _push_cloud_params_to(material: Material) -> void:
	material.set_shader_parameter("clouds_enabled", clouds_enabled)
	material.set_shader_parameter("cloud_noise", cloud_noise)
	material.set_shader_parameter("cloud_height", cloud_height)
	material.set_shader_parameter("cloud_noise_scale", cloud_noise_scale)
	material.set_shader_parameter("cloud_threshold", cloud_threshold)
	material.set_shader_parameter("cloud_bands", cloud_bands)
	material.set_shader_parameter("cloud_tightness", cloud_tightness)
	material.set_shader_parameter("cloud_octave_drop", cloud_octave_drop)
	material.set_shader_parameter("cloud_gap_erosion", cloud_gap_erosion)
	material.set_shader_parameter("cloud_detail_strength", cloud_detail_strength)
	material.set_shader_parameter("cloud_wind", cloud_wind)
	material.set_shader_parameter("cloud_shadow_strength", cloud_shadow_strength)
	material.set_shader_parameter("cloud_shadow_banding_enabled", cloud_shadow_banding_enabled)
	material.set_shader_parameter("cloud_shadow_levels", cloud_shadow_levels)
	material.set_shader_parameter("cloud_shadow_softness", cloud_shadow_softness)


func _push_cloud_sun_dir(dir: Vector3) -> void:
	for i in range(_materials.size() - 1, -1, -1):
		var material: Material = _materials[i]
		if material == null:
			_materials.remove_at(i)
			continue
		material.set_shader_parameter("cloud_sun_dir", dir)
	for i in range(_cloud_materials.size() - 1, -1, -1):
		var cloud_material: Material = _cloud_materials[i]
		if cloud_material == null:
			_cloud_materials.remove_at(i)
			continue
		cloud_material.set_shader_parameter("cloud_sun_dir", dir)
	if _god_ray_pass != null:
		_god_ray_pass.cloud_sun_dir = dir


# -----------------------------------------------------------------------------
# Debug overlay
# -----------------------------------------------------------------------------

func _print_debug_overlay() -> void:
	# The scene HUD (demo_debug_hud.gd) reads these same values every frame;
	# this print is the fallback when no HUD is present (once per second).
	if Engine.get_process_frames() % 60 != 0:
		return
	print("3DPixelArt overlay: %s" % get_debug_status_line())


## One-line status for the debug HUD / overlay:
## internal resolution, upscale mode, units-per-texel, sub-texel remainder,
## grid phase, snap delta and pin/outline state.
func get_debug_status_line() -> String:
	var size := Vector2i(get_viewport().get_visible_rect().size)
	var snap_delta := Vector2.ZERO
	if camera != null:
		snap_delta = Vector2(camera.h_offset, camera.v_offset)
	var remainder := get_remainder_texels()
	var grid_phase := Vector2(absf(remainder.x - floorf(remainder.x)), absf(remainder.y - floorf(remainder.y)))
	return "internal %dx%d | view %dx%d | upscale %s | units/px %.5f | orthoW %.3f | remainder (%.3f, %.3f) | grid-phase (%.3f, %.3f) | snap-delta (%.4f, %.4f) | pin %d dil %.1f | outlines %s" % [
		internal_resolution.x, internal_resolution.y,
		size.x, size.y,
		"sharp-bilinear" if upscale_aa else "nearest",
		get_units_per_pixel(), get_ortho_width(),
		remainder.x, remainder.y,
		grid_phase.x, grid_phase.y,
		snap_delta.x, snap_delta.y,
		pin_stencil_id, pin_dilate,
		"on" if (enable_outline and enabled) else "off",
	]
