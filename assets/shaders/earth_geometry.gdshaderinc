// Shared geographic reference sphere. Distances are SI metres.
uniform vec3 physical_camera_up = vec3(0.0, 1.0, 0.0);
uniform float physical_camera_altitude_m = 0.0;
uniform float physical_planet_radius_m = 6371008.8;
uniform float physical_depth_scale = 1.0;

float surface_distance_m(vec3 ray) {
    float h = max(physical_camera_altitude_m, 0.01) / physical_planet_radius_m;
    vec3 origin = physical_camera_up * (1.0 + h);
    float b = dot(origin, ray);
    float c = h * (2.0 + h);
    float discriminant = b * b - c;
    if (b >= 0.0 || discriminant < 0.0) return -1.0;
    // Stable near root; avoids subtracting two almost equal planetary radii.
    return physical_planet_radius_m * c / (-b + sqrt(discriminant));
}

// Distant bodies keep their well-resolved proxy depth. Reject their fragments
// behind the actual Earth before they can cover the opaque geographic surface.
bool earth_occludes_backdrop(vec3 delta) {
    float distance_m = length(delta) * physical_depth_scale * 2.8;
    float earth_hit_m = surface_distance_m(normalize(delta));
    return earth_hit_m > 0.0 && distance_m > earth_hit_m;
}
