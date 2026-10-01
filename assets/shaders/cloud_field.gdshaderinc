// Shared visual cloud field for sky and opaque Earth rays. SI metre coordinates;
// geography and advection use the same full basis and simulation-clock offset.
uniform sampler2D cloud_coverage_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform bool cloud_enabled = false;
uniform float cloud_base_altitude = 1200.0;
uniform float cloud_top_altitude = 12000.0;
uniform float cloud_extinction = 0.00011;
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.58;
uniform float cloud_longitude_offset = 0.0;
uniform mat3 cloud_world_to_texture;
// The weather texture provides the large-scale geographic mask. This bounded
// 3D field adds billows through the shell without requiring a
// second volume texture. Scales are in inverse world metres; keeping them
// explicit makes the defaults stable for Earth and similarly-sized bodies.
uniform float cloud_macro_scale = 0.000075;
uniform float cloud_macro_strength = 0.25;
// Low-sun rays magnify latitude rows in the equirectangular weather map.  The
// controller enables this narrow prefilter only around twilight/night; daylight
// retains the full-resolution weather texture and its synoptic detail.
uniform float cloud_weather_prefilter : hint_range(0.0, 1.0) = 0.0;
uniform bool cloud_local_weather_enabled = false;
uniform vec3 cloud_local_center = vec3(0.0, 1.0, 0.0);
uniform float cloud_local_density_multiplier = 1.0;
uniform float cloud_local_radius_m = 160000.0;
uniform float cloud_local_scale_m = 2800.0;
uniform float cloud_local_base_m = 1200.0;
uniform float cloud_local_top_m = 3600.0;
uniform float cloud_planet_radius_m = 6371000.0;
uniform float cloud_observer_altitude_m = 0.0;

vec2 cloud_equirect(vec3 p) {
    return vec2(atan(p.z, p.x) / (2.0 * PI) + 0.5,
        0.5 - asin(clamp(p.y, -1.0, 1.0)) / PI);
}

float cloud_height_shape(float height) {
	float u = (height - cloud_base_altitude)
		/ max(cloud_top_altitude - cloud_base_altitude, 1.0);
	return smoothstep(0.0, 0.12, u) * (1.0 - smoothstep(0.72, 1.0, u));
}

// A small, deterministic 3D value-noise implementation. The hash uses only
// arithmetic and fract (rather than a texture or a trigonometric hash), which
// keeps the extra cloud samples affordable in the view and sun integrals.
float cloud_hash3(vec3 cell) {
	vec3 p = fract(cell * 0.1031);
	p += dot(p, p.yzx + 33.33);
	return fract((p.x + p.y) * p.z);
}

float cloud_value_noise3(vec3 position) {
	vec3 cell = floor(position);
	vec3 fraction = fract(position);
	// Interpolate all eight corners. The former diagonal-only blend disagreed
	// on either side of a cell face, creating block seams in the cloud density
	// and its reciprocal sun-shadow integral.
	vec3 w = fraction * fraction * (3.0 - 2.0 * fraction);
	float x00 = mix(cloud_hash3(cell), cloud_hash3(cell + vec3(1.0, 0.0, 0.0)), w.x);
	float x10 = mix(cloud_hash3(cell + vec3(0.0, 1.0, 0.0)),
		cloud_hash3(cell + vec3(1.0, 1.0, 0.0)), w.x);
	float x01 = mix(cloud_hash3(cell + vec3(0.0, 0.0, 1.0)),
		cloud_hash3(cell + vec3(1.0, 0.0, 1.0)), w.x);
	float x11 = mix(cloud_hash3(cell + vec3(0.0, 1.0, 1.0)),
		cloud_hash3(cell + vec3(1.0)), w.x);
	return mix(mix(x00, x10, w.y), mix(x01, x11, w.y), w.z);
}

// The global weather map is an 8K equirectangular image.  Near the horizon a
// single texel row can be magnified over many view samples, exposing JPEG
// latitude bands as unstable stripes.  A narrow latitude-aware prefilter keeps
// the synoptic pattern while removing that projection aliasing.
float cloud_weather_spherical_sample(vec3 direction) {
	float prefilter = clamp(cloud_weather_prefilter, 0.0, 1.0);
	vec3 tangent = cross(direction, vec3(0.0, 1.0, 0.0));
	if (dot(tangent, tangent) < 1e-5)
		tangent = cross(direction, vec3(1.0, 0.0, 0.0));
	tangent = normalize(tangent);
	vec3 bitangent = normalize(cross(direction, tangent));
	vec2 center_uv = cloud_equirect(direction)
		+ vec2(cloud_longitude_offset, 0.0);
	float center = texture(cloud_coverage_tex, center_uv).r;
	// Preserve the same four textureLod reads, but widen the meridional
	// footprint slightly more than the horizontal one. This is a cheap
	// anisotropic blend targeted at the latitude-row artifact, not a new
	// scattering or cloud-resolution path. The isotropic reference form is
	// normalize(direction + tangent * footprint), split here into bounded
	// tangential and meridional radii.
	float tangential_footprint = 0.035 * prefilter
		* 0.78;
	float meridional_footprint = 0.035 * prefilter
		* 1.30;
	float tangent_a = textureLod(cloud_coverage_tex,
		cloud_equirect(normalize(direction + tangent * tangential_footprint))
			+ vec2(cloud_longitude_offset, 0.0), 6.0).r;
	float tangent_b = textureLod(cloud_coverage_tex,
		cloud_equirect(normalize(direction - tangent * tangential_footprint))
			+ vec2(cloud_longitude_offset, 0.0), 6.0).r;
	float bitangent_a = textureLod(cloud_coverage_tex,
		cloud_equirect(normalize(direction + bitangent * meridional_footprint))
			+ vec2(cloud_longitude_offset, 0.0), 6.0).r;
	float bitangent_b = textureLod(cloud_coverage_tex,
		cloud_equirect(normalize(direction - bitangent * meridional_footprint))
			+ vec2(cloud_longitude_offset, 0.0), 6.0).r;
	// At 10 km the tangent ray magnifies one weather-map latitude row across
	// thousands of pixels. Prefer the broad mip footprint here; retaining only a
	// small centre contribution prevents a horizontal stippled cloud stripe.
	float prefiltered = center * 0.08
		+ (tangent_a + tangent_b + bitangent_a + bitangent_b) * 0.23;
	return mix(center, prefiltered, prefilter);
}

