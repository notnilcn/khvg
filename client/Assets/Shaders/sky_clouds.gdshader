shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

// Visible banded cloud layer, the sky-side companion of the Pixelizer3D addon's
// cloud shadows (+ god rays once the addon ships them): PixelArtPipelineComponent
// feeds it the SAME noise texture, scale, wind, threshold and bands the manager
// pushes to pixelized materials, so the clouds overhead match the shadows on the
// ground and the gaps the god rays pour through.
//
// Deliberately OPAQUE with alpha scissor (hard-edged blobs — the banding is
// already hard-stepped, so nothing is lost): pixelized objects only write
// depth at anchor fragments, so a transparent cloud sheet between the camera
// and a character would draw over it and "dissolve" it into anchor dots.

// Linear filtering (not nearest): nearest exposes the noise texel grid as long
// ruler-straight runs and staircases on blob edges in close top-down ortho
// views, where one texel spans several screen pixels. Linear keeps smooth
// contours; the bands/step below still harden the blobs themselves.
uniform sampler2D cloud_noise : filter_linear, repeat_enable;
uniform vec4 cloud_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float noise_scale = 0.015;
uniform float threshold : hint_range(0.0, 1.0) = 0.72;
uniform float bands = 3.0;
uniform vec2 wind = vec2(0.01, 0.004);
uniform float edge_fade : hint_range(0.0, 0.5) = 0.15; // UV-space fade at the plane border
// Frustum exit-line erosion (driven per-frame by PixelArtPipelineComponent):
// on shallow pitches the tilted ortho box leaves the horizontal deck through its
// side, which would draw a hard straight cut across the frame. The component
// passes its live camera frame; oy_n is the fragment's screen-row in units of
// half the frustum height, and bound_on is the row of that exit line (parked
// off-frame, |bound_on| > 1, when the whole frame hits the deck). Cloud alpha
// erodes with noise breakup around the line so it reads as a natural cloud edge.
uniform vec3 cam_pos;
uniform vec3 cam_up = vec3(0.0, 1.0, 0.0);
uniform float half_h = 1000000.0;
uniform float bound_on = -1000000.0;
uniform float edge_width = 0.10; // half-width of the erosion band, in half-frustums
uniform float edge_wiggle = 0.09; // noise breakup amplitude, in half-frustums

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
	// Dissolve the frustum exit line: ortho rays sharing a screen row share oy_n
	// (all points on one ortho ray project to the same height along cam_up), so
	// this fades whole rows around the line with a noise-broken contour.
	float oy_n = dot(world_pos - cam_pos, cam_up) / half_h;
	float wob = (texture(cloud_noise, world_pos.xz * noise_scale * 3.7 + vec2(0.37, 0.11) + wind * TIME * 1.7).r - 0.5) * 2.0 * edge_wiggle;
	float exit_edge = smoothstep(bound_on - edge_width, bound_on + edge_width, oy_n + wob);
	// Two-tone shading by band so the clouds aren't a flat sheet of white.
	ALBEDO = cloud_color.rgb * (0.82 + 0.18 * n);
	ALPHA = cover * edge * exit_edge;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
}
