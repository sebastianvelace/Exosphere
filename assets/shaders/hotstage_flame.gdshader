// Luminous Ship exhaust venting radially through the hot-stage ring while both stages are
// attached. Presentation proxy driven by delivered Ship thrust; not a gas-dynamics solve.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float seed = 0.0;
uniform float simulation_time = 0.0;
varying vec3 local_position;

void vertex() {
    local_position = VERTEX;
}

void fragment() {
    // Unit sphere stretched along local +X (outward from the vent). Hottest at the vent
    // root, cooling toward the tip; soft silhouette so no hard flame card appears.
    float along = clamp(local_position.x * 0.5 + 0.5, 0.0, 1.0);
    float facing = max(dot(normalize(NORMAL), normalize(VIEW)), 0.0);
    float soft_edge = smoothstep(0.05, 0.75, facing);
    float flicker = 0.85 + 0.15 * sin(simulation_time * 37.0 + seed * 5.3)
        * sin(local_position.y * 9.0 + simulation_time * 23.0);
    vec3 hot = vec3(1.0, 0.93, 0.78);
    vec3 warm = vec3(1.0, 0.52, 0.16);
    vec3 col = mix(hot, warm, smoothstep(0.15, 0.85, along));
    float falloff = 1.0 - smoothstep(0.35, 1.0, along);
    ALBEDO = col * 2.2;
    EMISSION = vec3(0.0);
    ALPHA = intensity * soft_edge * falloff * flicker;
}
