// Exosphere — VAB studio floor.
//
// A procedural reconstruction for the preview only: large concrete/epoxy panels,
// expansion seams, low-frequency wear and fine aggregate. It is deliberately kept
// separate from launch_surface.gdshader so a VAB material change cannot alter the
// flight pad or any physical surface.
shader_type spatial;
render_mode diffuse_burley, cull_back;

uniform vec4 base_color : source_color = vec4(0.16, 0.18, 0.20, 1.0);
uniform vec4 seam_color : source_color = vec4(0.035, 0.045, 0.055, 1.0);
uniform vec2 panel_size = vec2(12.0, 12.0);
uniform float seam_width : hint_range(0.01, 0.40) = 0.12;
uniform float wear_strength : hint_range(0.0, 0.40) = 0.16;
uniform float grain_strength : hint_range(0.0, 0.20) = 0.055;

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
	vec2 p = v_world_position.xz;
	vec2 panels = max(panel_size, vec2(0.25));
	vec2 cell = p / panels;
	vec2 edge = abs(fract(cell + 0.5) - 0.5) * panels;
	float seam = 1.0 - smoothstep(seam_width, seam_width * 1.8,
		min(edge.x, edge.y));

	float macro = noise01(p * 0.035 + vec2(11.0, -7.0));
	float wear = noise01(p * 0.16 + vec2(-19.0, 23.0));
	float grain = noise01(p * 1.8 + vec2(37.0, 5.0));
	float variation = mix(0.86, 1.14, macro * 0.65 + wear * 0.35);
	vec3 albedo = base_color.rgb * variation;
	albedo += (grain - 0.5) * grain_strength;
	albedo = mix(albedo, albedo * vec3(0.72, 0.76, 0.82), wear * wear_strength);
	albedo = mix(albedo, seam_color.rgb, seam);

	ALBEDO = clamp(albedo, vec3(0.0), vec3(1.0));
	ROUGHNESS = 0.86;
	METALLIC = 0.0;
}
