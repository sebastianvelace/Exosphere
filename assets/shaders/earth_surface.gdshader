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

varying vec3 v_local;
varying vec3 v_world_normal;
varying vec3 v_world_pos;

void vertex() {
	v_local        = normalize(VERTEX);
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
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

	vec3 dayCol   = texture(day_tex, uv).rgb;
	vec3 nightCol = texture(night_tex, uv).rgb;            // city lights (bright on black)
	float cloud   = texture(cloud_tex, uv + vec2(TIME * cloud_speed, 0.0)).r;

	// Day / night from the real sun direction.
	float ndl = dot(N, L);
	float day = smoothstep(-0.12, 0.20, ndl);

	// White clouds over the day side.
	vec3 surface = mix(dayCol, vec3(1.0), cloud * cloud_amount * day * 0.85);

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

	// Blue atmospheric Fresnel rim — thin glow at the limb, brightest on the day side.
	float fres = pow(1.0 - clamp(dot(N, Vv), 0.0, 1.0), 4.0);
	float rim_day = smoothstep(-0.35, 0.4, ndl);
	lit += atmosphere_color * fres * atmosphere_strength * (0.18 + 0.9 * rim_day);

	ALBEDO = lit;
	EMISSION = cities * 0.6;
	ALPHA = planet_alpha;
}
