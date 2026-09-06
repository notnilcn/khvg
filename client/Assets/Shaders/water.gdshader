shader_type spatial;
render_mode cull_disabled, unshaded, shadows_disabled;

// Hex ocean water for WaterComponent3D. Started from the 3dpixelart addon's demo
// water (Pixel Perfect "Pixel Art Oceans" layers 1-5), but deliberately OPAQUE:
// pixelized objects only write depth at anchor fragments, so any transparent
// surface between the camera and a pixelized object draws over it (the object
// "dissolves" into its anchor dots). Opaque water writes real depth, so
// pixelized characters occlude and are occluded correctly by construction.
//
// The depth-texture fade/foam of the demo is replaced by a per-tile `coast`
// instance uniform (set by WaterComponent3D from the server's land set: 1.0 =
// touches land, 0.5 = one hex away, 0.0 = open ocean). The underwater terrain
// is flat level-0 everywhere, so a depth readout carried no information a
// coastal mask doesn't carry better — and the per-hex banding fits the look.
// Refraction (screen texture) is dropped for the same robustness reason.

uniform vec4 shallow_color : source_color = vec4(0.35, 0.75, 0.80, 1.0);
uniform vec4 deep_color : source_color = vec4(0.08, 0.25, 0.45, 1.0);
uniform vec4 foam_color : source_color = vec4(0.90, 0.97, 1.0, 1.0);
uniform sampler2D water_noise : filter_nearest, repeat_enable;

// 1.0 = coastal hex, 0.5 = one hex from land, 0.0 = open ocean.
instance uniform float coast = 0.0;

uniform float wave_height = 0.15;
uniform float wave_frequency = 0.25;
uniform float wave_speed = 1.2;

// Foam coverage on a fully coastal hex (ring-2 hexes get half).
uniform float foam_amount : hint_range(0.0, 1.0) = 0.45;
uniform float foam_noise_scale = 0.10;
uniform vec2 foam_scroll = vec2(0.05, 0.03);

uniform float cap_threshold = 0.88;
uniform float cap_noise_scale = 0.02;
uniform vec2 cap_scroll = vec2(0.06, -0.04);

// Cloud shadows, pushed by PixelArtPipelineComponent through the 3dpixelart
// pipeline's register_cloud_material: same uniform names and coverage shaping
// as pixel_art_object.gdshader. Unshaded water just darkens its albedo.
uniform bool clouds_enabled = false;
uniform sampler2D cloud_noise : filter_nearest, repeat_enable;
uniform vec3 cloud_sun_dir = vec3(0.0, -1.0, 0.0); // world, direction light travels
uniform float cloud_height = 10.0;
uniform float cloud_noise_scale = 0.05;
uniform float cloud_threshold : hint_range(0.0, 1.0) = 0.5;
uniform vec2 cloud_wind = vec2(0.02, 0.0);
uniform float cloud_shadow_strength : hint_range(0.0, 1.0) = 0.6;
uniform bool cloud_shadow_banding_enabled = true;
uniform float cloud_shadow_levels = 3.0;
uniform float cloud_shadow_softness : hint_range(0.0, 1.0) = 0.4;

varying vec3 world_pos;
varying float coast_v;

// Cloud shadow shaping (mirror of pixel_art_object.gdshader): coverage ramps
// from 0 to 1 across [threshold - softness, threshold], optionally quantized
// into discrete darkness levels (hard contour rings).
float cloud_coverage_from_noise(float n) {
  float edge0 = clamp(cloud_threshold - max(cloud_shadow_softness, 0.001), 0.0, 1.0);
  float cov = smoothstep(edge0, cloud_threshold, n);
  if (cloud_shadow_banding_enabled) {
    float levels = max(cloud_shadow_levels, 1.0);
    cov = floor(cov * levels + 0.5) / levels;
  }
  return cov;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	coast_v = coast;
	// Two sine waves 90 degrees apart, multiplied (A x B).
	float wave_a = sin(world_pos.x * wave_frequency + TIME * wave_speed);
	float wave_b = sin(world_pos.z * wave_frequency * 1.3 + TIME * wave_speed * 0.8);
	VERTEX.y += wave_a * wave_b * wave_height;
}

void fragment() {
	vec2 wuv = world_pos.xz;

	// Banded base color: deep ocean -> shallow at the coast, quantized per ring.
	float band = clamp(coast_v, 0.0, 1.0);
	vec3 col = mix(deep_color.rgb, shallow_color.rgb, band);

	// Shoreline foam on coastal hexes, deliberately broken up by noise.
	float foam_n = texture(water_noise, wuv * foam_noise_scale + foam_scroll * TIME).r;
	float foam = step(1.0 - foam_amount * band, foam_n) * step(0.01, band);
	col = mix(col, foam_color.rgb, foam);

	// Whitecaps: a hard threshold on scrolling noise, open water only.
	float cap_n = texture(water_noise, wuv * cap_noise_scale + cap_scroll * TIME).r;
	float caps = step(cap_threshold, cap_n) * (1.0 - band);
	col = mix(col, foam_color.rgb, caps);

	// Cloud shadows: same sun-projected coverage as the terrain, so shadows
	// continue seamlessly across the coastline.
	if (clouds_enabled && cloud_sun_dir.y < -0.02) {
		float cloud_t = (cloud_height - world_pos.y) / cloud_sun_dir.y;
		vec2 cloud_uv = (world_pos + cloud_sun_dir * cloud_t).xz * cloud_noise_scale
			+ cloud_wind * TIME;
		float coverage = cloud_coverage_from_noise(textureLod(cloud_noise, cloud_uv, 0.0).r);
		col *= 1.0 - coverage * cloud_shadow_strength;
	}

	ALBEDO = col;
}
