shader_type spatial;
render_mode ambient_light_disabled, specular_disabled;

// Base pixel-art material. One shader serves both the main camera (lit,
// pixel-clipped appearance) and the metadata camera (packed metadata output),
// selected by the cull mask of the camera currently rendering.
//
// Core no-creep mechanism: every fragment except
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
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 albedo_st = vec4(1.0, 1.0, 0.0, 0.0); // tiling xy, offset zw
uniform vec4 base_color : source_color = vec4(1.0);
uniform bool use_sharp_bilinear = false; // crisp + anti-aliased texel sampling
uniform sampler2D lighting_ramp : filter_linear, repeat_disable;
uniform vec4 ambient_light : source_color = vec4(0.2, 0.2, 0.2, 1.0);
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float normal_map_depth : hint_range(0.0, 2.0) = 1.0;
uniform bool use_normal_map = false;
uniform sampler2D emission_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
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
// 256x2 HDR outline palette (16-bit float RGBA): row 0 =
// exterior per ID, row 1 = interior per ID, alpha = multiply flag.
uniform sampler2D pixel_art_outline_palette : filter_nearest, repeat_disable, hint_default_black;
uniform bool use_outline_palette = false;
// Mover snap: round the anchor-grid pivot to whole
// screen pixels so moving subjects don't jitter sub-texel. Set by
// PixelArtObject3D.mover_snap as an instance uniform.
instance uniform float use_mover_snap = 0.0;

// ---------------------------------------------------------------------------
// Color grading (per-object, applied to albedo before lighting)
// ---------------------------------------------------------------------------
uniform bool use_color_grading = false;
uniform sampler2D palette_lut : filter_nearest, repeat_disable;

// ---------------------------------------------------------------------------
// Cloud shadows (pushed by PixelArtPipeline3D; attenuates the toon ramp only)
// ---------------------------------------------------------------------------
uniform bool clouds_enabled = false;
uniform sampler2D cloud_noise : filter_nearest, repeat_enable;
uniform vec3 cloud_sun_dir = vec3(0.0, -1.0, 0.0); // world, direction light travels
uniform float cloud_height = 10.0;
uniform float cloud_noise_scale = 0.05;
uniform float cloud_threshold : hint_range(0.0, 1.0) = 0.5;
uniform float cloud_bands = 3.0;
uniform float cloud_tightness = 1.0; // coverage curve tightness
uniform float cloud_octave_drop = 0.5; // FBM amplitude falloff per octave
uniform float cloud_gap_erosion = 0.0; // erodes cloud gaps
uniform float cloud_detail_strength = 0.0; // procedural FBM detail mix (0 = texture only)
uniform vec2 cloud_wind = vec2(0.02, 0.0); // dir * speed
uniform float cloud_shadow_strength : hint_range(0.0, 1.0) = 0.6;
// Shadow shaping: coverage ramps from 0 to 1 across
// [cloud_threshold - cloud_shadow_softness, cloud_threshold], optionally
// quantized into cloud_shadow_levels discrete darkness steps (contour bands).
uniform bool cloud_shadow_banding_enabled = true;
uniform float cloud_shadow_levels = 3.0;
uniform float cloud_shadow_softness : hint_range(0.0, 1.0) = 0.4;

#include "res://addons/3dpixelart/shaders/cloud_fbm.gdshaderinc"

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
varying float cloud_coverage;

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

// Truncating RG decode (floor-based, matches the packing).
float decode_float_rg(vec2 enc) {
	float ex = floor(enc.x * 15.0);
	float ey = floor(enc.y * 15.0);
	return (ex * 16.0 + ey) / 256.0;
}

// Sharp-bilinear texel sampling. With use_sharp_bilinear off this is an
// exact texel-center sample at mip 0 (identical to the old filter_nearest
// behavior); on, it generalizes bilinear filtering by the fragment's texel
// footprint so textures stay crisp but anti-aliased at rotated/minified
// footprints. Minification stability requires mipmaps on the texture.
vec4 sample_pixel_texture(sampler2D tex, vec2 uv) {
	vec2 sz = vec2(textureSize(tex, 0));
	if (!use_sharp_bilinear) {
		vec2 c = (floor(uv * sz) + 0.5) / sz;
		return textureGrad(tex, c, vec2(0.0), vec2(0.0));
	}
	vec2 box = clamp(fwidth(uv) * sz, 1e-5, 1.0);
	vec2 tx = uv * sz - 0.5 * box;
	vec2 offs = smoothstep(vec2(1.0) - box, vec2(1.0), fract(tx));
	vec2 snapped = (floor(tx) + 0.5 + offs) / sz;
	return textureGrad(tex, snapped, dFdx(uv), dFdy(uv));
}

