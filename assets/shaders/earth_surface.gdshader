// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Photorealistic Earth surface shader.
// Apply to a planet SphereMesh. Fully procedural and SCALE-INDEPENDENT: every
// detail is sampled from the *normalized* local position / normal (a point on
// the unit sphere), so the mesh radius can change freely.
//
//   • Procedural continents (fbm) — green→brown land, deep-blue oceans.
//   • White polar ice caps from latitude (local Y).
//   • Drifting fbm cloud layer (additive, animated via TIME).
//   • Day/night terminator from `sun_dir`, with faint warm city lights.
//   • Blue Fresnel atmospheric rim that glows at the limb.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
// Unshaded: the shader computes the full day/night lit colour itself (below). Without
// this, Godot's DirectionalLight would relight the already-lit ALBEDO and blow the day
// side out to white.
render_mode cull_back, unshaded;

// Direction toward the sun (world space). Default = nice 3/4 lighting.
uniform vec3 sun_dir = vec3(0.4, 0.5, 0.8);

// Palette ---------------------------------------------------------------------
uniform vec3  ocean_color      : source_color = vec3(0.03, 0.18, 0.42);  // mid ocean blue
uniform vec3  ocean_shallow    : source_color = vec3(0.07, 0.38, 0.58);
uniform vec3  land_low         : source_color = vec3(0.12, 0.40, 0.13);  // lush green
uniform vec3  land_high        : source_color = vec3(0.46, 0.37, 0.22);  // brown/desert
uniform vec3  ice_color        : source_color = vec3(0.93, 0.96, 1.00);
uniform vec3  cloud_color      : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3  atmosphere_color : source_color = vec3(0.30, 0.55, 1.0);
uniform vec3  city_light_color : source_color = vec3(1.0, 0.78, 0.42);

// Tuning ----------------------------------------------------------------------
uniform float sea_level     : hint_range(0.0, 1.0)  = 0.52; // elevation threshold for land
uniform float continent_scale : hint_range(0.5, 6.0) = 2.2; // larger = more landmasses
uniform float cloud_scale   : hint_range(1.0, 8.0)  = 3.0;
uniform float cloud_coverage: hint_range(0.0, 1.0)  = 0.60; // higher = fewer clouds
uniform float cloud_speed                            = 0.006;
uniform float ice_latitude  : hint_range(0.5, 1.0)  = 0.90; // |y| above which ice appears (small caps)
uniform float ocean_specular: hint_range(0.0, 1.0)  = 0.12;
uniform float atmosphere_strength : hint_range(0.0, 3.0) = 0.6;
uniform float night_lights  : hint_range(0.0, 2.0)  = 1.0;

varying vec3 v_local;        // normalized local-space position (point on unit sphere)
varying vec3 v_world_normal;
varying vec3 v_world_pos;

// ── Hash / noise / fbm on the unit sphere ────────────────────────────────────
vec3 hash3(vec3 p) {
	p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
	         dot(p, vec3(269.5, 183.3, 246.1)),
	         dot(p, vec3(113.5, 271.9, 124.6)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

// Gradient (Perlin-style) 3D noise — smooth, tileable enough for sphere use.
float gnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(dot(hash3(i + vec3(0,0,0)), f - vec3(0,0,0)),
		        dot(hash3(i + vec3(1,0,0)), f - vec3(1,0,0)), u.x),
		    mix(dot(hash3(i + vec3(0,1,0)), f - vec3(0,1,0)),
		        dot(hash3(i + vec3(1,1,0)), f - vec3(1,1,0)), u.x), u.y),
		mix(mix(dot(hash3(i + vec3(0,0,1)), f - vec3(0,0,1)),
		        dot(hash3(i + vec3(1,0,1)), f - vec3(1,0,1)), u.x),
		    mix(dot(hash3(i + vec3(0,1,1)), f - vec3(0,1,1)),
		        dot(hash3(i + vec3(1,1,1)), f - vec3(1,1,1)), u.x), u.y),
		u.z);
}

