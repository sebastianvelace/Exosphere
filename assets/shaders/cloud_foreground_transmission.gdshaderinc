#include "res://assets/shaders/earth_geometry.gdshaderinc"
#include "res://assets/shaders/cloud_field.gdshaderinc"
uniform float cloud_solar_visibility = 1.0;

// Transparent emission must lose contrast through the same foreground cloud.
// No added source term: the background already contains that cloud radiance.
float foreground_cloud_transmission(vec3 delta) {
    if (!cloud_enabled) return 1.0;
    float distance_m = length(delta) * 2.8;
    if (distance_m < 0.001) return 1.0;
    vec3 ray = normalize(delta);
    vec3 eye = physical_camera_up * (physical_planet_radius_m + physical_camera_altitude_m);
    float depth = 0.0;
    for (int i = 0; i < 8; i++) {
        vec3 p = eye + ray * (distance_m * (float(i) + 0.5) / 8.0);
        depth += cloud_density(p) * cloud_extinction * distance_m / 8.0;
    }
    return exp(-min(depth, 80.0));
}