// Palette-LUT color grading: grade a gamma-space color through the 256x256
// palette LUT, dithered by the macro-pixel position.
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

// Cloud shadow shaping: raw noise -> fraction of sun blocked. The coverage
// ramps from 0 to 1 across [threshold - softness, threshold], so the widest
// faintest band borders the clear sky and the core above the threshold is
// uniformly darkest; banding quantizes the ramp into discrete levels.
// Tightness shapes the curve: pow on smooth coverage,
// smoothstep shaping inside bands. Gap erosion eats the coverage edges.
float cloud_coverage_from_noise(float n) {
	float edge0 = clamp(cloud_threshold - max(cloud_shadow_softness, 0.001), 0.0, 1.0);
	float cov = smoothstep(edge0, cloud_threshold, n);
	cov = pow(clamp(cov, 0.0, 1.0), clamp(cloud_tightness, 0.1, 4.0));
	if (cloud_gap_erosion > 0.001) {
		float inv_erode = 1.0 / max(1e-4, 1.0 - cloud_gap_erosion);
		cov = clamp((cov - cloud_gap_erosion) * inv_erode, 0.0, 1.0);
	}
	if (cloud_shadow_banding_enabled) {
		float levels = max(cloud_shadow_levels, 1.0);
		if (levels > 1.5 && cloud_tightness > 1.01) {
			// Band-tightness shaping: smoothstep inside each cell.
			float x = clamp(cov, 0.0, 1.0) * levels;
			float cell = floor(x);
			float f = fract(x);
			float tight = max(cloud_tightness, 1e-3);
			float interp = smoothstep(0.0, 1.0, clamp((f - 0.5) * tight + 0.5, 0.0, 1.0));
			cov = clamp((cell + interp) / levels, 0.0, 1.0);
		} else {
			cov = floor(cov * levels + 0.5) / levels;
		}
	}
	return cov;
}