// The erosion/detail lookup is repeated at 3x frequency. It needs the same
// horizon-aware filtering as the weather mask; otherwise a prefiltered macro
// field can still expose the source texture's latitude rows at 10 km.
float cloud_detail_sample(vec2 uv) {
	float detail_prefilter = clamp(cloud_weather_prefilter, 0.0, 1.0);
	// The macro weather lookup owns the latitude-row prefilter. Keep the
	// repeated erosion lookup aligned with the same spherical direction; its
	// two existing reads are blended into the coarse mip below.
	vec2 detail_uv = uv * 3.07 + vec2(0.17, 0.31);
	float detail = texture(cloud_coverage_tex, detail_uv).r;
	float coarse = textureLod(cloud_coverage_tex, detail_uv, 6.0).r;
	return mix(detail, coarse, detail_prefilter);
}

bool cloud_in_local_core(vec3 position) {
    vec3 direction = normalize(cloud_world_to_texture * normalize(position));
    return cloud_local_weather_enabled && length(direction - cloud_local_center)
        * cloud_planet_radius_m < cloud_local_radius_m * 0.70;
}

float cloud_density(vec3 position) {
    float height = length(position) - cloud_planet_radius_m;
    float shape = cloud_height_shape(height);
    if (shape <= 0.0) return 0.0;
    vec3 texture_direction = normalize(cloud_world_to_texture * normalize(position));
    vec3 noise_position = cloud_world_to_texture * position;
    vec3 wind_offset = vec3(cloud_longitude_offset * 64.0,
        cloud_longitude_offset * 17.0, cloud_longitude_offset * 31.0);
    float local_weight = 0.0;
    if (cloud_local_weather_enabled) {
        float distance_m = length(texture_direction - cloud_local_center) * cloud_planet_radius_m;
        local_weight = 1.0 - smoothstep(cloud_local_radius_m * 0.70, cloud_local_radius_m, distance_m);
    }
    float local_density = 0.0;
    float billow = cloud_value_noise3(noise_position * 0.00065 + wind_offset);
    if (local_weight > 0.0) {
        // The horizon prefilter must not soften local holes into grey fog.
        vec3 cells_position = texture_direction * cloud_planet_radius_m
            / max(cloud_local_scale_m, 1.0) + vec3(89.0, 13.0, 41.0) + wind_offset;
        float cells = cloud_value_noise3(cells_position);
        float local_u = (height - cloud_local_base_m)
            / max(cloud_local_top_m - cloud_local_base_m, 1.0);
        float local_shape = smoothstep(0.0, 0.10, local_u)
            * (1.0 - smoothstep(mix(0.50, 0.85, cells), mix(0.65, 1.0, cells), local_u));
        local_density = local_shape * cloud_local_density_multiplier
            * smoothstep(0.55, 0.73, cells + (billow - 0.5) * 0.25);
        // Skip seven map/detail texture reads inside the procedural region.
        if (local_weight >= 1.0) return local_density;
    }
    vec2 uv = cloud_equirect(texture_direction) + vec2(cloud_longitude_offset, 0.0);
    float weather = cloud_weather_spherical_sample(texture_direction);
    float detail = cloud_detail_sample(uv);
    float macro = cloud_value_noise3(noise_position * max(cloud_macro_scale, 1e-6) + wind_offset);
    float u = (height - cloud_base_altitude) / max(cloud_top_altitude - cloud_base_altitude, 1.0);
    float top_fraction = mix(0.28, 0.68, macro);
    shape *= 1.0 - smoothstep(top_fraction - 0.12, top_fraction + 0.12, u);
    float weather_field = weather + (macro - 0.5) * cloud_macro_strength
        + (detail - 0.5) * 0.20 + (billow - 0.5) * 0.32;
    float threshold = 1.0 - cloud_coverage;
    float low_altitude_shell = 1.0 - smoothstep(6000.0, 45000.0,
        max(cloud_observer_altitude_m, 0.0));
    // Global-map horizon filtering retains the existing latitude-row guard.
    float dither_strength = mix(0.004, 0.025, 1.0 - low_altitude_shell);
    float coverage_dither = (cloud_hash3(noise_position * 0.00037
        + vec3(17.0, 43.0, 71.0)) * 2.0 - 1.0)
        * dither_strength * clamp(cloud_weather_prefilter, 0.0, 1.0);
    float threshold_softness = mix(0.055, 0.175,
        low_altitude_shell * clamp(cloud_weather_prefilter, 0.0, 1.0));
    float global_density = shape * smoothstep(threshold - 0.12 - threshold_softness,
        threshold + 0.10 + threshold_softness, weather_field + coverage_dither);
    return mix(global_density, local_density, local_weight);
}
