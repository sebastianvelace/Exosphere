#include "res://assets/shaders/cloud_field.gdshaderinc"
// Eclipse attenuation excludes the observing planet; each elevated sample
// resolves its own planetary horizon in surface_cloud_solar.
uniform float cloud_solar_visibility : hint_range(0.0, 1.0) = 1.0;

// Bounded visual transport through an elevated cloud shell, not a ground decal.
// Twenty-four camera and four shadow samples share the sky's density field. Molecular
// foreground transport uses a radiance-weighted distance closure at the caller.
vec2 surface_cloud_sphere(vec3 origin, vec3 ray, float radius) {
    float b = dot(origin, ray);
    float d = b * b - (dot(origin, origin) - radius * radius);
    if (d < 0.0) return vec2(-1.0);
    return vec2(-b - sqrt(d), -b + sqrt(d));
}

float surface_cloud_shadow(vec3 point, vec3 sun) {
    vec2 hit = surface_cloud_sphere(point, sun, cloud_planet_radius_m + cloud_top_altitude);
    if (dot(normalize(point), sun) <= 0.0) return 1.0;
    float distance_m = max(hit.y, 0.0);
    if (cloud_in_local_core(point) && cloud_in_local_core(point + sun * distance_m))
        distance_m = max(surface_cloud_sphere(point, sun,
            cloud_planet_radius_m + cloud_local_top_m).y, 0.0);
    if (cloud_in_local_core(point) && cloud_in_local_core(point + sun * distance_m))
        return cloud_local_sun_transmission_samples(point, sun, distance_m, 4);
    float depth = 0.0;
    for (int i = 0; i < 4; i++) {
        float t = (float(i) + 0.5) / 4.0;
        // Quadratic placement resolves the nearby lit cloud face on tangent rays.
        float a = float(i) / 4.0;
        float b = float(i + 1) / 4.0;
        float ds = (b * b - a * a) * distance_m;
        depth += cloud_density(point + sun * (t * t * distance_m))
            * cloud_extinction * ds;
    }
    return exp(-min(depth, 80.0));
}

vec3 surface_cloud_solar(vec3 point, vec3 sun) {
    float height = max(length(point) - cloud_planet_radius_m, 0.0);
    float mu = dot(normalize(point), sun);
    float horizon = -sqrt(max(1.0 - pow(cloud_planet_radius_m / length(point), 2.0), 0.0));
    if (mu <= horizon) return vec3(0.0);
    if (surface_solar_lut_enabled && mu >= -0.04) {
        float u = sqrt(clamp(height / max(surface_atmosphere_height_m, 1.0), 0.0, 1.0));
        float v = sqrt(clamp((mu + 0.04) / 1.04, 0.0, 1.0));
        return textureLod(surface_solar_lut, vec2(u, v), 0.0).rgb;
    }
    float air_mass = min(40.0, 1.0 / max(mu + 0.025 * exp(-11.0 * max(mu, 0.0)), 0.025));
    vec3 depth = surface_rayleigh_scattering * surface_rayleigh_height_m
        * exp(-height / max(surface_rayleigh_height_m, 1.0))
        + surface_mie_extinction * surface_mie_height_m
        * exp(-height / max(surface_mie_height_m, 1.0))
        + surface_ozone_absorption * surface_ozone_half_width_m
        * max(1.0 - abs(height - surface_ozone_center_m) / max(surface_ozone_half_width_m, 1.0), 0.0);
    return exp(-depth * air_mass);
}

float surface_cloud_phase(float mu, float g) {
    return (1.0 - g * g) / (4.0 * PI
        * pow(max(1.0 + g * g - 2.0 * g * mu, 0.001), 1.5));
}

