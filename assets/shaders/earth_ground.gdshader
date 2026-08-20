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
// The patch owns its physically bounded direct/earthshine response. Keeping it
// unshaded avoids the scene DirectionalLight multiplying the already attenuated
// direct term and erasing the twilight/night floor on the real framebuffer.
render_mode cull_back, unshaded, depth_draw_opaque;

uniform sampler2D day_tex : source_color, filter_linear_mipmap;
uniform sampler2D night_tex : source_color, filter_linear_mipmap;
uniform vec3  sub_p       = vec3(0.0, 1.0, 0.0);  // sub-vessel point on the unit sphere (texture space)
uniform vec3  east_local  = vec3(1.0, 0.0, 0.0);  // patch +X (east)  in texture space
uniform vec3  north_local = vec3(0.0, 0.0, 1.0);  // patch +Z (north) in texture space
uniform float earth_radius = 6371000.0;           // metres
uniform float metres_per_unit = 2.8;

uniform float fade : hint_range(0.0, 1.0) = 1.0;
uniform float horizon_dist = 4000.0;
// Matches the SkyController ground-horizon blue so the patch dissolves into the sky with
// no grey seam (EarthGroundController overrides it with the live sky horizon colour).
uniform vec3  haze_color : source_color = vec3(0.40, 0.65, 1.0);
uniform vec3  sun_dir = vec3(0.42, 0.78, 0.46);
uniform float solar_visibility : hint_range(0.0, 1.0) = 1.0;
uniform vec3  vertical_optical_depth = vec3(0.0615, 0.1420, 0.2714);
uniform float detail_strength : hint_range(0.0, 0.5) = 0.18;
uniform float coastal_grade : hint_range(0.0, 1.0) = 0.46;
// The orbital map is intentionally macro-scale. These controls add bounded
// regional/terrain variation to the local patch without pretending that a
// procedural height field is measured topography.
uniform float terrain_relief_strength : hint_range(0.0, 1.0) = 0.34;
uniform float night_city_gain : hint_range(0.0, 1.0) = 0.34;
// Bounded indirect sky/earthshine floor. This is intentionally small: it keeps
// albedo readable at night without replacing the missing direct solar beam.
uniform float night_floor : hint_range(0.0, 0.12) = 0.08;
uniform vec3  night_floor_tint : source_color = vec3(0.72, 0.84, 1.0);
// Display-referred multiplier for the bounded floor. It compensates for the
// intentionally conservative pre-exposure earthshine coefficient while keeping
// the direct solar term and its visibility gate unchanged.
uniform float earthshine_gain : hint_range(1.0, 3.0) = 2.20;
// Small reflectance floor used only by the bounded earthshine path. It prevents
// dark water/terrain texels from quantizing to black during a sunless view.
uniform float earthshine_min_reflectance : hint_range(0.0, 0.10) = 0.055;
// Angular width of the local terminator transition, expressed as N·L.
uniform float terminator_width : hint_range(0.02, 0.35) = 0.16;
// Amount of view-angle haze used to hide the tangent patch's grazing-angle seam.
uniform float horizon_haze_strength : hint_range(0.0, 1.0) = 0.82;

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec3 v_world_tangent_e;
varying vec3 v_world_tangent_n;
varying vec2 v_patch;   // patch-local (east, north) offset in metres

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
float noise01(float value) {
	return clamp(0.5 + 0.5 * value, 0.0, 1.0);
}

