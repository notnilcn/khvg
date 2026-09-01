shader_type spatial;
render_mode ambient_light_disabled, specular_disabled;

// ProPixelizer base material — Godot port of SRP/PixelizedWithOutline.shader +
// ProPixelizerBase.shadergraph. One shader serves both the main camera (lit,
// pixel-clipped appearance) and the metadata camera (packed metadata output),
// selected by the cull mask of the camera currently rendering.
//
// Core no-creep mechanism (port of SRP/PixelUtils.hlsl): every fragment except
// one "anchor" per macro-block is discarded, on a screen grid anchored to the
// object's projected pivot. The compositor's pixelization pass then replicates
// the anchor color across the block.
//
// This shader never writes ALPHA. Any ALPHA write would make Godot sort the
// material into the TRANSPARENT pass (plain `discard` does not count as alpha
// clipping there) — after the POST_SKY compositor effects that must already
// see these objects in the color/depth layers. Alpha scissor/hash would fix
// the sorting but the engine then forces output alpha to 1.0, destroying any
// metadata payload. So the pixel size is packed into the G channel instead.

// ---------------------------------------------------------------------------
// Appearance
// ---------------------------------------------------------------------------
uniform sampler2D albedo_texture : source_color, filter_nearest, repeat_enable;
uniform vec4 albedo_st = vec4(1.0, 1.0, 0.0, 0.0); // tiling xy, offset zw
uniform vec4 base_color : source_color = vec4(1.0);
uniform sampler2D lighting_ramp : filter_linear, repeat_disable;
uniform vec4 ambient_light : source_color = vec4(0.2, 0.2, 0.2, 1.0);
uniform sampler2D normal_map : hint_normal, filter_nearest, repeat_enable;
uniform float normal_map_depth : hint_range(0.0, 2.0) = 1.0;
uniform bool use_normal_map = false;
uniform sampler2D emission_texture : source_color, filter_nearest, repeat_enable;
uniform vec4 emission_color : source_color = vec4(1.0);
uniform bool use_emission = false;
uniform bool receive_shadows = true;
uniform float diffuse_vertex_color_weight : hint_range(0.0, 1.0) = 1.0;
uniform float emissive_vertex_color_weight : hint_range(0.0, 1.0) = 0.0;

// ---------------------------------------------------------------------------
// Pixelization (per-object via instance uniforms, set by PixelArtObject3D)
// ---------------------------------------------------------------------------
instance uniform float pixel_size : hint_range(1.0, 5.0, 1.0) = 3.0;
instance uniform vec3 pixel_grid_origin = vec3(0.0);
instance uniform bool use_object_position = true; // anchor grid to object pivot
uniform float alpha_clip_threshold : hint_range(0.0, 1.0) = 0.5;
uniform bool use_alpha = false; // multiply alpha by albedo alpha * base color alpha
uniform bool use_dithering = true; // 4x4 Bayer screen-door transparency

// ---------------------------------------------------------------------------
// Outlines (per-object via instance uniforms)
// ---------------------------------------------------------------------------
instance uniform float outline_id : hint_range(0.0, 255.0, 1.0) = 1.0;
instance uniform vec4 outline_color = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform vec4 edge_highlight_color = vec4(0.5, 0.5, 0.5, 1.0);
// Filled by PixelArtPipeline3D at runtime; black = no outlines.
uniform sampler2D pixel_art_outlines : filter_nearest, repeat_disable, hint_default_black;

// ---------------------------------------------------------------------------
// Color grading (per-object, applied to albedo before lighting)
// ---------------------------------------------------------------------------
uniform bool use_color_grading = false;
uniform sampler2D palette_lut : filter_nearest, repeat_disable;

// ---------------------------------------------------------------------------
// Metadata pass
// ---------------------------------------------------------------------------
uniform float metadata_cull_mask = 524288.0; // bit 19 = render layer 20
uniform bool write_view_normals = true; // R/B = view normal (crease edges); else outline color nibbles

varying vec4 pivot_obj_clip;
varying vec4 pivot_grid_clip;
varying float is_ortho;
varying float in_shadow_pass;
varying float in_metadata_pass;
varying float id_outline;
varying float edge_outline;

