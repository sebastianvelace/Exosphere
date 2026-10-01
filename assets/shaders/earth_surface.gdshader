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
render_mode cull_disabled, unshaded, fog_disabled;

#include "res://assets/shaders/surface_atmosphere.gdshaderinc"
#include "res://assets/shaders/backdrop_depth.gdshaderinc"
#include "res://assets/shaders/earth_ortho.gdshaderinc"
uniform mat3 world_to_earth_texture;
// Squared polar/equatorial radius ratio; radial direction to geodetic latitude.
uniform float geodetic_latitude_factor = 1.0;
uniform bool site_ortho_enabled = false;
uniform vec3 site_radial_up = vec3(0.0, 1.0, 0.0);
uniform vec3 site_east = vec3(1.0, 0.0, 0.0);
uniform vec3 site_north = vec3(0.0, 0.0, 1.0);

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
// Global ETOPO relief is a low-frequency visual layer for the scaled Earth. Local
// NAIP/3DEP terrain remains authoritative near launch sites; this texture never
// participates in gravity, collision or flight dynamics.
uniform sampler2D relief_tex : hint_default_black, filter_linear_mipmap;
uniform float relief_enabled : hint_range(0.0, 1.0) = 0.0;
uniform float relief_min_m = -11000.0;
uniform float relief_max_m = 9000.0;
uniform float earth_radius_m = 6371008.8;

// N3: high-frequency FBM micro-detail strength (0 = off, 0.18 = default).
uniform float detail_strength : hint_range(0.0, 0.5) = 0.18;
// N4: cloud drift animation — set from script each frame (TIME * scale).
uniform float time_offset : hint_range(0.0, 1000.0) = 0.0;


// Earth radial direction → geodetic equirectangular UV in the reflected texture frame.
vec2 equirect(vec3 p) {
	float lon = atan(p.z, p.x);                 // −π … π
	float lat = atan(p.y, length(p.xz) * geodetic_latitude_factor);
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

float relief_elevation_m(vec2 uv) {
	vec2 wrapped_uv = vec2(fract(uv.x), clamp(uv.y, 0.0, 1.0));
	float encoded = textureLod(relief_tex, wrapped_uv, 0.0).r;
	return mix(relief_min_m, relief_max_m, encoded);
}

void vertex() {
    // Projection is only a conservative coverage proxy. Fragment depth comes
    // from the physical ray/sphere intersection, never this full-screen quad.
    POSITION = vec4(VERTEX.xy, 0.5, 1.0);
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

void fragment() {
    vec4 view_near = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 1.0, 1.0);
    vec3 view_ray = normalize(view_near.xyz / view_near.w);
    vec3 world_ray = normalize((INV_VIEW_MATRIX * vec4(view_ray, 0.0)).xyz);
    float distance_m = surface_distance_m(world_ray);
    if (distance_m < 0.0) discard;
    vec3 physical_point = physical_camera_up * (physical_planet_radius_m + physical_camera_altitude_m)
        + world_ray * distance_m;
    vec3 N = normalize(physical_point);
    vec3 p = normalize(world_to_earth_texture * N);
    DEPTH = physical_backdrop_depth(view_ray * (distance_m / 2.8), PROJECTION_MATRIX);
	vec3 L = normalize(sun_dir);

	vec2 uv = equirect(p);
	if (relief_enabled > 0.5) {
		float relief_height_m = relief_elevation_m(uv);
		float land_mask = smoothstep(0.0, 250.0, relief_height_m);
		float lat = asin(clamp(p.y, -1.0, 1.0));
		float east_len = length(vec2(p.x, p.z));
		vec3 local_east = east_len > 0.00001
			? vec3(-p.z, 0.0, p.x) / east_len : vec3(1.0, 0.0, 0.0);
		vec3 local_north = normalize(cross(local_east, p));
		vec3 east = normalize(transpose(world_to_earth_texture) * local_east);
		vec3 north = normalize(transpose(world_to_earth_texture) * local_north);
		float du = 1.0 / 4096.0;
		float dv = 1.0 / 2048.0;
		float east_m = max(earth_radius_m * 2.0 * PI * max(cos(lat), 0.08) * du, 1.0);
		float north_m = max(earth_radius_m * PI * dv, 1.0);
		float east_slope = (relief_elevation_m(uv + vec2(du, 0.0))
			- relief_elevation_m(uv - vec2(du, 0.0))) / (2.0 * east_m);
		float north_slope = (relief_elevation_m(uv - vec2(0.0, dv))
			- relief_elevation_m(uv + vec2(0.0, dv))) / (2.0 * north_m);
		N = normalize(N - east * east_slope * land_mask
			- north * north_slope * land_mask);
	}

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

    if (site_ortho_enabled) {
        vec3 site_offset_m = (normalize(physical_point) - site_radial_up) * physical_planet_radius_m;
        vec2 site_m = vec2(dot(site_offset_m, site_east), dot(site_offset_m, site_north));
        dayCol = context_surface(dayCol, site_m);
        vec2 macro = macro_uv(site_m);
        vec2 regional = regional_uv(site_m);
        dayCol = mix(dayCol, texture(macro_ortho_tex, macro).rgb, macro_validity(macro));
        dayCol = mix(dayCol, texture(regional_ortho_tex, regional).rgb, regional_validity(regional));
    }

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

    vec3 V = -world_ray;
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

    // Only this camera-to-ground segment is composed over the surface. Off-limb
    // rays are discarded above and retain the existing spherical sky integral.
    ALBEDO = surface_aerial_radiance(lit, world_ray, distance_m,
        normalize(sun_dir), vertical_optical_depth, solar_visibility);
    EMISSION = vec3(0.0);
}
