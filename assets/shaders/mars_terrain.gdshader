// Local low-altitude Mars terrain. This is deliberately separate from the scaled
// planet shader: the terrain patch is a tangent mesh with vertex-colour albedo.
// Direct lighting follows the same bounded solar visibility contract as bodies,
// preventing night exposure adaptation from turning regolith into a white plane.
shader_type spatial;
render_mode cull_back, unshaded;

uniform vec3 sun_dir = vec3(0.4, 0.5, 0.8);
uniform float solar_visibility : hint_range(0.0, 1.0) = 1.0;
uniform float day_gain : hint_range(0.0, 1.5) = 0.92;
uniform float night_floor : hint_range(0.0, 0.05) = 0.006;
uniform vec3 night_tint : source_color = vec3(0.58, 0.38, 0.30);
uniform float rim_strength : hint_range(0.0, 0.08) = 0.045;
uniform float rim_power : hint_range(1.0, 6.0) = 3.0;

// The terrain controller intentionally keeps its legacy 0.006 value. Keep a
// shader-side minimum so a nightside patch remains readable, while the direct
// solar term stays fully gated by solar_visibility. This is an appearance
// floor, not additional illumination in the simulation.
const float MIN_READABLE_FLOOR = 0.012;

varying vec3 v_local_position;

void vertex() {
	v_local_position = VERTEX;
}

vec2 terrain_hash(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453);
}

float terrain_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = dot(terrain_hash(i), f);
	float b = dot(terrain_hash(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(terrain_hash(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(terrain_hash(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float terrain_fbm(vec2 p) {
	float value = 0.0;
	float amplitude = 0.5;
	for (int i = 0; i < 4; i++) {
		value += amplitude * terrain_noise(p);
		p *= 2.07;
		amplitude *= 0.5;
	}
	return value;
}

void fragment() {
	vec3 N = normalize(NORMAL);
	vec3 V = normalize(VIEW);
	float ndl = dot(N, normalize(sun_dir));
	float day = smoothstep(-0.12, 0.22, ndl);
	float floor_term = max(night_floor, MIN_READABLE_FLOOR);
	float solar_term = day * day_gain * solar_visibility;
	float grazing = pow(1.0 - abs(dot(N, V)), rim_power);
	float rim_visibility = mix(0.35, 1.0, solar_visibility);
	vec3 rim = night_tint * grazing * rim_strength * rim_visibility;
	// Vertex colour carries broad rust elevation. These world-locked scales add
	// visible dust/dune breakup to the 10 km horizon without claiming measured
	// Martian topography or introducing a texture lookup in the local patch.
	float macro = 0.5 + 0.5 * terrain_fbm(v_local_position.xz * 0.00018);
	float fines = 0.5 + 0.5 * terrain_fbm(v_local_position.xz * 0.0028 + vec2(19.0, -7.0));
	vec3 terrain_color = COLOR.rgb;
	terrain_color *= mix(vec3(0.78, 0.76, 0.72), vec3(1.18, 1.08, 0.94), macro);
	terrain_color = mix(terrain_color, terrain_color * vec3(0.72, 0.68, 0.62),
		smoothstep(0.55, 0.92, fines) * 0.22);
	ALBEDO = terrain_color * (floor_term + solar_term) + rim;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
}