// Procedural FBM detail mixed with the texture noise (0 = texture only).
float cloud_noise_combined(vec2 base_uv, float time_s) {
	float tex_n = textureLod(cloud_noise, base_uv, 0.0).r;
	if (cloud_detail_strength < 0.001) {
		return tex_n;
	}
	// Quantize the texture contribution into cloud_bands
	// before mixing, so gaps stay aligned with the god-ray field.
	float bands = max(cloud_bands, 2.0);
	float banded_tex = floor(tex_n * bands) / (bands - 1.0);
	float fbm_n = cloud_fbm(vec3(base_uv * 4.0, time_s * 0.05), 4.0, 2.0, clamp(cloud_octave_drop, 0.1, 1.0));
	return mix(banded_tex, fbm_n, clamp(cloud_detail_strength, 0.0, 1.0));
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
	vec4 tex = sample_pixel_texture(albedo_texture, uv);
	float alpha = use_alpha ? tex.a * base_color.a : 1.0;
	cloud_coverage = 0.0;

	if (in_shadow_pass > 0.5) {
		// Shadow/depth-only passes: plain alpha cutout, no anchor clipping.
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
		// ortho projection is already pixel-stable. Mover snap
		// (VIEWPORT_SIZE lattice) forces the same rounding for opted-in
		// moving subjects so they don't jitter sub-texel.
		if (is_ortho < 0.5 || use_mover_snap > 0.5) {
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
			// --- Metadata output ------------------------------------------
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
			// Cloud shadows: project the fragment's world position onto the
			// cloud plane along the sun direction; the shaped coverage
			// attenuates the toon ramp in light(). cloud_sun_dir is the
			// direction the light travels (downward), so t comes out negative.
			if (clouds_enabled && cloud_sun_dir.y < -0.02) {
				vec3 world_pos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
				float cloud_t = (cloud_height - world_pos.y) / cloud_sun_dir.y;
				vec2 cloud_uv = (world_pos + cloud_sun_dir * cloud_t).xz * cloud_noise_scale
					+ cloud_wind * TIME;
				float cloud_n = cloud_noise_combined(cloud_uv, TIME);
				cloud_coverage = cloud_coverage_from_noise(cloud_n);
			}

			vec3 albedo = tex.rgb * base_color.rgb;
			albedo *= mix(vec3(1.0), COLOR.rgb, diffuse_vertex_color_weight);
			if (use_color_grading) {
				albedo = color_grade(albedo, macro_pixel_delta);
			}

			if (use_normal_map) {
				NORMAL_MAP = sample_pixel_texture(normal_map, uv).rgb;
				NORMAL_MAP_DEPTH = normal_map_depth;
			}

			vec4 outline = textureLod(pixel_art_outlines, FRAGCOORD.xy / VIEWPORT_SIZE, 0.0);
			id_outline = outline.r;
			edge_outline = outline.b;

			// Per-ID palette (256x2 HDR): row 0 = exterior,
			// row 1 = interior, alpha = multiply-with-scene flag. When
			// use_outline_palette is off, fall back to per-object colors.
			vec3 outline_col_eff = outline_color.rgb;
			float outline_alpha_eff = outline_color.a;
			vec3 edge_col_eff = edge_highlight_color.rgb;
			float edge_multiply = 1.0;
			float exterior_multiply = 0.0;
			if (use_outline_palette) {
				float pal_u = (clamp(outline_id, 0.0, 255.0) + 0.5) / 256.0;
				vec4 pal_ext = textureLod(pixel_art_outline_palette, vec2(pal_u, 0.25), 0.0);
				vec4 pal_int = textureLod(pixel_art_outline_palette, vec2(pal_u, 0.75), 0.0);
				exterior_multiply = step(0.5, pal_ext.a);
				// Multiply exterior tints the scene albedo; flat replaces.
				outline_col_eff = mix(pal_ext.rgb, pal_ext.rgb * albedo * 2.0, exterior_multiply);
				outline_alpha_eff = 1.0;
				edge_col_eff = pal_int.rgb;
				edge_multiply = pal_int.a;
			}

			// Edge highlight: < 0.5 darkens, 0.5 is neutral, > 0.5 lightens.
			// Flat interior (multiply flag off) replaces the albedo instead.
			vec3 edge_mul = mix(vec3(1.0), edge_col_eff * 2.0, edge_outline * edge_multiply);
			vec3 albedo_flat_mix = mix(albedo, edge_col_eff, edge_outline * (1.0 - edge_multiply));

			ALBEDO = mix(albedo * edge_mul, albedo_flat_mix, (use_outline_palette ? 1.0 : 0.0) * (1.0 - edge_multiply));

			vec3 emissive = vec3(0.0);
			if (use_emission) {
				emissive = sample_pixel_texture(emission_texture, uv).rgb * emission_color.rgb
					* mix(vec3(1.0), COLOR.rgb, emissive_vertex_color_weight);
			}
			vec3 ambient = albedo * ambient_light.rgb;
			// Outlined pixels are replaced by the exterior color (flat or
			// scene-multiplied via the palette flag).
			vec3 lit_base = mix((ambient + emissive) * edge_mul, (ambient + emissive), (use_outline_palette ? 1.0 : 0.0) * (1.0 - edge_multiply) * edge_outline);
			EMISSION = mix(lit_base, outline_col_eff, outline.r * outline_alpha_eff);
		}
	}
}

void light() {
	if (in_metadata_pass > 0.5) {
		DIFFUSE_LIGHT = vec3(0.0);
		SPECULAR_LIGHT = vec3(0.0);
	} else {
		// Cell shading: ramp over the saturated NdotL dot product.
		float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
		vec3 ramp = textureLod(lighting_ramp, vec2(ndl, 0.5), 0.0).rgb;
		float att = receive_shadows ? ATTENUATION : 1.0;
		// cloud_coverage is the fraction of sun blocked (0 = clear).
		att *= 1.0 - cloud_coverage * cloud_shadow_strength;
		DIFFUSE_LIGHT += ramp * LIGHT_COLOR * att * (1.0 - id_outline);
		SPECULAR_LIGHT = vec3(0.0);
	}
}
