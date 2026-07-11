// Exosphere physical atmosphere sky — spherical single scattering.
// Integrates wavelength-banded Rayleigh + aerosol Mie scattering, Mie/ozone absorption,
// planet shadow and stellar extinction from the observer altitude to the atmosphere edge.
shader_type sky;

uniform sampler2D star_tex : source_color, filter_linear_mipmap;
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
uniform float mie_g = 0.80;
uniform float sun_illuminance = 20.0;
uniform float star_energy = 0.9;

uniform vec3 ground_horizon : source_color = vec3(0.30, 0.55, 0.90);
uniform vec3 ground_bottom : source_color = vec3(0.18, 0.30, 0.50);

const int VIEW_STEPS = 12;
const int LIGHT_STEPS = 6;

vec2 ray_sphere(vec3 origin, vec3 direction, float radius) {
	float b = dot(origin, direction);
	float c = dot(origin, origin) - radius * radius;
	float h = b * b - c;
	if (h < 0.0) return vec2(1e20, -1e20);
	h = sqrt(h);
	return vec2(-b - h, -b + h);
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
	float step_length = distance_to_space / float(LIGHT_STEPS);
	vec3 depth = vec3(0.0);
	for (int i = 0; i < LIGHT_STEPS; i++) {
		float t = (float(i) + 0.5) * step_length;
		float height = length(position + direction * t) - planet_radius;
		depth += extinction(density_at_height(height)) * step_length;
	}
	return depth;
}

vec2 dir_to_equirect(vec3 direction) {
	float lon = atan(direction.z, direction.x);
	float lat = asin(clamp(direction.y, -1.0, 1.0));
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
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
	if (crosses_atmosphere && ray_end > ray_start) {
		float step_length = (ray_end - ray_start) / float(VIEW_STEPS);
		float mu = dot(view_dir, to_sun);
		float mu2 = mu * mu;
		float rayleigh_phase = 3.0 * (1.0 + mu2) / (16.0 * PI);
		float g2 = mie_g * mie_g;
		float mie_phase = 3.0 * (1.0 - g2) * (1.0 + mu2)
			/ (8.0 * PI * (2.0 + g2)
			* pow(max(1.0 + g2 - 2.0 * mie_g * mu, 1e-3), 1.5));

		for (int i = 0; i < VIEW_STEPS; i++) {
			float t = ray_start + (float(i) + 0.5) * step_length;
			vec3 sample_position = origin + view_dir * t;
			float height = length(sample_position) - planet_radius;
			vec3 density = density_at_height(height);
			vec3 sample_extinction = extinction(density);
			vec3 sun_depth = optical_depth_to_sun(sample_position, to_sun, outer_radius);
			vec3 throughput = transmittance(optical_depth + sun_depth);
			vec3 scattering = rayleigh_scattering * density.x * rayleigh_phase
				+ mie_scattering * density.y * mie_phase;
			in_scatter += throughput * scattering * step_length;
			optical_depth += sample_extinction * step_length;
		}
	}

	vec3 stars = texture(star_tex, dir_to_equirect(view_dir)).rgb * star_energy;
	vec3 star_transmittance = crosses_atmosphere ? transmittance(optical_depth) : vec3(1.0);
	if (hits_ground) stars = vec3(0.0);

	// Exponential filmic response keeps the physical HDR integral bounded for Godot's sky.
	vec3 atmosphere = vec3(1.0) - exp(-in_scatter * sun_illuminance);
	float sky_luminance = dot(atmosphere, vec3(0.2126, 0.7152, 0.0722));
	float eye_star_visibility = 1.0 - smoothstep(0.002, 0.035, sky_luminance);
	vec3 color = atmosphere + stars * star_transmittance * eye_star_visibility;

	if (hits_ground) {
		float nadir = clamp(-dot(view_dir, up), 0.0, 1.0);
		vec3 ground = mix(ground_horizon, ground_bottom, sqrt(nadir));
		color += ground * transmittance(optical_depth) * 0.12;
	}

	COLOR = max(color, vec3(0.0));
}
