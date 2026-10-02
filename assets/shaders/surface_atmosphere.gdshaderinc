#include "res://assets/shaders/earth_geometry.gdshaderinc"
// Camera-to-surface optical segment. Off-limb transport stays in space_sky.
uniform float surface_atmosphere_height_m = 140000.0;
uniform vec3 surface_rayleigh_scattering = vec3(5.802e-6, 13.558e-6, 33.100e-6);
uniform vec3 surface_mie_scattering = vec3(3.996e-6);
uniform vec3 surface_mie_extinction = vec3(4.440e-6);
uniform vec3 surface_ozone_absorption = vec3(0.650e-6, 1.881e-6, 0.085e-6);
uniform float surface_rayleigh_height_m = 8000.0;
uniform float surface_mie_height_m = 1200.0;
uniform float surface_ozone_center_m = 25000.0;
uniform float surface_ozone_half_width_m = 15000.0;
uniform float surface_mie_g = 0.8;
uniform float surface_sun_illuminance = 20.0;
uniform sampler2D surface_density_lut : filter_linear;
uniform float surface_density_lut_top_m = 140000.0;
uniform bool surface_density_lut_enabled = false;
uniform sampler2D surface_solar_lut : filter_linear;
uniform bool surface_solar_lut_enabled = false;


vec3 surface_aerial_radiance(vec3 ground, vec3 ray, float distance_m,
                            vec3 sun, vec3 vertical_depth, float solar_visibility) {
    vec3 origin = physical_camera_up * (physical_planet_radius_m + physical_camera_altitude_m);
    float outer = physical_planet_radius_m + surface_atmosphere_height_m;
    float b = dot(origin, ray);
    float disc = b * b - (dot(origin, origin) - outer * outer);
    if (disc < 0.0) return ground;
    float start = max(-b - sqrt(disc), 0.0);
    float end = min(distance_m, -b + sqrt(disc));
    if (end <= start) return ground;
    vec3 optical_depth = vec3(0.0);
    vec3 radiance = vec3(0.0);
    float mu = dot(ray, sun);
    float phase_r = 3.0 * (1.0 + mu * mu) / (16.0 * PI);
    float g2 = surface_mie_g * surface_mie_g;
    float phase_m = 3.0 * (1.0 - g2) * (1.0 + mu * mu)
        / (8.0 * PI * (2.0 + g2) * pow(max(1.0 + g2 - 2.0 * surface_mie_g * mu, 0.001), 1.5));
    // Warped quadrature resolves the dense end of orbital camera-to-ground rays.
    for (int i = 0; i < 24; i++) {
        float u0 = float(i) / 24.0;
        float u1 = float(i + 1) / 24.0;
        float t0 = end - (end - start) * (1.0 - u0) * (1.0 - u0);
        float t1 = end - (end - start) * (1.0 - u1) * (1.0 - u1);
        float ds = t1 - t0;
        vec3 point = origin + ray * ((t0 + t1) * 0.5);
        float height = max(length(point) - physical_planet_radius_m, 0.0);
        vec3 densities;
        if (surface_density_lut_enabled) {
            float u = sqrt(clamp(height / max(surface_density_lut_top_m, 1.0), 0.0, 1.0));
            densities = max(textureLod(surface_density_lut, vec2(u, 0.5), 0.0).rgb, vec3(0.0));
        } else {
            densities = vec3(exp(-height / max(surface_rayleigh_height_m, 1.0)),
                exp(-height / max(surface_mie_height_m, 1.0)),
                max(1.0 - abs(height - surface_ozone_center_m) / max(surface_ozone_half_width_m, 1.0), 0.0));
        }
        float rho_r = densities.x;
        float rho_m = densities.y;
        float rho_o = densities.z;
        vec3 extinction = surface_rayleigh_scattering * rho_r
            + surface_mie_extinction * rho_m + surface_ozone_absorption * rho_o;
        float sun_mu = dot(normalize(point), sun);
        float horizon_mu = -sqrt(max(1.0 - pow(physical_planet_radius_m / max(length(point), physical_planet_radius_m), 2.0), 0.0));
        float air_mass = 1.0 / max(sun_mu + 0.025 * exp(-11.0 * max(sun_mu, 0.0)), 0.025);
        vec3 sun_depth = surface_rayleigh_scattering * (surface_rayleigh_height_m * rho_r)
            + surface_mie_extinction * (surface_mie_height_m * rho_m)
            + surface_ozone_absorption * max(surface_ozone_half_width_m * rho_o, 0.0);
        vec3 solar = sun_mu > horizon_mu ? exp(-sun_depth * min(air_mass, 40.0)) : vec3(0.0);
        if (surface_solar_lut_enabled && sun_mu > horizon_mu) {
            float u = sqrt(clamp(height / max(surface_atmosphere_height_m, 1.0), 0.0, 1.0));
            float v = sqrt(clamp((sun_mu + 0.04) / 1.04, 0.0, 1.0));
            solar = sun_mu >= -0.04 ? textureLod(surface_solar_lut, vec2(u, v), 0.0).rgb : vec3(0.0);
        }
        vec3 scattering = surface_rayleigh_scattering * rho_r * phase_r
            + surface_mie_scattering * rho_m * phase_m;
        radiance += exp(-optical_depth - extinction * ds * 0.5) * solar * scattering * ds;
        optical_depth += extinction * ds;
    }
    return ground * exp(-optical_depth) + radiance * surface_sun_illuminance * solar_visibility;
}