float fbm(vec3 p, int octaves) {
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

// Ridged fbm — gives sharper coastlines / mountain ridges than plain fbm.
float ridged(vec3 p, int octaves) {
	float v = 0.0;
	float amp = 0.5;
	float freq = 1.0;
	for (int i = 0; i < 8; i++) {
		if (i >= octaves) break;
		float n = 1.0 - abs(gnoise(p * freq));
		v += amp * n * n;
		freq *= 2.0;
		amp *= 0.5;
	}
	return v;
}

void vertex() {
	v_local = normalize(VERTEX);
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Elevation field on the unit sphere: 0 = deep ocean, 1 = high land.
float elevation(vec3 p) {
	vec3 q = p * continent_scale;
	// Domain-warp for organic, non-blobby coastlines.
	vec3 warp = vec3(fbm(q + vec3(11.3, 4.7, 1.1), 4),
	                 fbm(q + vec3(2.1, 9.6, 5.4), 4),
	                 fbm(q + vec3(7.2, 1.9, 8.8), 4));
	float base = fbm(q + 0.6 * warp, 6);          // large continents
	float detail = ridged(q * 2.7, 5);            // ridges / mountains
	float e = 0.5 + 0.55 * base;
	e += 0.12 * (detail - 0.5);
	return clamp(e, 0.0, 1.0);
}

void fragment() {
	vec3 p = normalize(v_local);
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);
	vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);

	// ── Continents vs ocean ─────────────────────────────────────────────────
	float e = elevation(p);
	float land = smoothstep(sea_level - 0.02, sea_level + 0.03, e);

	// Ocean color: deep blue, slightly lighter near coasts / shallows.
	float shallow = smoothstep(sea_level - 0.10, sea_level, e);
	vec3 ocean = mix(ocean_color, ocean_shallow, shallow * 0.6);
	ocean += 0.02 * fbm(p * 8.0, 3);

	// Land color: green lowlands → brown highlands, modulated by a second noise
	// for biome variation (deserts, forests).
	float highland = smoothstep(sea_level + 0.02, 1.0, e);
	float biome = 0.5 + 0.5 * fbm(p * 4.3 + 21.0, 4);
	vec3 land_col = mix(land_low, land_high, clamp(highland + 0.35 * (biome - 0.5), 0.0, 1.0));
	// Touch of darker vegetation variation.
	land_col *= 0.85 + 0.3 * biome;

	vec3 surface = mix(ocean, land_col, land);

	// ── Polar ice caps (latitude = |local y|) ───────────────────────────────
	float lat = abs(p.y);
	float ice_noise = 0.04 * fbm(p * 6.0, 3);
	float ice = smoothstep(ice_latitude - 0.08, ice_latitude + 0.05, lat + ice_noise);
	// A little frost creeps onto high land at moderate latitudes.
	ice = max(ice, smoothstep(ice_latitude - 0.22, ice_latitude, lat) * highland * 0.7);
	surface = mix(surface, ice_color, clamp(ice, 0.0, 1.0));

	// Is this pixel ocean (for specular)? 1 = ocean, 0 = land/ice.
	float ocean_mask = (1.0 - land) * (1.0 - clamp(ice, 0.0, 1.0));

	// ── Clouds (animated drifting fbm) ──────────────────────────────────────
	vec3 cp = p * cloud_scale;
	float drift = TIME * cloud_speed;
	cp += vec3(drift, 0.0, drift * 0.6);
	float cloud_raw = fbm(cp + 0.5 * fbm(cp * 1.7 + drift, 3), 6);
	cloud_raw = 0.5 + 0.7 * cloud_raw;
	float clouds = smoothstep(cloud_coverage, cloud_coverage + 0.35, cloud_raw);

	// ── Day / night lighting ────────────────────────────────────────────────
	float ndl = dot(N, L);
	float day = smoothstep(-0.15, 0.25, ndl);          // terminator softness
	float ambient = 0.04;

	// Diffuse-lit surface.
	vec3 lit = surface * (ambient + day * 1.15);

	// Ocean specular (sun glint) — only on water, only on the day side.
	vec3 H = normalize(L + V);
	float spec = pow(max(dot(N, H), 0.0), 300.0);
	lit += vec3(1.0, 0.97, 0.85) * spec * ocean_specular * ocean_mask * day;

	// City lights on the night side over land (not ocean / ice).
	float night = 1.0 - day;
	float city_field = fbm(p * 28.0, 4);
	float cities = smoothstep(0.62, 0.78, city_field) * land * (1.0 - clamp(ice, 0.0, 1.0));
	lit += city_light_color * cities * night * night_lights * 0.9;

	// ── Clouds over the lit surface (white, lit by day, additive-ish) ───────
	vec3 cloud_lit = cloud_color * (ambient + day * 1.0);
	lit = mix(lit, cloud_lit, clouds * 0.5);

	// ── Blue Fresnel atmospheric rim ────────────────────────────────────────
	float fres = pow(1.0 - clamp(dot(N, V), 0.0, 1.0), 6.0);
	// Rim is brightest where it is also day-lit; keep a faint dusk band.
	float rim_day = smoothstep(-0.35, 0.4, ndl);
	vec3 rim = atmosphere_color * fres * atmosphere_strength * (0.2 + 0.9 * rim_day);
	lit += rim;

	ALBEDO = lit;
	// Drive emission for night lights + rim so they glow without external light.
	EMISSION = city_light_color * cities * night * night_lights * 0.6 + rim * 0.2;
	ROUGHNESS = mix(0.55, 0.95, 1.0 - ocean_mask);   // ocean smoother than land
	METALLIC = 0.0;
	SPECULAR = 0.2 + 0.5 * ocean_mask;
}
