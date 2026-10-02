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
	float center = textureLod(cloud_coverage_tex, center_uv, 0.0).r;
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

// Domain-warped spherical detail avoids scaled copies of the geographic map.
// Reduce fine detail for grazing, low-sun views.
float cloud_detail_sample(vec3 position) {
    vec3 p = position * 0.00031;
    float displacement = cloud_value_noise3(p * 0.31 + vec3(5.0)) - 0.5;
    vec3 warp = displacement * vec3(1.0, -0.73, 0.49);
    float coarse = cloud_value_noise3(p + warp * 2.0);
    float fine = cloud_value_noise3(p * 2.73 + warp);
    float filtered = clamp(cloud_weather_prefilter, 0.0, 1.0);
    return mix(coarse * 0.68 + fine * 0.32, coarse, filtered);
}

bool cloud_in_local_core(vec3 position) {
    vec3 direction = normalize(cloud_world_to_texture * normalize(position));
    return cloud_local_weather_enabled && length(direction - cloud_local_center)
        * cloud_planet_radius_m < cloud_local_radius_m * 0.70;
}

// Shared occupancy and centres keep the empty-space bound conservative for the
// same cloud cells used by view, shadow and foreground transmission rays.
float cloud_coastal_threshold(vec2 id) {
    vec2 p = id * 0.17;
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float regional = mix(
        mix(cloud_hash3(vec3(cell, 5.0)), cloud_hash3(vec3(cell + vec2(1.0, 0.0), 5.0)), f.x),
        mix(cloud_hash3(vec3(cell + vec2(0.0, 1.0), 5.0)),
            cloud_hash3(vec3(cell + vec2(1.0), 5.0)), f.x), f.y);
    return mix(0.05, 0.995, smoothstep(0.25, 0.75, regional));
}
vec2 cloud_coastal_centre(vec2 id) {
    return id + vec2(0.08) + 0.84 * vec2(
        cloud_hash3(vec3(id, 31.0)), cloud_hash3(vec3(id, 47.0)));
}

// Smoothly bend the cell lattice into regional weather flow. The same mapping
// must be used for density and empty-space bounds; its Jacobian norm stays below 2.
vec2 cloud_coastal_xy(vec3 local, vec3 east, vec3 north, float scale, vec2 wind) {
    vec2 xy = vec2(dot(local, east), dot(local, north)) / scale + wind;
    vec2 warp = vec2(sin(xy.y * 0.21 + sin(xy.x * 0.11)),
        cos(xy.x * 0.19 + sin(xy.y * 0.13))) * 0.8;
    return xy + warp;
}

// Rounded, isolated coastal cells rather than thresholded value-noise columns.
// The flat condensation base and variable dome share one metre-coordinate field
// for the sky, terrain and foreground vessel. This is appearance calibration.
float cloud_coastal_density(vec3 position, float height, vec3 wind) {
    if (height < cloud_local_base_m || height > cloud_local_top_m) return 0.0;
    vec3 up = normalize(cloud_local_center);
    vec3 east = normalize(cross(up, vec3(0.0, 1.0, 0.0)));
    vec3 north = cross(up, east);
    vec3 local = cloud_world_to_texture * position - up * cloud_planet_radius_m;
    float scale = max(cloud_local_scale_m, 1.0);
    vec2 xy = cloud_coastal_xy(local, east, north, scale, wind.xz);
    vec2 cell = floor(xy);
    float density = 0.0;
    float erosion = 0.0;
    float billow = 0.0;
    bool detail_ready = false;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 id = cell + vec2(float(x), float(y));
            float seed = cloud_hash3(vec3(id, 19.0));
            if (seed < 0.05 || (seed < 0.995 && seed < cloud_coastal_threshold(id))) continue;
            vec2 centre = cloud_coastal_centre(id);
            float radius = mix(0.15, 0.56, cloud_hash3(vec3(id, 73.0)));
            // The largest aspect is 1.20; billow can extend the 1.05 footprint
            // by at most 0.11. 1.40 encloses 1.20 * 1.16, so empty samples can
            // skip rotation, lobes and noise without changing any cloud density.
            if (length(xy - centre) > radius * 1.40) continue;
            float angle = cloud_hash3(vec3(id, 67.0)) * 6.283185;
            mat2 rotation = mat2(vec2(cos(angle), -sin(angle)), vec2(sin(angle), cos(angle)));
            vec2 aspect = vec2(1.0, mix(0.60, 1.20, cloud_hash3(vec3(id, 71.0))));
            vec2 offset = rotation * (xy - centre) / (radius * aspect);
            if (!detail_ready) {
                erosion = cloud_value_noise3(local * 0.0021 + wind);
                billow = 0.65 * cloud_value_noise3(local * 0.0011 + wind)
                    + 0.35 * cloud_value_noise3(local * 0.0041 + wind);
                detail_ready = true;
            }
            float radial = length(offset) - (billow - 0.5) * 0.22;
            if (radial >= 1.05) continue;
            float footprint = 1.0 - smoothstep(0.65, 1.05, radial);
            // Several overlapping condensation lobes give a cell an uneven crown.
            // Their tops remain inside the declared meteorological shell.
            float dome = 0.45 * sqrt(max(1.0 - radial * radial * 0.80, 0.0));
            for (int lobe = 0; lobe < 4; lobe++) {
                if (float(lobe) > mix(1.0, 3.99, cloud_hash3(vec3(id, 89.0)))) break;
                float lobe_angle = cloud_hash3(vec3(id, 113.0 + float(lobe))) * 6.283185;
                vec2 lobe_centre = vec2(cos(lobe_angle), sin(lobe_angle))
                    * mix(0.15, 0.55, cloud_hash3(vec3(id, 127.0 + float(lobe))));
                float lobe_radius = mix(0.45, 0.72,
                    cloud_hash3(vec3(id, 91.0 + float(lobe))));
                float d = length(offset - lobe_centre) / lobe_radius;
                float crown = sqrt(max(1.0 - d * d, 0.0));
                dome = max(dome, crown * mix(0.55, 1.0,
                    cloud_hash3(vec3(id, 101.0 + float(lobe)))));
            }
            float depth = cloud_local_top_m - cloud_local_base_m;
            float cap = cloud_local_base_m + depth * mix(0.45, 0.95, seed) * dome
                + (billow - 0.5) * depth * 0.28;
            float base = smoothstep(cloud_local_base_m, cloud_local_base_m + 140.0, height);
            float top = 1.0 - smoothstep(cap - 180.0, cap + 50.0, height);
            float detail = clamp(0.72 + billow * 0.55 - erosion * 0.38, 0.0, 1.0);
            density = max(density, footprint * base * top * detail);
        }
    }
    return density * cloud_local_density_multiplier;
}