vec4 surface_cloud_radiance(vec3 ray, float ground_distance, vec3 sun,
                            float amount, out float mean_distance) {
    mean_distance = 0.0;
    if (!cloud_enabled || amount <= 0.0) return vec4(0.0);
    vec3 origin = physical_camera_up * (physical_planet_radius_m + physical_camera_altitude_m);
    vec2 outer = surface_cloud_sphere(origin, ray, cloud_planet_radius_m + cloud_top_altitude);
    vec2 inner = surface_cloud_sphere(origin, ray, cloud_planet_radius_m + cloud_base_altitude);
    float start = max(outer.x, 0.0);
    float end = min(outer.y, ground_distance);
    if (length(origin) < cloud_planet_radius_m + cloud_base_altitude && inner.y > start)
        start = inner.y;
    else if (inner.x > start && inner.x < end) end = inner.x;
    vec2 local_hit = surface_cloud_sphere(origin, ray, cloud_planet_radius_m + cloud_local_top_m);
    if (cloud_in_local_core(origin + ray * max(start, local_hit.x))
            && cloud_in_local_core(origin + ray * min(end, local_hit.y))) {
        start = max(start, local_hit.x);
        end = min(end, local_hit.y);
    }
    if (end <= start) return vec4(0.0);
    float phase = 0.82 * surface_cloud_phase(dot(ray, sun), 0.82)
        + 0.18 * surface_cloud_phase(dot(ray, sun), -0.20);
    vec3 radiance = vec3(0.0);
    float transmission = 1.0;
    float distance_weight = 0.0;
    for (int i = 0; i < 24; i++) {
        // Opaque cloud interiors do not need another nested density/light march
        // for each terrain fragment. Keep residual throughput below 0.5%.
        if (transmission < 0.005) break;
        // Quadratic placement concentrates samples at the dense end of a
        // downward ray. Direction-stable jitter breaks coherent shell bands.
        float u0 = float(i) / 24.0;
        float u1 = float(i + 1) / 24.0;
        float a = end - (end - start) * (1.0 - u0) * (1.0 - u0);
        float b = end - (end - start) * (1.0 - u1) * (1.0 - u1);
        float ds = b - a;
        float jitter = cloud_hash3(ray * 7919.0 + vec3(float(i) * 17.0));
        float t = mix(a, b, 0.35 + 0.30 * jitter);
        vec3 point = origin + ray * t;
        float alpha = 1.0 - exp(-cloud_density(point) * cloud_extinction * ds * amount);
        if (alpha > 0.001) {
            float weight = transmission * alpha;
            vec3 solar = surface_cloud_solar(point, sun);
            float daylight = smoothstep(-0.16, 0.08, dot(normalize(point), sun));
            float direct_light = surface_cloud_shadow(point, sun);
            // Bounded diffuse closure: the lit crown receives more incident skylight
            // than the shadowed base. This is not a droplet scattering solver.
            float diffuse = (0.018 + 0.025 * direct_light) * daylight;
            vec3 sky_fill = 0.03 * (vec3(1.0) - exp(-surface_rayleigh_scattering * surface_rayleigh_height_m * 4.0));
            vec3 source = cloud_solar_visibility * surface_sun_illuminance
                * (solar * (direct_light * phase + diffuse) + sky_fill * daylight);
            radiance += source * weight;
            distance_weight += t * weight;
            transmission *= 1.0 - alpha;
        }
    }
    float opacity = 1.0 - transmission;
    mean_distance = distance_weight / max(opacity, 0.000001);
    return vec4(radiance, opacity);
}

vec3 surface_weather_radiance(vec3 ground, vec3 ray, float distance_m,
                              vec3 sun, vec3 vertical_depth, float visibility, float amount) {
    vec3 ground_radiance = surface_aerial_radiance(ground, ray, distance_m,
        sun, vertical_depth, visibility);
    float cloud_distance;
    vec4 clouds = surface_cloud_radiance(ray, distance_m, sun, amount, cloud_distance);
    vec3 cloud_radiance = clouds.a > 0.000001
        ? surface_aerial_radiance(clouds.rgb / clouds.a, ray, cloud_distance,
            sun, vertical_depth, visibility) : vec3(0.0);
    return ground_radiance * (1.0 - clouds.a) + cloud_radiance * clouds.a;
}