// Classic Bayer 4x4, values (n+1)/17, x-major: TRANSPARENCY_DITHER[x][y].
const mat4 TRANSPARENCY_DITHER = mat4(
	vec4(1.0 / 17.0, 9.0 / 17.0, 3.0 / 17.0, 11.0 / 17.0),
	vec4(13.0 / 17.0, 5.0 / 17.0, 15.0 / 17.0, 7.0 / 17.0),
	vec4(4.0 / 17.0, 12.0 / 17.0, 2.0 / 17.0, 10.0 / 17.0),
	vec4(16.0 / 17.0, 8.0 / 17.0, 14.0 / 17.0, 6.0 / 17.0)
);

const float MAXCOLOR = 16.0;
const float LUT_RES = 16.0;
const float DITHER_SIZE = 16.0;

// PackingUtils.hlsl DecodeFloatRG (truncating, as in the original).
float decode_float_rg(vec2 enc) {
	float ex = floor(enc.x * 15.0);
	float ey = floor(enc.y * 15.0);
	return (ex * 16.0 + ey) / 256.0;
}

// ColorGrading.hlsl: grade a gamma-space color through the 256x256 palette
// LUT, dithered by the macro-pixel position.
vec3 color_grade(vec3 color, vec2 macro_pixel_delta) {
	vec3 orig = pow(clamp(color, vec3(0.0), vec3(1.0)), vec3(1.0 / 2.2));
	orig = clamp(orig, vec3(0.0), vec3(0.9999));

	float u = (clamp(orig.r * LUT_RES, 0.0, LUT_RES - 1.0) + 0.5) / (LUT_RES * LUT_RES);
	float v = (clamp(orig.g * LUT_RES, 0.0, LUT_RES - 1.0) + 0.5) / (LUT_RES * DITHER_SIZE);
	float cell = floor(clamp(orig.b * MAXCOLOR, 0.0, LUT_RES - 1.0) + 0.5);
	u += cell / LUT_RES;

	float band = (mod(macro_pixel_delta.x, 4.0) * 4.0 + mod(macro_pixel_delta.y, 4.0)) / DITHER_SIZE;
	v += band;

	return textureLod(palette_lut, vec2(u, v), 0.0).rgb;
}

void vertex() {
	pivot_obj_clip = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0);
	pivot_grid_clip = PROJECTION_MATRIX * VIEW_MATRIX * vec4(pixel_grid_origin, 1.0);
	// Godot: [3][3] == 1.0 for orthographic projection, 0.0 for perspective.
	is_ortho = PROJECTION_MATRIX[3][3];
	in_shadow_pass = IN_SHADOW_PASS ? 1.0 : 0.0;
}