// Conservative empty-space bound for local view rays. A cylinder encloses each
// billowing cell; stepping less than this distance cannot skip a dense boundary.
float cloud_coastal_empty_distance(vec3 position) {
    vec3 up = normalize(cloud_local_center);
    vec3 east = normalize(cross(up, vec3(0.0, 1.0, 0.0)));
    vec3 north = cross(up, east);
    vec3 local = cloud_world_to_texture * position - up * cloud_planet_radius_m;
    float scale = max(cloud_local_scale_m, 1.0);
    vec2 wind = vec2(cloud_longitude_offset * 64.0, cloud_longitude_offset * 31.0);
    vec2 xy = cloud_coastal_xy(local, east, north, scale, wind);
    vec2 cell = floor(xy);
    // Unvisited cells start at least 1.08 cells away. Their maximum enclosing
    // radius is 0.56 * 1.45 = 0.812; a quarter-cell bound cannot jump into them.
    float distance_m = scale * 0.25;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 id = cell + vec2(float(x), float(y));
            float seed = cloud_hash3(vec3(id, 19.0));
            if (seed < 0.05 || (seed < 0.995 && seed < cloud_coastal_threshold(id))) continue;
            vec2 centre = cloud_coastal_centre(id);
            float radius = mix(0.15, 0.56, cloud_hash3(vec3(id, 73.0)));
            distance_m = min(distance_m, (length(xy - centre) - radius * 1.45) * scale);
        }
    }
    float height = length(position) - cloud_planet_radius_m;
    return max(max(distance_m / 2.0, cloud_local_base_m - height), height - cloud_local_top_m);
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
    if (local_weight > 0.0) {
        // Preserve clear holes; horizon map filtering does not alter local cells.
        local_density = cloud_coastal_density(position, height, wind_offset);
        // Skip global weather/detail sampling inside the procedural region.
        if (local_weight >= 1.0) return local_density;
    }
    float billow = cloud_value_noise3(noise_position * 0.00065 + wind_offset);
    float weather = cloud_weather_spherical_sample(texture_direction);
    float detail = cloud_detail_sample(noise_position + wind_offset * 3200.0);
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
// Resolve the nearby illuminated face before sampling the opaque interior.
// A coarse uniform light march skips thin sunlit crowns in dense coastal cells.
// The integral still uses the same extinction/density as the camera ray.
float cloud_local_sun_transmission_samples(vec3 point, vec3 sun, float distance_m, int samples) {
    float depth = 0.0;
    for (int i = 0; i < 10; i++) {
        if (i >= samples) break;
        float u0 = float(i) / float(samples);
        float u1 = float(i + 1) / float(samples);
        float a = u0 * u0 * distance_m;
        float b = u1 * u1 * distance_m;
        depth += cloud_density(point + sun * (0.5 * (a + b)))
            * cloud_extinction * (b - a);
        if (depth > 8.0) break;
    }
    return exp(-min(depth, 80.0));
}

float cloud_local_sun_transmission(vec3 point, vec3 sun, float distance_m) {
    return cloud_local_sun_transmission_samples(point, sun, distance_m, 10);
}
