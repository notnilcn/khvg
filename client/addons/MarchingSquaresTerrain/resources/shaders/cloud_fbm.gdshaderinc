// Vendored procedural FBM so the public toolkit is self-contained. The game's
// Pixelizer3D addon ships its own include with the shared Effects.Cloud_Shadows
// uniforms (main/client/addons/pixelizer_3d/shaders/cloud_fbm.gdshaderinc) —
// keep the cloud_* uniform names and coverage shaping in sync with it
// (see AGENTS.md gotcha).

// Shared procedural cloud FBM for spatial shaders (twin of cloud_fbm.glslinc).
// Hash 1664525/1013904223, rotation (0.8253356, 0.5646425).

float cloud_hash_fbm_iv(vec3 ip) {
	uint ux = uint(int(ip.x)) * 1664525u + 1013904223u;
	uint uy = uint(int(ip.y)) * 1664525u + 1013904223u;
	uint uz = uint(int(ip.z)) * 1664525u + 1013904223u;
	ux += uy * uz;
	uy += uz * ux;
	uz += ux * uy;
	ux ^= (ux >> 16u);
	ux += uy * uz;
	return float(ux & 0x00FFFFFFu) / 16777215.0;
}

float cloud_value_noise_fbm(vec3 p) {
	vec3 f = floor(p);
	vec3 t = p - f;
	t = t * t * (3.0 - 2.0 * t);
	float n000 = cloud_hash_fbm_iv(f + vec3(0.0, 0.0, 0.0));
	float n100 = cloud_hash_fbm_iv(f + vec3(1.0, 0.0, 0.0));
	float n010 = cloud_hash_fbm_iv(f + vec3(0.0, 1.0, 0.0));
	float n110 = cloud_hash_fbm_iv(f + vec3(1.0, 1.0, 0.0));
	float n001 = cloud_hash_fbm_iv(f + vec3(0.0, 0.0, 1.0));
	float n101 = cloud_hash_fbm_iv(f + vec3(1.0, 0.0, 1.0));
	float n011 = cloud_hash_fbm_iv(f + vec3(0.0, 1.0, 1.0));
	float n111 = cloud_hash_fbm_iv(f + vec3(1.0, 1.0, 1.0));
	float nx00 = mix(n000, n100, t.x);
	float nx10 = mix(n010, n110, t.x);
	float nx01 = mix(n001, n101, t.x);
	float nx11 = mix(n011, n111, t.x);
	return mix(mix(nx00, nx10, t.y), mix(nx01, nx11, t.y), t.z);
}

float cloud_fbm_norm(vec3 p, float octaves, float norm_octaves, float lacunarity, float gain) {
	int oct = int(clamp(floor(octaves + 0.5), 1.0, 8.0));
	int norm_oct = int(clamp(floor(norm_octaves + 0.5), float(oct), 8.0));
	float lac = max(lacunarity, 1.0);
	float g = clamp(gain, 0.0, 1.0);
	vec2 rot = vec2(0.8253356, 0.5646425);
	float amp = 1.0;
	float sum = 0.0;
	float norm = 0.0;
	for (int i = 0; i < 8; i++) {
		if (i >= norm_oct) {
			break;
		}
		float value = 0.5;
		if (i < oct) {
			value = cloud_value_noise_fbm(p);
			p.xy = vec2(p.x * rot.x - p.y * rot.y, p.x * rot.y + p.y * rot.x) * lac;
			p.z *= lac;
		}
		sum += amp * value;
		norm += amp;
		amp *= g;
	}
	return sum / max(norm, 1e-4);
}

float cloud_fbm(vec3 p, float octaves, float lacunarity, float gain) {
	return cloud_fbm_norm(p, octaves, octaves, lacunarity, gain);
}
