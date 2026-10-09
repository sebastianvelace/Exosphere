shader_type canvas_item;
render_mode blend_disabled;

#define CLOUD_DENSITY_BAKE
#include "res://assets/shaders/cloud_field.gdshaderinc"
uniform float cache_half_extent_m = 16000.0;
uniform int cache_side = 256;
uniform int cache_slice = 0;
uniform float cache_bottom_m = 1100.0;
uniform float cache_height_m = 1400.0;

void fragment() {
    // Sample grid endpoints, not tile borders. Alpha is linear data on both
    // renderers; it avoids sRGB conversion and HDR framebuffer requirements.
    float side = float(cache_side);
    vec2 grid = (UV * side - vec2(0.5)) / (side - 1.0);
    vec2 xy = (grid * 2.0 - 1.0) * cache_half_extent_m;
    float height = cache_bottom_m + float(cache_slice) / 31.0 * cache_height_m;
    vec3 up = normalize(cloud_local_center);
    vec3 east = normalize(cross(up, vec3(0.0, 1.0, 0.0)));
    vec3 north = cross(up, east);
    float radius = cloud_planet_radius_m + height;
    vec3 point = up * sqrt(max(radius * radius - dot(xy, xy), 0.0))
        + east * xy.x + north * xy.y;
    // The bake is already in the geographic frame. Transform back so the
    // unchanged shared procedural field performs its normal world-to-texture map.
    vec3 world = inverse(cloud_world_to_texture) * point;
    float density = cloud_density(world) / max(cloud_local_density_multiplier, 0.000001);
    COLOR = vec4(0.0, 0.0, 0.0, clamp(density, 0.0, 1.0));
}
