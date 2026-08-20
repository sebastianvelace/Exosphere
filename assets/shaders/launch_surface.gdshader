// Procedural launch-site surface detail for primitive concrete, asphalt and sand meshes.
// This is a calibrated visual reconstruction, not measured photogrammetry.
shader_type spatial;
render_mode diffuse_burley, cull_back;

uniform vec4 base_color : source_color = vec4(0.30, 0.30, 0.28, 1.0);
uniform float detail_scale : hint_range(0.01, 2.0) = 0.22;
uniform float detail_strength : hint_range(0.0, 0.8) = 0.28;
uniform float grain_strength : hint_range(0.0, 0.5) = 0.10;
uniform float wetness : hint_range(0.0, 1.0) = 0.0;

varying vec3 v_world_position;

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = dot(hash2(i), f);
	float b = dot(hash2(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(hash2(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(hash2(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float noise01(vec2 p) {
	return clamp(0.5 + 0.5 * noise(p), 0.0, 1.0);
}

void vertex() {
	v_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// World-space coordinates stay stable while the camera and vessel move and
	// avoid UV stretching differences between BoxMesh and CylinderMesh surfaces.
	vec2 p = v_world_position.xz;
	float macro = noise01(p * detail_scale * 0.08 + vec2(13.0, -7.0));
	float aggregate = noise01(p * detail_scale * 0.55 + vec2(-31.0, 17.0));
	float grain = noise01(p * detail_scale * 5.0 + vec2(47.0, 29.0));
	float weather = mix(0.82, 1.18, macro * 0.65 + aggregate * 0.35);
	vec3 albedo = base_color.rgb * weather;
	albedo += (grain - 0.5) * grain_strength;
	albedo = mix(albedo, albedo * vec3(0.68, 0.73, 0.78), wetness * 0.45);
	ALBEDO = clamp(albedo, vec3(0.0), vec3(1.0));
	ROUGHNESS = mix(0.96, 0.72, wetness);
	METALLIC = 0.0;
}