vec2 equirect(vec3 p) {
	float lon = atan(p.z, p.x);
	float lat = asin(clamp(p.y, -1.0, 1.0));
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

void vertex() {
	v_world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_tangent_e = normalize((MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
	v_world_tangent_n = normalize((MODEL_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
	// Derive the physical patch coordinate from the authoritative mesh position.
	// UV2 is retained by the mesh for tooling, but using VERTEX here avoids driver
	// differences in secondary-UV interpolation on the duplicated SurfaceTool grid.
	v_patch        = VERTEX.xz * metres_per_unit;
}

void fragment() {
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);
	vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);

	// Reconstruct this fragment's direction on the sphere → equirect UV → real texture.
	float ae = v_patch.x / earth_radius;
	float an = v_patch.y / earth_radius;
	vec3  dir = normalize(sub_p + ae * east_local + an * north_local);
	vec2  uv  = equirect(dir);
	vec3  surface = texture(day_tex, uv).rgb;
	vec3  night_map = texture(night_tex, uv).rgb;

	// Low-altitude launch views should read as coastal fill, marsh and sand,
	// not as saturated satellite green. Keep the Blue Marble macro-shapes, but
	// grade land toward muted Starbase/Cape-like tones close to the pad.
	float luma = dot(surface, vec3(0.299, 0.587, 0.114));
	float water_hint = smoothstep(0.18, 0.34, surface.b - max(surface.r, surface.g) * 0.62);
	vec3 muted = mix(vec3(luma), surface, 0.52);
	vec3 coastal_land = mix(vec3(0.34, 0.31, 0.24), vec3(0.24, 0.28, 0.20), smoothstep(0.34, 0.62, luma));
	vec3 coastal_water = vec3(0.05, 0.15, 0.19);
	vec3 coastal = mix(coastal_land, coastal_water, water_hint);
	surface = mix(muted, coastal, coastal_grade);
	surface *= 0.82;

	// The 8K orbital map is excellent for continents, but at launch altitude one
	// texel can cover tens of kilometres. Add four restrained spatial scales so
	// the pad has believable coast, scrub, soil and terrain breakup instead of a
	// stretched smooth colour field. This is a calibrated visual reconstruction,
	// not a claim of measured Cape Canaveral topography.
	float macro = noise01(gnoise(v_patch * 0.000012 + vec2(17.0, 43.0)));
	float regional = noise01(gnoise(v_patch * 0.000075 + vec2(-29.0, 11.0)));
	float terrain = noise01(gnoise(v_patch * 0.00042 + vec2(7.0, -19.0)));
	float fine = noise01(gnoise(v_patch * 0.0032 + vec2(3.0, 5.0)));
	// At 20–100 m camera altitude the visible footprint is only tens of metres;
	// kilometre-scale noise is invisible there. These two final bands represent
	// scrub/soil breakup and compact launch-site grain at the correct screen scale.
	float local_scrub = noise01(gnoise(v_patch * 0.035 + vec2(-13.0, 29.0)));
	float ground_grain = noise01(gnoise(v_patch * 0.16 + vec2(47.0, -37.0)));
	float land_mask = 1.0 - water_hint;
	float macro_grade = mix(macro, regional, 0.68);
	float soil_breakup = (terrain - 0.5) * 0.16 + (fine - 0.5) * 0.075
		+ (local_scrub - 0.5) * 0.14 + (ground_grain - 0.5) * 0.06;
	vec3 soil_tint = vec3(1.00, 0.86, 0.66);
	surface *= 0.62 + 0.80 * macro_grade;
	surface += land_mask * soil_breakup * soil_tint;
	float det = gnoise(v_patch * 0.0006) + 0.5 * gnoise(v_patch * 0.0028);
	surface *= 1.0 + detail_strength * det;

	// A small normal perturbation makes the multi-scale breakup respond to the
	// sun direction. It is deliberately low amplitude: this gives texture relief
	// without turning the tangent patch into a noisy tessellation substitute.
	float relief = gnoise(v_patch * 0.045 + vec2(61.0, -13.0));
	float relief_e = gnoise((v_patch + vec2(2.0, 0.0)) * 0.045 + vec2(61.0, -13.0));
	float relief_n = gnoise((v_patch + vec2(0.0, 2.0)) * 0.045 + vec2(61.0, -13.0));
	float relief_scale = terrain_relief_strength * land_mask;
	N = normalize(N
		+ v_world_tangent_e * (relief - relief_e) * relief_scale
		+ v_world_tangent_n * (relief - relief_n) * relief_scale);

	// Spectrally attenuated direct sunlight. The small diffuse floor represents skylight;
	// unlike the former 0.55 term it does not leave the coast brightly lit at midnight.
	float sun_height = dot(N, L);
	float ndl = max(sun_height, 0.0);
	float air_mass = 1.0 / max(ndl + 0.025 * exp(-11.0 * ndl), 0.025);
	vec3 direct_transmittance = exp(-vertical_optical_depth * min(air_mass, 40.0));

	// The floor represents weak sky/earthshine, not direct sunlight, so it remains
	// bounded and is deliberately not multiplied by solar_visibility. The direct
	// term below remains fully gated by solar_visibility for eclipses/occlusion.
	float terminator = smoothstep(-terminator_width, terminator_width, sun_height);
	float twilight_fill = mix(1.0, 1.45, terminator);
	vec3 indirect_floor = night_floor_tint * night_floor * twilight_fill;
	vec3 direct = vec3(1.05 * ndl * solar_visibility) * direct_transmittance;
	// ALBEDO is multiplied by Godot's scene lights. At sunrise (solar_visibility=0)
	// and on the night side that multiplication can erase the bounded floor entirely.
	// Keep the floor as a low-energy emission: it represents sky/earthshine and remains
	// visible without inventing direct sunlight. The direct term stays in ALBEDO and is
	// still fully controlled by solar_visibility.
	vec3 lit = surface * direct;
	vec3 earthshine_surface = max(surface, night_floor_tint * earthshine_min_reflectance);
	vec3 indirect_emission = earthshine_surface * indirect_floor * earthshine_gain;
	float city_luma = dot(night_map, vec3(0.299, 0.587, 0.114));
	float city_mask = smoothstep(0.018, 0.12, city_luma);
	float night_side = 1.0 - smoothstep(-0.16, 0.18, sun_height);
	vec3 city_lights = night_map * city_mask * night_side * night_city_gain;
	vec3 ground_radiance = lit + indirect_emission + city_lights;

	// Atmospheric perspective: distant ground fades toward the SKY-horizon colour and then
	// dissolves to transparent, so it blends seamlessly into the sky/backdrop with no grey
	// band. The colour blend leads the alpha fade so the seam is never a hard line.
	float dist = length(v_world_pos - CAMERA_POSITION_WORLD);
	float distance_haze = smoothstep(horizon_dist * 0.60, horizon_dist * 1.05, dist);
	// At the true grazing angle the local tangent patch and the atmospheric
	// backdrop meet at a single high-contrast line. Fade its colour toward the
	// same live sky horizon even when the fragment is inside horizon_dist.
	float view_up = max(dot(N, V), 0.0);
	float grazing_haze = 1.0 - smoothstep(0.015, 0.16, view_up);
	float haze = max(distance_haze, grazing_haze * horizon_haze_strength);
	ground_radiance = mix(ground_radiance, haze_color, haze);
	float edge = smoothstep(horizon_dist * 0.90, horizon_dist * 1.12, dist);

	// Unshaded materials do not receive the scene light a second time. Add the
	// bounded earthshine to the manually computed radiance so it survives the
	// zero-direct-light sunrise/night/eclipsed states.
	ALBEDO    = ground_radiance;
	EMISSION  = vec3(0.0);
	ROUGHNESS = 0.85;
	METALLIC  = 0.0;
	ALPHA     = clamp(fade, 0.0, 1.0) * (1.0 - edge);
}
