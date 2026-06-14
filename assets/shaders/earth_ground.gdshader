// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Local Earth ground patch (TRUE-scale, flat-horizon).
//
// A large tangent-plane mesh anchored under the vessel by EarthGroundController.
// The mesh geometry only encodes the true sphere CURVATURE (a gentle parabolic
// dip away from the centre). All the SURFACE LOOK — ocean / land / coastlines /
// relief — is procedural and sampled from a WORLD-SPACE 2D coordinate fed in via
// `ground_offset`. As the vessel translates horizontally the controller scrolls
// that offset, so continents/coastlines glide across the patch and you can SEE
// the rocket moving. `fade` cross-fades the whole patch out into the scaled-space
// backdrop as altitude climbs toward orbit.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

// World-space ground coordinate of the patch centre (metres), supplied per-frame.
// Drives the noise sampling so the surface scrolls under the vessel.
uniform vec2  ground_offset = vec2(0.0, 0.0);
// Overall surface fade (1 = fully visible, 0 = invisible → backdrop takes over).
uniform float fade : hint_range(0.0, 1.0) = 1.0;
// Metres covered per noise unit — larger = bigger continents.
uniform float feature_scale = 120000.0;
// Direction toward the sun (world space) for simple shading.
uniform vec3  sun_dir = vec3(0.4, 0.6, 0.3);
// Distance to the true horizon in RENDER UNITS (fed per-frame from altitude). Ground
// beyond this fades into atmospheric haze, so the far curved-down terrain disappears
// into a flat hazy horizon instead of reading as a small-planet dome.
uniform float horizon_dist = 4000.0;
uniform vec3  haze_color : source_color = vec3(0.58, 0.71, 0.88);

uniform vec3 ocean_deep    : source_color = vec3(0.02, 0.13, 0.33);
uniform vec3 ocean_shallow : source_color = vec3(0.08, 0.34, 0.52);
uniform vec3 land_low      : source_color = vec3(0.16, 0.42, 0.15);
uniform vec3 land_high     : source_color = vec3(0.45, 0.38, 0.22);
uniform vec3 cloud_color   : source_color = vec3(1.0, 1.0, 1.0);

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec2 v_ground;   // world-space ground coordinate at this vertex (metres)

// ── Hash / value noise / fbm in 2D ───────────────────────────────────────────
vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float gnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(dot(hash2(i + vec2(0.0, 0.0)), f - vec2(0.0, 0.0)),
	               dot(hash2(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0)), u.x),
	           mix(dot(hash2(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0)),
	               dot(hash2(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p, int octaves) {
	float v = 0.0;
	float amp = 0.5;
	float freq = 1.0;
	for (int i = 0; i < 8; i++) {
		if (i >= octaves) break;
		v += amp * gnoise(p * freq);
		freq *= 2.0;
		amp *= 0.5;
	}
	return v;
}

void vertex() {
	v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	// Patch-local X/Z plus the world ground offset → a global ground coordinate
	// that slides as the vessel moves. (UV2.x holds local X, UV2.y holds local Z.)
	v_ground = UV2 + ground_offset;
}

// Elevation field: 0 = deep ocean, 1 = high land.
float elevation(vec2 p) {
	vec2 q = p / feature_scale;
	vec2 warp = vec2(fbm(q + vec2(5.2, 1.3), 4), fbm(q + vec2(9.7, 4.1), 4));
	float base = fbm(q + 0.7 * warp, 6);
	return clamp(0.5 + 0.85 * base, 0.0, 1.0);
}

void fragment() {
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);

	float e = elevation(v_ground);
	float sea = 0.50;
	float land = smoothstep(sea - 0.004, sea + 0.006, e);   // crisp coastlines

	// Ocean shading: deep blue → lighter near coasts, with faint ripple variation.
	float shallow = smoothstep(sea - 0.10, sea, e);
	vec3 ocean = mix(ocean_deep, ocean_shallow, shallow * 0.7);
	ocean += 0.015 * fbm(v_ground / (feature_scale * 0.05), 3);

	// Land shading: green lowlands → brown highlands + biome variation.
	float highland = smoothstep(sea + 0.02, 1.0, e);
	float biome = 0.5 + 0.5 * fbm(v_ground / (feature_scale * 0.4) + 17.0, 4);
	vec3 land_col = mix(land_low, land_high, clamp(highland + 0.3 * (biome - 0.5), 0.0, 1.0));
	land_col *= 0.85 + 0.3 * biome;

	vec3 surface = mix(ocean, land_col, land);
	float ocean_mask = 1.0 - land;

	// Drifting cloud-shadow reference features (mid-grey patches over everything).
	vec2 cp = v_ground / (feature_scale * 0.6);
	float cloud_raw = 0.5 + 0.7 * fbm(cp + 0.4 * fbm(cp * 1.6, 3), 5);
	float clouds = smoothstep(0.62, 0.92, cloud_raw);

	// Simple day-ish diffuse so relief and coastlines read clearly.
	float ndl = max(dot(N, L), 0.0);
	float ambient = 0.45;
	vec3 lit = surface * (ambient + 0.8 * ndl);

	// White cloud tops with a soft shadow edge for visual reference points.
	vec3 cloud_lit = cloud_color * (ambient + 0.7 * ndl);
	lit = mix(lit, cloud_lit, clouds * 0.45);

	// Atmospheric haze: blend the surface toward the sky/haze colour with distance, so
	// terrain beyond the true horizon dissolves into a flat hazy band (no dome).
	float dist = length(v_world_pos - CAMERA_POSITION_WORLD);
	float haze = smoothstep(horizon_dist * 0.45, horizon_dist * 0.95, dist);
	lit = mix(lit, haze_color, haze);

	// Beyond the true horizon, fade the patch to TRANSPARENT so the scaled-space backdrop
	// (the real Earth limb) shows through seamlessly — no hard patch-edge horizon line.
	float edge = smoothstep(horizon_dist * 0.95, horizon_dist * 1.25, dist);

	ALBEDO   = lit;
	ROUGHNESS = mix(0.5, 0.95, ocean_mask);
	METALLIC  = 0.0;
	SPECULAR  = 0.2 + 0.4 * ocean_mask;
	ALPHA     = clamp(fade, 0.0, 1.0) * (1.0 - edge);
}
