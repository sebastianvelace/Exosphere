// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Local Earth ground patch, textured from the real Blue Marble map.
//
// A large tangent-plane mesh anchored under the vessel by EarthGroundController.
// Each fragment reconstructs its real lat/lon (from the sub-vessel point + its patch
// offset over the sphere) and samples the same NASA day texture as the orbital
// backdrop, so the launch zone shows the REAL Cape Canaveral coast/terrain — and as
// the rocket climbs those features recede, giving a clear sense of rising. A subtle
// procedural micro-detail keeps it from looking flat up close. `horizon_dist` hazes
// terrain beyond the true horizon into the sky (flat horizon, no dome); `fade`
// cross-fades the whole patch into the scaled-space backdrop on ascent.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

uniform sampler2D day_tex : source_color, filter_linear_mipmap;
uniform vec3  sub_p       = vec3(0.0, 1.0, 0.0);  // sub-vessel point on the unit sphere (texture space)
uniform vec3  east_local  = vec3(1.0, 0.0, 0.0);  // patch +X (east)  in texture space
uniform vec3  north_local = vec3(0.0, 0.0, 1.0);  // patch +Z (north) in texture space
uniform float earth_radius = 6371000.0;           // metres

uniform float fade : hint_range(0.0, 1.0) = 1.0;
uniform float horizon_dist = 4000.0;
// Matches the SkyController ground-horizon blue so the patch dissolves into the sky with
// no grey seam (EarthGroundController overrides it with the live sky horizon colour).
uniform vec3  haze_color : source_color = vec3(0.40, 0.65, 1.0);
uniform vec3  sun_dir = vec3(0.42, 0.78, 0.46);
uniform float detail_strength : hint_range(0.0, 0.5) = 0.14;

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec2 v_patch;   // patch-local (east, north) offset in metres (UV2)

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}
float gnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(dot(hash2(i + vec2(0,0)), f - vec2(0,0)),
	               dot(hash2(i + vec2(1,0)), f - vec2(1,0)), u.x),
	           mix(dot(hash2(i + vec2(0,1)), f - vec2(0,1)),
	               dot(hash2(i + vec2(1,1)), f - vec2(1,1)), u.x), u.y);
}
float fbm(vec2 p) {
	float v = 0.0, a = 0.5, f = 1.0;
	for (int i = 0; i < 5; i++) { v += a * gnoise(p * f); f *= 2.0; a *= 0.5; }
	return v;
}

vec2 equirect(vec3 p) {
	float lon = atan(p.z, p.x);
	float lat = asin(clamp(p.y, -1.0, 1.0));
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

void vertex() {
	v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_patch        = UV2;
}

void fragment() {
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);

	// Reconstruct this fragment's direction on the sphere → equirect UV → real texture.
	float ae = v_patch.x / earth_radius;
	float an = v_patch.y / earth_radius;
	vec3  dir = normalize(sub_p + ae * east_local + an * north_local);
	vec2  uv  = equirect(dir);
	vec3  surface = texture(day_tex, uv).rgb;

	// Subtle procedural micro-detail so the (low-res) texture isn't flat up close.
	float det = fbm(v_patch * 0.0006) + 0.5 * fbm(v_patch * 0.0028);
	surface *= 1.0 + detail_strength * det;

	// Simple sun diffuse so relief/coast reads.
	float ndl = max(dot(N, L), 0.0);
	vec3 lit = surface * (0.55 + 0.6 * ndl);

	// Atmospheric perspective: distant ground fades toward the SKY-horizon colour and then
	// dissolves to transparent, so it blends seamlessly into the sky/backdrop with no grey
	// band. The colour blend leads the alpha fade so the seam is never a hard line.
	float dist = length(v_world_pos - CAMERA_POSITION_WORLD);
	float haze = smoothstep(horizon_dist * 0.60, horizon_dist * 1.05, dist);
	lit = mix(lit, haze_color, haze);
	float edge = smoothstep(horizon_dist * 0.90, horizon_dist * 1.12, dist);

	ALBEDO    = lit;
	ROUGHNESS = 0.85;
	METALLIC  = 0.0;
	ALPHA     = clamp(fade, 0.0, 1.0) * (1.0 - edge);
}