void fragment() {
	vec2 uv = UV * albedo_st.xy + albedo_st.zw;
	vec4 tex = texture(albedo_texture, uv);
	float alpha = use_alpha ? tex.a * base_color.a : 1.0;

	if (in_shadow_pass > 0.5) {
		// Shadow/depth-only passes (port of PixelUtils.hlsl SHADOWTEST):
		// plain alpha cutout, no anchor clipping.
		if (alpha < alpha_clip_threshold) {
			discard;
		}
		ALBEDO = vec3(0.0);
		id_outline = 0.0;
		edge_outline = 0.0;
		in_metadata_pass = 0.0;
	} else {
		// --- PixelClipAlpha_float -----------------------------------------
		vec4 pivot_clip = use_object_position ? pivot_obj_clip : pivot_grid_clip;
		vec2 obj_ndc = pivot_clip.xy / pivot_clip.w;
		vec2 obj_pixel_pos = (obj_ndc * 0.5 + 0.5) * VIEWPORT_SIZE;
		float pixel = max(1.0, round(pixel_size));
		// Perspective needs the pivot rounded to kill sub-pixel jitter;
		// ortho projection is already pixel-stable.
		if (is_ortho < 0.5) {
			obj_pixel_pos = round(obj_pixel_pos);
		}

		vec2 pos_cs = floor(FRAGCOORD.xy);
		vec2 delta = floor(pos_cs - floor(obj_pixel_pos + 0.1)) + 0.01;
		float xfactor = step(mod(delta.x, pixel), 0.1);
		float yfactor = step(mod(delta.y, pixel), 0.1);
		vec2 macro_pixel_delta = delta / pixel;

		// Ordered dithering for transparency, in macro-pixel space.
		if (use_dithering) {
			int dither_x = int(mod(macro_pixel_delta.x, 4.0));
			int dither_y = int(mod(macro_pixel_delta.y, 4.0));
			alpha *= step(TRANSPARENCY_DITHER[dither_x][dither_y], alpha);
		}
		alpha = step(alpha_clip_threshold, alpha);

		// Always draw screen-edge pixels.
		float x_edge = min(1.0, step(FRAGCOORD.x, 1.0) + step(VIEWPORT_SIZE.x - 1.0, FRAGCOORD.x));
		float y_edge = min(1.0, step(FRAGCOORD.y, 1.0) + step(VIEWPORT_SIZE.y - 1.0, FRAGCOORD.y));
		float draw = min(1.0, (x_edge + xfactor) * (y_edge + yfactor));
		if (alpha * draw < 0.5) {
			discard;
		}

		in_metadata_pass = (float(CAMERA_VISIBLE_LAYERS) == metadata_cull_mask) ? 1.0 : 0.0;
		if (in_metadata_pass > 0.5) {
			// --- Metadata output (port of SRP/OutlinePass.hlsl) ------------
			// Tonemapping for this camera is linear, values pass through as-is.
			vec3 packed_data;
			if (write_view_normals) {
				packed_data.rb = NORMAL.xy * 0.5 + 0.5;
			} else {
				packed_data.r = decode_float_rg(outline_color.rg * 0.5);
				packed_data.b = decode_float_rg(outline_color.ba * 0.5);
			}
			// Outline ID and pixel size packed jointly into G as
			// (id + pixelSize * 256) / 4096: exact in the RGBA16F scene buffer
			// and in the RGBA16 shared export texture (id <= 255, size <= 5).
			// The white background decodes to size 16 = "not pixelized".
			// (ALPHA is deliberately NOT written: any ALPHA write without a
			// scissor/hash threshold sorts the material into the transparent
			// pass, and with one the engine forces output alpha to 1.0 —
			// either way the metadata payload or the render order breaks.)
			packed_data.g = (mod(outline_id, 256.0) + clamp(pixel, 0.0, 5.0) * 256.0) / 4096.0;
			ALBEDO = vec3(0.0);
			EMISSION = packed_data;
			id_outline = 0.0;
			edge_outline = 0.0;
		} else {
			// --- Lit appearance ---------------------------------------------
			vec3 albedo = tex.rgb * base_color.rgb;
			albedo *= mix(vec3(1.0), COLOR.rgb, diffuse_vertex_color_weight);
			if (use_color_grading) {
				albedo = color_grade(albedo, macro_pixel_delta);
			}

			if (use_normal_map) {
				NORMAL_MAP = texture(normal_map, uv).rgb;
				NORMAL_MAP_DEPTH = normal_map_depth;
			}

			vec4 outline = textureLod(pixel_art_outlines, FRAGCOORD.xy / VIEWPORT_SIZE, 0.0);
			id_outline = outline.r;
			edge_outline = outline.b;

			// Edge highlight: < 0.5 darkens, 0.5 is neutral, > 0.5 lightens.
			vec3 edge_mul = mix(vec3(1.0), edge_highlight_color.rgb * 2.0, edge_outline);

			ALBEDO = albedo * edge_mul;

			vec3 emissive = vec3(0.0);
			if (use_emission) {
				emissive = texture(emission_texture, uv).rgb * emission_color.rgb
					* mix(vec3(1.0), COLOR.rgb, emissive_vertex_color_weight);
			}
			vec3 ambient = albedo * ambient_light.rgb;
			// Outlined pixels are replaced by the flat outline color.
			EMISSION = mix((ambient + emissive) * edge_mul, outline_color.rgb, outline.r * outline_color.a);
		}
	}
}

void light() {
	if (in_metadata_pass > 0.5) {
		DIFFUSE_LIGHT = vec3(0.0);
		SPECULAR_LIGHT = vec3(0.0);
	} else {
		// Cell shading: ramp over saturated NdotL (port of the CellShade
		// subgraph fed by CalculateLighting's saturated dot product).
		float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
		vec3 ramp = textureLod(lighting_ramp, vec2(ndl, 0.5), 0.0).rgb;
		float att = receive_shadows ? ATTENUATION : 1.0;
		DIFFUSE_LIGHT += ramp * LIGHT_COLOR * att * (1.0 - id_outline);
		SPECULAR_LIGHT = vec3(0.0);
	}
}
