// Exosphere physical atmosphere sky — spherical single scattering.
// Integrates wavelength-banded Rayleigh + aerosol Mie scattering, Mie/ozone absorption,
// planet shadow and stellar extinction from the observer altitude to the atmosphere edge.
shader_type sky;

uniform sampler2D star_tex : source_color, filter_linear_mipmap;
uniform sampler2D cloud_coverage_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform vec3 sun_dir = vec3(0.4, 0.5, 0.8);
uniform vec3 local_up = vec3(0.0, 1.0, 0.0);
uniform float planet_radius = 6371000.0;
uniform float atmosphere_height = 140000.0;
uniform float observer_altitude = 2.0;

uniform vec3 rayleigh_scattering = vec3(5.802e-6, 13.558e-6, 33.100e-6);
uniform vec3 mie_scattering = vec3(3.996e-6);
uniform vec3 mie_absorption = vec3(0.444e-6);
uniform vec3 ozone_absorption = vec3(0.650e-6, 1.881e-6, 0.085e-6);
uniform float rayleigh_scale_height = 8000.0;
uniform float mie_scale_height = 1200.0;
uniform float ozone_center_altitude = 25000.0;
uniform float ozone_half_width = 15000.0;
uniform vec3 airglow_emission = vec3(0.060e-8, 0.144e-8, 0.044e-8);
uniform float airglow_center_altitude = 97000.0;
uniform float airglow_scale_height = 6000.0;
uniform float mie_g = 0.80;
uniform float sun_illuminance = 20.0;
uniform float star_energy = 0.9;
uniform float eye_star_gain = 0.0;
uniform float low_order_diffuse_strength : hint_range(0.0, 0.5) = 0.25;
uniform bool cloud_enabled = false;
uniform float cloud_base_altitude = 1200.0;
uniform float cloud_top_altitude = 12000.0;
uniform float cloud_extinction = 0.00011;
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.58;
uniform float cloud_longitude_offset = 0.0;
uniform mat3 cloud_world_to_texture;
uniform float sun_angular_radius = 0.00465;
uniform float sun_disc_radiance = 32.0;
uniform float solar_visibility : hint_range(0.0, 1.0) = 1.0;
uniform float atmospheric_solar_visibility : hint_range(0.0, 1.0) = 1.0;
uniform bool solar_occluder_enabled = false;
uniform vec3 solar_occluder_dir = vec3(0.0, -1.0, 0.0);
uniform float solar_occluder_angular_radius = 0.0;

uniform vec3 ground_horizon : source_color = vec3(0.30, 0.55, 0.90);
uniform vec3 ground_bottom : source_color = vec3(0.18, 0.30, 0.50);

const int VIEW_STEPS = 16;
const int LIGHT_STEPS = 8;
const int CLOUD_VIEW_STEPS = 12;
const int CLOUD_LIGHT_STEPS = 3;

vec2 ray_sphere(vec3 origin, vec3 direction, float radius) {
	float b = dot(origin, direction);
	float c = dot(origin, origin) - radius * radius;
	float h = b * b - c;
	if (h < 0.0) return vec2(1e20, -1e20);
	h = sqrt(h);
	return vec2(-b - h, -b + h);
}

// Density changes by orders of magnitude close to the ray's tangent altitude. Uniform
// steps can jump over the complete troposphere on long ground-horizon or orbital-limb
// rays. This warp spends samples around the closest approach while retaining both ends.
float tangent_warped_boundary(
	float u, float interval_start, float interval_end, float closest_distance) {
	float span = max(interval_end - interval_start, 1e-3);
	float closest = clamp((closest_distance - interval_start) / span, 0.0, 1.0);
	if (closest <= 0.04) return interval_start + span * u * u;
	if (closest >= 0.96) {
		float reverse = 1.0 - u;
		return interval_end - span * reverse * reverse;
	}
	if (u < 0.5) {
		float q = u * 2.0;
		float toward_tangent = 1.0 - (1.0 - q) * (1.0 - q);
		return mix(interval_start, interval_start + span * closest, toward_tangent);
	}
	float q = (u - 0.5) * 2.0;
	return mix(interval_start + span * closest, interval_end, q * q);
}

vec3 density_at_height(float height) {
	float h = max(height, 0.0);
	float rayleigh = exp(-h / max(rayleigh_scale_height, 1.0));
	float mie = exp(-h / max(mie_scale_height, 1.0));
	float ozone = ozone_half_width > 0.0
		? max(0.0, 1.0 - abs(h - ozone_center_altitude) / ozone_half_width)
		: 0.0;
	return vec3(rayleigh, mie, ozone);
}

float airglow_density_at_height(float height) {
	float sigma = max(airglow_scale_height, 1.0);
	float z = (height - airglow_center_altitude) / sigma;
	return exp(-0.5 * z * z);
}

