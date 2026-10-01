shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, fog_disabled;

#include "res://assets/shaders/surface_atmosphere.gdshaderinc"
#include "res://assets/shaders/cloud_surface.gdshaderinc"
uniform vec3 cloud_foreground_sun = vec3(0.0, 1.0, 0.0);

void fragment() {
    // VERTEX is view-space here. End the integral at this visible hull fragment,
    // not at the ground behind it. The original hull keeps its opaque depth.
    vec3 delta = (INV_VIEW_MATRIX * vec4(VERTEX, 0.0)).xyz;
    float distance_m = length(delta) * 2.8;
    float mean_distance;
    vec4 clouds = surface_cloud_radiance(normalize(delta), distance_m,
        normalize(cloud_foreground_sun), 1.0, mean_distance);
    ALBEDO = clouds.rgb / max(clouds.a, 0.000001);
    ALPHA = clouds.a;
}
