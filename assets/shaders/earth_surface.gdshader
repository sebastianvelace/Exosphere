// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Photoreal Earth from NASA Blue Marble textures.
// Equirectangular day (natural-colour land/ocean), night (Black Marble city lights)
// and cloud textures, sampled from the sphere's local position. Day/night blend from
// `sun_dir`, warm terminator band, and a blue atmospheric Fresnel rim. Unshaded — the
// shader does its own lighting (so Godot's DirectionalLight does not double-light it).
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_back, unshaded;

uniform vec3 sun_dir = vec3(0.42, 0.78, 0.46);

uniform sampler2D day_tex   : source_color, filter_linear_mipmap;
uniform sampler2D night_tex : source_color, filter_linear_mipmap;
uniform sampler2D cloud_tex : hint_default_white, filter_linear_mipmap;

uniform vec3  atmosphere_color : source_color = vec3(0.36, 0.58, 1.0);
uniform float atmosphere_strength : hint_range(0.0, 3.0) = 0.8;
uniform float cloud_amount : hint_range(0.0, 1.0) = 0.85;
uniform float cloud_speed  = 0.0015;
uniform float night_lights : hint_range(0.0, 4.0) = 2.4;
uniform float day_gain     : hint_range(0.5, 2.0) = 1.15;
// Fades the whole distant-Earth backdrop in/out. Driven by FloatingOrigin from the CAMERA's
// altitude: ~0 in the low launch view (so the local ground patch + sky own the horizon, no
// grey seam between the two Earth representations) → 1 once the viewpoint is high enough that
// the curved planet is what you should actually be seeing.
uniform float planet_alpha : hint_range(0.0, 1.0) = 1.0;

// N3: high-frequency FBM micro-detail strength (0 = off, 0.18 = default).
uniform float detail_strength : hint_range(0.0, 0.5) = 0.18;
// N4: cloud drift animation — set from script each frame (TIME * scale).
uniform float time_offset : hint_range(0.0, 1000.0) = 0.0;

varying vec3 v_local;
varying vec3 v_world_normal;
varying vec3 v_world_pos;

void vertex() {
	v_local        = normalize(VERTEX);
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// ── FBM helpers for N3 surface detail and N4 cloud detail ────────────────────
float es_hash(vec2 p) {
	p = fract(p * vec2(127.1, 311.7));
	p += dot(p, p + 19.19);
	return fract(p.x * p.y);
}

float es_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(es_hash(i),                  es_hash(i + vec2(1.0, 0.0)), f.x),
	           mix(es_hash(i + vec2(0.0, 1.0)), es_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float es_fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * es_noise(p);
		p *= 2.1;
		a *= 0.5;
	}
	return v;
}

// Point on the unit sphere → equirectangular UV (lon = atan2(z,x), lat = asin(y)).
vec2 equirect(vec3 p) {
	float lon = atan(p.z, p.x);                 // −π … π
	float lat = asin(clamp(p.y, -1.0, 1.0));    // −π/2 … π/2
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

void fragment() {
	vec3 p = normalize(v_local);
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);
	vec3 Vv = normalize(CAMERA_POSITION_WORLD - v_world_pos);

	vec2 uv = equirect(p);

	// ── N3: Day texture with FBM micro-detail ────────────────────────────────
	// High-frequency FBM at UV * 180 gives ~5 km sub-texel variation at orbital
	// scale, breaking up the soft/pixelated look from ~150 km altitude.
	vec3 dayCol = texture(day_tex, uv).rgb;
	float detail = es_fbm(uv * 180.0) * 2.0 - 1.0;   // range -1..1
	dayCol = clamp(dayCol + detail * detail_strength, 0.0, 1.0);

	vec3 nightCol = texture(night_tex, uv).rgb;            // city lights (bright on black)

	// ── N4: Animated, volumetric-looking clouds ──────────────────────────────
	// Drift UV: combine TIME-driven speed with time_offset for script-driven drift.
	vec2 cloud_uv = uv + vec2((TIME * cloud_speed) + (time_offset * 0.00002), 0.0);
	float cloud_raw = texture(cloud_tex, cloud_uv).r;

	// FBM detail on cloud mask: breaks up hard edges and adds wispy variation.
	float cloud_detail = es_fbm(uv * 60.0 + vec2(time_offset * 0.0001, 0.0));
	float cloud = clamp(cloud_raw + (cloud_detail - 0.5) * 0.3, 0.0, 1.0);

	// Day / night from the real sun direction.
	float ndl = dot(N, L);
	float day = smoothstep(-0.12, 0.20, ndl);

	// Cloud shadow: darken ground beneath thick clouds slightly.
	float shadow = 1.0 - cloud * 0.25;
	vec3 lit_day = dayCol * shadow;

	// Cloud colour: slight blue-white tint; translucency via smoothstep so thin
	// clouds (low coverage) remain semi-transparent.
	vec3 cloud_col = vec3(0.97, 0.97, 1.0);
	float cloud_alpha = smoothstep(0.1, 0.6, cloud) * cloud_amount * day;
	vec3 surface = mix(lit_day, cloud_col, cloud_alpha);

	// Lambert-ish day lighting (no harsh hotspot, no wash-out).
	float diff = clamp(ndl, 0.0, 1.0);
	vec3 lit = surface * (0.03 + day_gain * pow(diff, 0.8));

	// City lights on the night side (emissive). Threshold on luminance so ONLY genuine
	// bright lights pass — the Black Marble texture has a dark, blocky data floor in
	// unlit regions that otherwise bleeds across the terminator as grey/blocky squares
	// when viewed from orbit. Keep cities to the truly-dark side with a sharp night mask.
	float night = smoothstep(0.10, -0.10, ndl);
	float lum    = dot(nightCol, vec3(0.299, 0.587, 0.114));
	vec3  cities = nightCol * smoothstep(0.07, 0.18, lum) * night * night_lights;
	lit += cities;

	// Warm sunrise/sunset band at the terminator.
	float term = exp(-(ndl * ndl) / (0.14 * 0.14));
	lit += vec3(1.0, 0.45, 0.20) * term * smoothstep(-0.3, 0.2, ndl) * 0.45;

	// ── Blue atmospheric limb halo (Rayleigh airglow) ───────────────────────
	// Two-lobe Fresnel: a wide soft scattering glow that fills toward the limb,
	// plus a tight bright rim right at the edge — together they read as a real
	// atmosphere shell rather than a thin outline. Brightest on the day side,
	// carrying a faint twilight band a little past the terminator.
	float ndv  = clamp(dot(N, Vv), 0.0, 1.0);
	float fresWide = pow(1.0 - ndv, 2.2);   // broad scattering body of the halo
	float fresEdge = pow(1.0 - ndv, 6.0);   // crisp bright limb line
	float rim_day  = smoothstep(-0.5, 0.35, ndl);          // fades through terminator
	float twilight = smoothstep(-0.45, -0.05, ndl) * (1.0 - smoothstep(0.0, 0.3, ndl));
	vec3 halo = atmosphere_color * atmosphere_strength
	          * ((fresWide * 0.55 + fresEdge * 1.1) * (0.10 + 0.95 * rim_day)
	             + fresWide * 0.25 * twilight);
	lit += halo;

	ALBEDO = lit;
	EMISSION = cities * 0.6;
	ALPHA = planet_alpha;
}