vec3 extinction(vec3 density) {
	return rayleigh_scattering * density.x
		+ (mie_scattering + mie_absorption) * density.y
		+ ozone_absorption * density.z;
}

vec3 transmittance(vec3 optical_depth) {
	return exp(-clamp(optical_depth, vec3(0.0), vec3(80.0)));
}

vec3 optical_depth_to_sun(vec3 position, vec3 direction, float outer_radius) {
	vec2 ground_hit = ray_sphere(position, direction, planet_radius);
	if (ground_hit.x > 0.0) return vec3(1e6);
	vec2 atmosphere_hit = ray_sphere(position, direction, outer_radius);
	float distance_to_space = max(atmosphere_hit.y, 0.0);
	float closest = max(-dot(position, direction), 0.0);
	vec3 depth = vec3(0.0);
	for (int i = 0; i < LIGHT_STEPS; i++) {
		float segment_start = tangent_warped_boundary(
			float(i) / float(LIGHT_STEPS), 0.0, distance_to_space, closest);
		float segment_end = tangent_warped_boundary(
			float(i + 1) / float(LIGHT_STEPS), 0.0, distance_to_space, closest);
		float step_length = max(segment_end - segment_start, 0.0);
		float t = 0.5 * (segment_start + segment_end);
		float height = length(position + direction * t) - planet_radius;
		depth += extinction(density_at_height(height)) * step_length;
	}
	return depth;
}

bool sun_is_occluded(vec3 position, vec3 direction) {
	vec2 ground_hit = ray_sphere(position, direction, planet_radius);
	return ground_hit.x > 0.0;
}

