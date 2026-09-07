#[compute]
#version 450

// Outline detection pass.
//
// Reads the packed metadata buffer and produces an outline texture:
//   R = ID/silhouette outline, B = normal-crease edge.
// Only anchor pixels of macro-blocks carry metadata; every other pixel is
// background (writes black).
//
// Additional outline terms:
//   * depth second-derivative (FarCurve) exterior test: max(-H, -V) over the
//     raw reversed-Z depth at macro spacing catches silhouettes where
//     same-ID surfaces fold away from the camera.
//   * world-space normal-bias fallback (-0.5, 0.5, 1) when the packed normal
//     is degenerate, so crease detection degrades gracefully.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(rgba8, set = 0, binding = 0) uniform writeonly image2D outlines;
layout(set = 0, binding = 1) uniform sampler2D metadata;
layout(set = 0, binding = 2) uniform sampler2D metadata_depth;

layout(push_constant, std430) uniform Params {
	vec4 settings; // x: depth test threshold, y: 1/normal edge sensitivity, z: use depth test (0/1), w: use normal edges (0/1)
	vec4 settings2; // x: depth curve threshold, y: use depth curve (0/1), z: use normal bias fallback (0/1), w: pass enabled (0 = write black)
} params;

// ID/size packing: id (0-255) and pixel size (0-5) packed into G as
// (id + pixelSize * 256) / 4096. The white background decodes to size 16,
// which is out of range and means "not pixelized".
float metadata_pixel_size(float g) {
	float ps = floor(round(g * 4096.0) / 256.0);
	return ps <= 5.0 ? ps : 0.0;
}

float get_uid(vec4 data) {
	return mod(round(data.g * 4096.0), 256.0);
}

ivec2 clamp_texel(ivec2 t, ivec2 size) {
	return clamp(t, ivec2(0), size - 1);
}

// Reconstruct the view-space normal stored in the metadata R/B channels,
// with the world-space bias fallback for degenerate input.
vec3 get_outline_normal(ivec2 t) {
	vec2 rg = texelFetch(metadata, t, 0).rb * 2.0 - 1.0;
	if (params.settings2.z > 0.5 && dot(rg, rg) > 1.0) {
		return normalize(vec3(-0.5, 0.5, 1.0));
	}
	float b = sqrt(max(0.0, 1.0 - dot(rg, rg)));
	return vec3(rg, b);
}

void main() {
	ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
	ivec2 size = imageSize(outlines);
	if (pixel.x >= size.x || pixel.y >= size.y) {
		return;
	}

	if (params.settings2.w < 0.5) {
		imageStore(outlines, pixel, vec4(0.0, 0.0, 0.0, 1.0));
		return;
	}

	vec4 packed_data = texelFetch(metadata, pixel, 0);
	float pixel_size = metadata_pixel_size(packed_data.g);
	if (pixel_size < 1.0) {
		imageStore(outlines, pixel, vec4(0.0, 0.0, 0.0, 1.0));
		return;
	}
	float id = get_uid(packed_data);
	int p = int(pixel_size);

	bool use_depth_test = params.settings.z > 0.5;
	bool use_normal_edges = params.settings.w > 0.5;
	float depth = use_depth_test ? texelFetch(metadata_depth, pixel, 0).r : 0.0;

	// ID outlines: 3x3 taps at a distance of pixel_size texels (i.e. the
	// neighbouring macro-pixels). A neighbour counts as "similar" when it has
	// the same ID and is pixelized; with depth testing, a neighbour that is
	// clearly in front also counts as similar (suppresses outlines where
	// objects intersect). Reversed-Z: larger depth == nearer.
	float count_similar = 0.0;
	for (int u = -1; u <= 1; u++) {
		for (int v = -1; v <= 1; v++) {
			ivec2 t = clamp_texel(pixel + ivec2(u, v) * p, size);
			vec4 neighbour = texelFetch(metadata, t, 0);
			bool similar = get_uid(neighbour) == id && metadata_pixel_size(neighbour.g) > 0.5;
			if (use_depth_test) {
				float neighbour_depth = texelFetch(metadata_depth, t, 0).r;
				similar = similar || (neighbour_depth > depth + params.settings.x);
			}
			count_similar += similar ? 1.0 : 0.0;
		}
	}
	float id_factor = count_similar > 7.0 ? 0.0 : 1.0;

	// Depth second-derivative (FarCurve) exterior: where the surface curves
	// away from the camera (far side), force an exterior outline even when
	// the ID neighbourhood agrees. Reversed-Z raw depth: the far side gives
	// negative second differences, hence max(-H, -V).
	if (params.settings2.y > 0.5) {
		float d_c = texelFetch(metadata_depth, pixel, 0).r;
		float d_l = texelFetch(metadata_depth, clamp_texel(pixel + ivec2(-p, 0), size), 0).r;
		float d_r = texelFetch(metadata_depth, clamp_texel(pixel + ivec2(p, 0), size), 0).r;
		float d_u = texelFetch(metadata_depth, clamp_texel(pixel + ivec2(0, -p), size), 0).r;
		float d_d = texelFetch(metadata_depth, clamp_texel(pixel + ivec2(0, p), size), 0).r;
		float h_curve = d_l + d_r - 2.0 * d_c;
		float v_curve = d_u + d_d - 2.0 * d_c;
		float far_curve = max(-h_curve, -v_curve);
		if (far_curve > params.settings2.x) {
			id_factor = 1.0;
		}
	}

	// Normal-crease edges: finite differences of the reconstructed normals at
	// macro-pixel spacing. Each axis is zeroed unless both neighbours share
	// the ID (prevents false creases at silhouettes).
	float normal_factor = 0.0;
	if (use_normal_edges) {
		vec3 n_left = get_outline_normal(clamp_texel(pixel + ivec2(-p, 0), size));
		vec3 n_center = get_outline_normal(pixel);
		vec3 n_up = get_outline_normal(clamp_texel(pixel + ivec2(0, -p), size));
		vec3 d_normal_x = n_center - n_left;
		vec3 d_normal_y = n_center - n_up;

		vec4 na = texelFetch(metadata, clamp_texel(pixel + ivec2(p, 0), size), 0);
		vec4 nb = texelFetch(metadata, clamp_texel(pixel - ivec2(p, 0), size), 0);
		float x_test = (get_uid(na) == get_uid(nb) && get_uid(na) == id) ? 1.0 : 0.0;
		na = texelFetch(metadata, clamp_texel(pixel + ivec2(0, p), size), 0);
		nb = texelFetch(metadata, clamp_texel(pixel - ivec2(0, p), size), 0);
		float y_test = (get_uid(na) == get_uid(nb) && get_uid(na) == id) ? 1.0 : 0.0;

		float edge_normal_sq = dot(d_normal_x, d_normal_x) * x_test + dot(d_normal_y, d_normal_y) * y_test;
		normal_factor = edge_normal_sq > params.settings.y ? 1.0 : 0.0;
	}

	imageStore(outlines, pixel, vec4(id_factor, 0.0, normal_factor, 1.0));
}
