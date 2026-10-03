shader_type spatial;
#include "res://assets/shaders/backdrop_depth.gdshaderinc"
#include "res://assets/shaders/earth_geometry.gdshaderinc"
render_mode cull_disabled, blend_mix, depth_draw_always;

uniform float sim_time = 0.0;
uniform float exhaust_gain = 0.0;
uniform float contact_gain = 0.0;
uniform vec2 wet_hull_start_m = vec2(0.0);
uniform vec2 wet_hull_end_m = vec2(0.0);
uniform float hull_radius_m = 4.5;
uniform float sea_radius_m = 6371000.0;
varying vec2 sea_m;

void vertex() {
    sea_m = VERTEX.xz * 2.8;
    // Mean surface follows local planetary curvature. Swell is normal detail only:
    // displacing trough geometry would expose the opaque geographic surface below it.
    VERTEX.y -= dot(sea_m, sea_m)/(2.0*sea_radius_m*2.8);
    NORMAL = normalize(vec3(sea_m.x/sea_radius_m, 1.0, sea_m.y/sea_radius_m));
}

void fragment() {
    float p = dot(sea_m, vec2(0.075, 0.027))-sim_time*0.8;
    float q = dot(sea_m, vec2(-0.032, 0.097))-sim_time*1.05;
    vec2 slope = vec2(0.075, 0.027)*0.56*cos(p) + vec2(-0.032, 0.097)*0.34*cos(q);
    float detail = 0.50*sin(dot(sea_m, vec2(0.61, 0.23))-sim_time*2.3)
        + 0.30*sin(dot(sea_m, vec2(-0.37, 0.79))+sim_time*1.6)
        + 0.20*sin(dot(sea_m, vec2(1.13, -0.41))-sim_time*2.9);
    float resolved = 1.0-smoothstep(0.5, 3.0, max(length(dFdx(sea_m)), length(dFdy(sea_m))));
    NORMAL_MAP = vec3(0.5-slope.x*0.9*resolved+detail*0.035*resolved, 0.5-slope.y*0.9*resolved, 1.0);
    NORMAL_MAP_DEPTH = 0.8;
    float range_m = length(sea_m);
    float spray = exhaust_gain*exp(-pow((range_m-15.0)/12.0, 2.0));
    vec2 wet_axis = wet_hull_end_m-wet_hull_start_m;
    float along_hull = clamp(dot(sea_m-wet_hull_start_m, wet_axis)/max(dot(wet_axis, wet_axis), 0.001), 0.0, 1.0);
    float hull_range = length(sea_m-mix(wet_hull_start_m, wet_hull_end_m, along_hull));
    float foam = (spray*0.55+contact_gain*exp(-pow((hull_range-hull_radius_m-1.5)/3.5, 2.0))*0.32)
        * (0.6+0.4*detail);
    ALBEDO = mix(vec3(0.025, 0.055, 0.080), vec3(0.50, 0.57, 0.61), clamp(foam, 0.0, 0.65));
    ROUGHNESS = mix(0.42, 0.70, clamp(foam, 0.0, 1.0));
    METALLIC = 0.0;
    SPECULAR = 0.4;
    EMISSION = vec3(0.025, 0.037, 0.055) + vec3(1.0, 0.28, 0.055)*spray*0.65;
    // Match the metre-scale geographic-detail depth policy. Without this decal bias,
    // the opaque mean Earth surface cuts every wave trough into a black hole.
    vec3 view_ray = normalize(VERTEX);
    vec3 world_ray = normalize((INV_VIEW_MATRIX*vec4(view_ray, 0.0)).xyz);
    float sea_distance = surface_distance_m(world_ray);
    if (sea_distance <= 0.0) discard;
    // Analytic depth avoids coarse horizon triangles falling behind the opaque globe.
    float water_depth = physical_backdrop_depth(view_ray*(max(sea_distance-2.8, 0.01)/2.8), PROJECTION_MATRIX);
    #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
    // At grazing range the metre bias can quantize to the same 24-bit depth as
    // the geographic sea. Two depth steps keep this decal ordered in reverse Z.
    water_depth += (2.0/16777216.0)*smoothstep(1000.0, 5000.0, sea_distance);
    #endif
    DEPTH = min(water_depth, 1.0);
    // Fade the grazing row where geographic ray depth becomes ill-conditioned.
    float horizon_alpha = smoothstep(0.0, 0.0015, -dot(world_ray, physical_camera_up));
    ALPHA = (1.0-smoothstep(20000.0, 25000.0, range_m))*horizon_alpha;
    if (ALPHA < 0.001) discard;
}