vec2 dir_to_equirect(vec3 direction) {
	float lon = atan(direction.z, direction.x);
	float lat = asin(clamp(direction.y, -1.0, 1.0));
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

float cloud_height_shape(float height) {
	float u = (height - cloud_base_altitude)
		/ max(cloud_top_altitude - cloud_base_altitude, 1.0);
	return smoothstep(0.0, 0.12, u) * (1.0 - smoothstep(0.72, 1.0, u));
}

float cloud_density(vec3 position) {
	float height = length(position) - planet_radius;
	float shape = cloud_height_shape(height);
	if (shape <= 0.0) return 0.0;
	vec3 texture_direction = normalize(cloud_world_to_texture * normalize(position));
	vec2 uv = dir_to_equirect(texture_direction) + vec2(cloud_longitude_offset, 0.0);
	float weather = texture(cloud_coverage_tex, uv).r;
	float detail = texture(cloud_coverage_tex, uv * 3.07 + vec2(0.17, 0.31)).r;
	float threshold = 1.0 - cloud_coverage;
	return shape * smoothstep(threshold - 0.12, threshold + 0.10,
		weather + (detail - 0.5) * 0.22);
}

float cloud_sun_transmittance(vec3 position, vec3 to_sun) {
	if (sun_is_occluded(position, to_sun)) return 0.0;
	vec2 outer_hit = ray_sphere(position, to_sun, planet_radius + cloud_top_altitude);
	float distance_to_edge = max(outer_hit.y, 0.0);
	float closest = max(-dot(position, to_sun), 0.0);
	float optical_depth = 0.0;
	for (int i = 0; i < CLOUD_LIGHT_STEPS; i++) {
		float segment_start = tangent_warped_boundary(float(i) / float(CLOUD_LIGHT_STEPS),
			0.0, distance_to_edge, closest);
		float segment_end = tangent_warped_boundary(float(i + 1) / float(CLOUD_LIGHT_STEPS),
			0.0, distance_to_edge, closest);
		float step_length = max(segment_end - segment_start, 0.0);
		vec3 p = position + to_sun * (0.5 * (segment_start + segment_end));
		optical_depth += cloud_density(p) * cloud_extinction * step_length;
	}
	return exp(-min(optical_depth, 80.0));
}

float hg_phase(float mu, float g) {
	float g2 = g * g;
	return (1.0 - g2) / (4.0 * PI
		* pow(max(1.0 + g2 - 2.0 * g * mu, 1e-3), 1.5));
}

void sky() {
	vec3 view_dir = normalize(EYEDIR);
	vec3 up = normalize(local_up);
	vec3 to_sun = normalize(sun_dir);
	float outer_radius = planet_radius + atmosphere_height;
	vec3 origin = up * (planet_radius + max(observer_altitude, 1.0));

	vec2 atmosphere_hit = ray_sphere(origin, view_dir, outer_radius);
	float ray_start = max(atmosphere_hit.x, 0.0);
	float ray_end = atmosphere_hit.y;
	bool crosses_atmosphere = ray_end > ray_start;
	bool hits_ground = false;

	vec2 ground_hit = ray_sphere(origin, view_dir, planet_radius);
	if (ground_hit.x > ray_start && ground_hit.x < ray_end) {
		ray_end = ground_hit.x;
		hits_ground = true;
	}

	vec3 optical_depth = vec3(0.0);
	vec3 in_scatter = vec3(0.0);
	vec3 diffuse_scatter = vec3(0.0);
	vec3 airglow_radiance = vec3(0.0);
	if (crosses_atmosphere && ray_end > ray_start) {
		float closest = max(-dot(origin, view_dir), 0.0);
		float mu = dot(view_dir, to_sun);
		float mu2 = mu * mu;
		float rayleigh_phase = 3.0 * (1.0 + mu2) / (16.0 * PI);
		float g2 = mie_g * mie_g;
		float mie_phase = 3.0 * (1.0 - g2) * (1.0 + mu2)
			/ (8.0 * PI * (2.0 + g2)
			* pow(max(1.0 + g2 - 2.0 * mie_g * mu, 1e-3), 1.5));

		for (int i = 0; i < VIEW_STEPS; i++) {
			float segment_start = tangent_warped_boundary(float(i) / float(VIEW_STEPS),
				ray_start, ray_end, closest);
			float segment_end = tangent_warped_boundary(float(i + 1) / float(VIEW_STEPS),
				ray_start, ray_end, closest);
			float step_length = max(segment_end - segment_start, 0.0);
			float t = 0.5 * (segment_start + segment_end);
			vec3 sample_position = origin + view_dir * t;
			float height = length(sample_position) - planet_radius;
			vec3 density = density_at_height(height);
			vec3 sample_extinction = extinction(density);
			vec3 segment_depth = sample_extinction * step_length;
			vec3 sun_depth = optical_depth_to_sun(sample_position, to_sun, outer_radius);
			vec3 view_throughput = transmittance(optical_depth + segment_depth * 0.5);
			vec3 sun_throughput = transmittance(sun_depth);
			vec3 throughput = view_throughput * sun_throughput;
			vec3 scattering = rayleigh_scattering * density.x * rayleigh_phase
				+ mie_scattering * density.y * mie_phase;
			in_scatter += throughput * scattering * step_length;

			// Conservative local order-2 closure: redistribute a bounded fraction of
			// direct-beam losses isotropically. Planet shadow is kept at zero explicitly.
			if (!sun_is_occluded(sample_position, to_sun)) {
				vec3 beta_s = rayleigh_scattering * density.x + mie_scattering * density.y;
				vec3 omega = clamp(beta_s / max(sample_extinction, vec3(1e-12)), vec3(0.0), vec3(1.0));
				vec3 removed = clamp(vec3(1.0) - sun_throughput, vec3(0.0), vec3(1.0));
				vec3 source_2 = beta_s * omega * removed
					* (low_order_diffuse_strength / (4.0 * PI));
				diffuse_scatter += view_throughput * source_2 * step_length;
			}

			// Nightglow is emission, not reflected sunlight. Fade it only where daylight
			// overwhelms the tenuous 95–100 km O/OH layer; view extinction still applies.
			float local_solar_elevation = dot(normalize(sample_position), to_sun);
			float night_visibility = 1.0 - smoothstep(-0.10, 0.08, local_solar_elevation);
			airglow_radiance += view_throughput * airglow_emission
				* airglow_density_at_height(height) * night_visibility * step_length;
			optical_depth += segment_depth;
		}
	}

	// Exact nearest interval through the spherical cloud shell. This remains geometric
	// from below the base through orbit; no altitude colour fade is involved.
	vec3 cloud_radiance = vec3(0.0);
	float cloud_transmittance = 1.0;
	if (cloud_enabled && cloud_top_altitude > cloud_base_altitude) {
		vec2 outer_cloud = ray_sphere(origin, view_dir, planet_radius + cloud_top_altitude);
		vec2 inner_cloud = ray_sphere(origin, view_dir, planet_radius + cloud_base_altitude);
		float cloud_start = max(outer_cloud.x, 0.0);
		float cloud_end = outer_cloud.y;
		float origin_radius = length(origin);
		if (origin_radius < planet_radius + cloud_base_altitude && inner_cloud.y > cloud_start)
			cloud_start = inner_cloud.y;
		else if (inner_cloud.x > cloud_start && inner_cloud.x < cloud_end)
			cloud_end = inner_cloud.x;
		if (ground_hit.x > cloud_start && ground_hit.x < cloud_end)
			cloud_end = ground_hit.x;

		if (cloud_end > cloud_start) {
			float cloud_closest = max(-dot(origin, view_dir), 0.0);
			float mu_cloud = dot(view_dir, to_sun);
			float phase_cloud = 0.82 * hg_phase(mu_cloud, 0.82)
				+ 0.18 * hg_phase(mu_cloud, -0.20);
			for (int i = 0; i < CLOUD_VIEW_STEPS; i++) {
				float segment_start = tangent_warped_boundary(
					float(i) / float(CLOUD_VIEW_STEPS), cloud_start, cloud_end, cloud_closest);
				float segment_end = tangent_warped_boundary(
					float(i + 1) / float(CLOUD_VIEW_STEPS), cloud_start, cloud_end, cloud_closest);
				float cloud_step = max(segment_end - segment_start, 0.0);
				vec3 p = origin + view_dir * (0.5 * (segment_start + segment_end));
				float rho = cloud_density(p);
				if (rho > 0.001) {
					float segment_alpha = 1.0
						- exp(-rho * cloud_extinction * cloud_step);
					float direct_light = cloud_sun_transmittance(p, to_sun);
					vec3 atmospheric_sun = transmittance(
						optical_depth_to_sun(p, to_sun, outer_radius));
					vec3 source = atmospheric_sun
						* (direct_light * phase_cloud * atmospheric_solar_visibility + 0.018);
					cloud_radiance += cloud_transmittance * source * segment_alpha;
					cloud_transmittance *= 1.0 - segment_alpha;
				}
			}
		}
	}

	vec3 stars = texture(star_tex, dir_to_equirect(view_dir)).rgb * star_energy;
	vec3 star_transmittance = crosses_atmosphere ? transmittance(optical_depth) : vec3(1.0);
	if (hits_ground) stars = vec3(0.0);

	// Return one linear HDR radiance domain. Godot's environment applies exposure and
	// tonemapping exactly once; compressing here previously clipped the limb before the
	// camera model could adapt and made the photosphere incomparable with the sky.
	// Observer-level visibility includes the planet below the horizon. The separate
	// atmospheric value excludes that local planet, because samples above the observer can
	// still see the Sun and produce twilight. It retains external occultations (Moon, etc.).
	vec3 atmosphere = ((in_scatter + diffuse_scatter) * atmospheric_solar_visibility
		+ cloud_radiance) * sun_illuminance;
	vec3 airglow = airglow_radiance * sun_illuminance;
	float sky_luminance = dot(atmosphere, vec3(0.2126, 0.7152, 0.0722));
	float eye_star_visibility = 1.0 - smoothstep(0.002, 0.035, sky_luminance);
	vec3 color = atmosphere + airglow + stars * star_transmittance * cloud_transmittance
		* eye_star_visibility * eye_star_gain;

	// Resolved solar photosphere. The radial term approximates visible-band limb
	// darkening; an angular occulting disc produces partial, total and annular eclipses.
	float sun_chord = length(view_dir - to_sun);
	float sun_radius_chord = 2.0 * sin(0.5 * sun_angular_radius);
	float sun_edge = max(sun_radius_chord * 0.08, 0.00025);
	float solar_disc = 1.0 - smoothstep(sun_radius_chord - sun_edge,
		sun_radius_chord + sun_edge, sun_chord);
	float radial = clamp(sun_chord / max(sun_radius_chord, 1e-6), 0.0, 1.0);
	float mu_limb = sqrt(max(1.0 - radial * radial, 0.0));
	float limb_darkening = (0.40 + 0.60 * mu_limb) / 0.80;
	float occultation = 1.0;
	if (solar_occluder_enabled) {
		float occ_chord = length(view_dir - normalize(solar_occluder_dir));
		float occ_radius_chord = 2.0 * sin(0.5 * solar_occluder_angular_radius);
		float occ_edge = max(occ_radius_chord * 0.015, 0.00015);
		occultation = smoothstep(occ_radius_chord - occ_edge,
			occ_radius_chord + occ_edge, occ_chord);
	}
	if (hits_ground) solar_disc = 0.0;
	vec3 solar_color = vec3(1.0, 0.985, 0.94) * star_transmittance;
	color += solar_color * sun_disc_radiance * solar_disc
		* limb_darkening * occultation * cloud_transmittance;

	if (hits_ground) {
		float nadir = clamp(-dot(view_dir, up), 0.0, 1.0);
		vec3 ground = mix(ground_horizon, ground_bottom, sqrt(nadir));
		vec3 ground_position = origin + view_dir * ray_end;
		float ground_sun = max(dot(normalize(ground_position), to_sun), 0.0);
		vec3 ground_direct = transmittance(
			optical_depth_to_sun(ground_position, to_sun, outer_radius));
		color += ground * transmittance(optical_depth)
			* (0.012 + 0.24 * ground_sun * ground_direct * solar_visibility);
	}

	COLOR = max(color, vec3(0.0));
}
