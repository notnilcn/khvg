shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

// Visible banded cloud layer, the sky-side companion of the 3dpixelart addon's
// cloud shadows + god rays: PixelArtPipelineComponent feeds it the SAME noise
// texture, scale, wind, threshold and bands the pipeline pushes to pixelized
// materials, so the clouds overhead match the shadows on the ground and the
// gaps the god rays pour through.
//
// Deliberately OPAQUE with alpha scissor (hard-edged blobs — the banding is
// already hard-stepped, so nothing is lost): pixelized objects only write
// depth at anchor fragments, so a transparent cloud sheet between the camera
// and a character would draw over it and "dissolve" it into anchor dots.

uniform sampler2D cloud_noise : filter_nearest, repeat_enable;
uniform vec4 cloud_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float noise_scale = 0.015;
uniform float threshold : hint_range(0.0, 1.0) = 0.72;
uniform float bands = 3.0;
uniform vec2 wind = vec2(0.01, 0.004);
uniform float edge_fade : hint_range(0.0, 0.5) = 0.15; // UV-space fade at the plane border

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float n = texture(cloud_noise, world_pos.xz * noise_scale + wind * TIME).r;
	n = floor(n * bands) / bands; // stepped banding, matches the ground shadows
	float cover = step(threshold, n);
	// Fade the plane border so the sheet never shows a hard rectangular edge.
	vec2 d = abs(UV - 0.5) * 2.0;
	float edge = 1.0 - smoothstep(1.0 - edge_fade * 2.0, 1.0, max(d.x, d.y));
	// Two-tone shading by band so the clouds aren't a flat sheet of white.
	ALBEDO = cloud_color.rgb * (0.82 + 0.18 * n);
	ALPHA = cover * edge;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
}
