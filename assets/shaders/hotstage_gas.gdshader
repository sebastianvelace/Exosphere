shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back;

uniform float opacity : hint_range(0.0, 0.22) = 0.0;
uniform float age : hint_range(0.0, 1.0) = 0.0;
uniform float seed = 0.0;
varying vec3 local_position;

void vertex() {
    local_position = VERTEX;
}

void fragment() {
    // Fade the volume silhouette independently of camera direction.
    float facing = max(dot(normalize(NORMAL), normalize(VIEW)), 0.0);
    float soft_edge = smoothstep(0.0, 0.65, facing);
    vec3 p = local_position * 5.0;
    float wisps = 0.55 + 0.20 * sin(p.x + seed + age * 3.0)
        * sin(p.y * 1.7 - age * 4.0) * sin(p.z * 1.3 + seed);
    ALBEDO = mix(vec3(0.78, 0.76, 0.71), vec3(0.48, 0.51, 0.54), age);
    ALPHA = opacity * soft_edge * wisps;
}
