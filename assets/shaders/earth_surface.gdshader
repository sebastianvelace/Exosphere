// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Photoreal Earth from NASA Blue Marble textures.
// Equirectangular day (natural-colour land/ocean), night (Black Marble city lights)
// and cloud textures, sampled from the sphere's local position. Day/night blend from
// `sun_dir` and spectrally attenuated direct sunlight. The spherical sky integrator
// owns free-sky radiance; the surface applies the bounded camera-to-ground optical
// path needed when the scaled Earth disc itself occludes that atmosphere.
// Unshaded — the
// shader does its own lighting (so Godot's DirectionalLight does not double-light it).
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_back, unshaded, blend_mix;

uniform vec3 sun_dir = vec3(0.42, 0.78, 0.46);
// Limb-darkened solar-disc visibility supplied by SunController.  This keeps the
// distant Earth surface consistent with the atmospheric eclipse radiance instead of
// leaving a full-strength unshaded day map during totality.
uniform float solar_visibility : hint_range(0.0, 1.0) = 1.0;

uniform sampler2D day_tex   : source_color, filter_linear_mipmap;
uniform sampler2D night_tex : source_color, filter_linear_mipmap;
uniform sampler2D cloud_tex : hint_default_white, filter_linear_mipmap;

uniform vec3 vertical_optical_depth = vec3(0.0615, 0.1420, 0.2714);
uniform float cloud_amount : hint_range(0.0, 1.0) = 0.72;
// ≈15 m/s zonal wind at Earth's equator: v/(2πR) in texture revolutions/s.
uniform float cloud_speed  = 0.000000375;
uniform float night_lights : hint_range(0.0, 4.0) = 2.4;
uniform float day_gain     : hint_range(0.5, 2.0) = 1.15;
// Bounded earthshine for the scaled-space night hemisphere. The actual city-light
// texture remains additive; this floor only keeps land/ocean readable when the
// physically direct beam is absent and exposure is driven by the star field.
uniform float night_floor : hint_range(0.0, 0.16) = 0.12;
// Bounded ocean response to the blue atmospheric sky. The term is derived from
// the same vertical Rayleigh optical depth used by the atmosphere integrator,
// rather than from an arbitrary global brightness multiplier.
uniform float ocean_sky_fill_strength : hint_range(0.0, 0.40) = 0.16;
// Fades the whole distant-Earth backdrop in/out. Driven by FloatingOrigin from the CAMERA's
// altitude: ~0 in the low launch view (so the local ground patch + sky own the horizon, no
// grey seam between the two Earth representations) → 1 once the viewpoint is high enough that
// the curved planet is what you should actually be seeing. The handoff is complementary with
// the ground patch (40–75 km camera altitude).
uniform float planet_alpha : hint_range(0.0, 1.0) = 1.0;
// Presentation-only Earth shell. The shell is a separate, slightly larger mesh;
// keeping this branch in the same shader reuses the measured RGB optical depth and
// the live solar visibility without changing the opaque surface radiance.
uniform float atmosphere_shell : hint_range(0.0, 1.0) = 0.0;

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

	vec2 uv = equirect(p);

	// ── N3: Day texture with FBM micro-detail ────────────────────────────────
	// High-frequency FBM at UV * 180 gives ~5 km sub-texel variation at orbital
	// scale, breaking up the soft/pixelated look from ~150 km altitude.
	vec3 dayCol = texture(day_tex, uv).rgb;
	float detail = es_fbm(uv * 180.0) * 2.0 - 1.0;   // range -1..1
	dayCol = clamp(dayCol + detail * detail_strength, 0.0, 1.0);
	// Oceans from orbit are darker and more blue than the raw Blue Marble average;
	// this restores disc contrast without touching day_gain (A/B rejected lowering it).
	float water = smoothstep(0.04, 0.16, dayCol.b - max(dayCol.r, dayCol.g) * 0.70);
	dayCol = mix(dayCol, dayCol * vec3(0.70, 0.84, 1.08), water * 0.55);

	vec3 nightCol = texture(night_tex, uv).rgb;            // city lights (bright on black)

	// ── N4: Animated, volumetric-looking clouds ──────────────────────────────
	// Drift UV: combine TIME-driven speed with time_offset for script-driven drift.
	vec2 cloud_uv = uv + vec2((TIME * cloud_speed) + (time_offset * 0.00002), 0.0);
	float cloud_raw = texture(cloud_tex, cloud_uv).r;

	// FBM detail on cloud mask: breaks up hard edges and adds wispy variation.
	float cloud_detail = es_fbm(uv * 60.0 + vec2(time_offset * 0.0001, 0.0));
	// The source map is intentionally broad and smooth. Shape its coverage
	// before adding detail so low-orbit views read as a continuous cloud deck
	// instead of isolated opaque blobs with image-like edges.
	float cloud_signal = cloud_raw + (cloud_detail - 0.5) * 0.22;
	float cloud = smoothstep(0.18, 0.78, clamp(cloud_signal, 0.0, 1.0));

	// Day / night from the real sun direction.
	float ndl = dot(N, L);
	float day = smoothstep(-0.12, 0.20, ndl);

	// Cloud shadow: darken ground beneath thick clouds slightly.
	float shadow = 1.0 - cloud * 0.16;
	vec3 lit_day = dayCol * shadow;

	// Cloud colour: slight blue-white tint; translucency via smoothstep so thin
	// clouds (low coverage) remain semi-transparent.
	vec3 cloud_col = vec3(0.97, 0.97, 1.0);
	float cloud_alpha = smoothstep(0.24, 0.82, cloud) * cloud_amount * day;
	vec3 surface = mix(lit_day, cloud_col, cloud_alpha);

	// Direct solar beam attenuated through the same RGB vertical optical depth as the sky.
	// Kasten-style curvature guard keeps air mass finite at the terminator without inventing
	// an orange emissive strip. Twilight itself comes from the volumetric atmosphere.
	float diff = clamp(ndl, 0.0, 1.0);
	float air_mass = 1.0 / max(diff + 0.025 * exp(-11.0 * diff), 0.025);
	vec3 direct_transmittance = exp(-vertical_optical_depth * min(air_mass, 40.0));
	float diffuse_fill = mix(night_floor, 0.045, smoothstep(-0.18, 0.30, ndl));
	vec3 lit = surface * (vec3(diffuse_fill)
		+ day_gain * pow(diff, 0.8) * direct_transmittance * solar_visibility);
	// The distant ocean is not a black Lambertian tile: it reflects a bounded
	// blue atmospheric dome. Use optical depth for the sky colour and keep this
	// term view-independent so it cannot paint a camera-centred contour.
	vec3 rayleigh_sky = vec3(1.0) - exp(-vertical_optical_depth * 5.0);
	vec3 ocean_sky = rayleigh_sky * ocean_sky_fill_strength
		* water * day * solar_visibility;
	lit += ocean_sky;

	// City lights on the night side (emissive). Threshold on luminance so ONLY genuine
	// bright lights pass — the Black Marble texture has a dark, blocky data floor in
	// unlit regions that otherwise bleeds across the terminator as grey/blocky squares
	// when viewed from orbit. Keep cities to the truly-dark side with a sharp night mask.
	float night = smoothstep(0.10, -0.10, ndl);
	float lum    = dot(nightCol, vec3(0.299, 0.587, 0.114));
	vec3  cities = nightCol * smoothstep(0.07, 0.18, lum) * night * night_lights;
	lit += cities;

	bool shell_render = atmosphere_shell > 0.5;
	vec3 shell_radiance = vec3(0.0);
	float shell_alpha = 0.0;
	if (shell_render) {
		vec3 shell_view = normalize(CAMERA_POSITION_WORLD - v_world_pos);
		float shell_view_cosine = clamp(dot(N, shell_view), 0.0, 1.0);
		float shell_air_mass = 1.0 / max(
			shell_view_cosine + 0.025 * exp(-11.0 * shell_view_cosine), 0.025);
		vec3 shell_transmittance = exp(-vertical_optical_depth
			* min(shell_air_mass, 40.0));
		float shell_limb = smoothstep(0.02, 0.82,
			clamp(1.0 - shell_view_cosine, 0.0, 1.0));
		float shell_day = mix(0.12, 1.0,
			smoothstep(-0.18, 0.30, dot(N, L)) * solar_visibility);
		shell_radiance = rayleigh_sky * shell_day
			* (vec3(1.0) - shell_transmittance);
		float shell_luminance = dot(shell_radiance,
			vec3(0.2126, 0.7152, 0.0722));
		shell_alpha = planet_alpha * shell_limb
			* clamp(shell_luminance * 2.8, 0.0, 0.72);
	}

	vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);
	float ndotv = dot(N, V);

	// Air-to-water Fresnel reflectance: n_air≈1.000 and n_water≈1.333 give
	// F0=(1.000-1.333)^2/(1.000+1.333)^2≈0.0204. At scaled-Earth range the
	// ocean is a broad statistical surface, so keep the solar response bounded
	// to a low-frequency lobe instead of pretending to resolve individual waves.
	float view_cosine = clamp(ndotv, 0.0, 1.0);
	float water_fresnel = 0.0204 + 0.9796 * pow(1.0 - view_cosine, 5.0);
	float half_length = length(L + V);
	vec3 reflection_half = half_length > 0.0001
		? (L + V) / half_length : N;
	float sun_glint = pow(max(dot(N, reflection_half), 0.0), 64.0);
	vec3 ocean_sun_reflection = vec3(water_fresnel * sun_glint)
		* direct_transmittance * water * day * solar_visibility;
	lit += ocean_sun_reflection;

	// The scaled globe is opaque in front of the sky cubemap, so the atmosphere
	// cannot paint its own limb over the surface. Apply the same bounded optical
	// depth as a camera-to-surface path: surface radiance is attenuated and the
	// missing energy is replaced by blue Rayleigh in-scatter. The blend is only
	// active near the geometric limb; the disc centre keeps its measured albedo.
	float view_air_mass = 1.0 / max(
		view_cosine + 0.025 * exp(-11.0 * view_cosine), 0.025);
	vec3 view_transmittance = exp(-vertical_optical_depth
		* min(view_air_mass, 40.0));
	float limb_illumination = mix(0.20, 1.0, day * solar_visibility);
	vec3 aerial_radiance = lit * view_transmittance
		+ rayleigh_sky * limb_illumination * (vec3(1.0) - view_transmittance);
	float limb_scatter = smoothstep(0.0, 0.42,
		clamp(1.0 - ndotv, 0.0, 1.0));
	lit = mix(lit, aerial_radiance, limb_scatter);

	if (shell_render) {
		ALBEDO = shell_radiance;
		EMISSION = vec3(0.0);
		ALPHA = clamp(shell_alpha, 0.0, 0.72);
	} else {
		ALBEDO = lit;
		EMISSION = cities * 0.6;
		// Coverage only: the geometric silhouette is N dot V = 0, not an interior
		// view-angle contour. Use the unclamped cosine for derivatives so the
		// footprint remains valid across the tangent. The epsilon only guards zero
		// derivatives; no fixed angular band should expose sky through the disc.
		float limb = 1.0 - ndotv;
		float limb_aa = max(fwidth(limb), 0.000001);
		float silhouette = smoothstep(0.0, limb_aa, ndotv);
		ALPHA = planet_alpha * silhouette;
	}
}
