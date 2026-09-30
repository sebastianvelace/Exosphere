// Optical plasma proxy driven by measured convective flux, not a gas-temperature solver.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform float heat_level : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D noise_tex;
uniform float halo_size : hint_range(0.0, 0.5) = 0.08; // render units
uniform float simulation_time = 0.0;
uniform int effect_kind = 0; // bow cap / downstream wake / surface edge
uniform float opacity_gain = 1.0;

void vertex() {
    VERTEX += NORMAL * halo_size * heat_level * heat_level;
}

void fragment() {
    // Advect both scales downstream. The clock comes from committed simulation time,
    // so pause and time warp retain the relationship between heating and optical flow.
    float n1 = texture(noise_tex, UV * vec2(3.0, 1.0)
        + vec2(0.0, -simulation_time * 0.24)).r;
    float n2 = texture(noise_tex, UV * vec2(7.0, 2.3)
        + vec2(simulation_time * 0.04, -simulation_time * 0.51)).r;
    float turbulence = 0.72 + 0.40 * (0.65 * n1 + 0.35 * n2);
    float t = clamp(heat_level * turbulence, 0.0, 1.0);
    const float PLASMA_ONSET  = 0.08;
    float onset = smoothstep(PLASMA_ONSET, 0.26, t);
    // Line emission has a different spectrum from the incandescent TPS. This
    // calibrated orange/rose sheath is not a blackbody temperature measurement.
    vec3 warm = mix(vec3(0.85, 0.16, 0.025), vec3(1.0, 0.48, 0.13),
        smoothstep(0.15, 0.75, t));
    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.6);
    vec3 col = mix(warm, vec3(1.0, 0.34, 0.22), rim * 0.16);
    col = mix(col, vec3(1.0, 0.86, 0.62), smoothstep(0.82, 1.0, t) * 0.45);
    float mask = 1.0;
    float gain = 1.0;
    if (effect_kind == 0) {
        // UV.y=0 is the windward pole. Feather the trailing half of the cap.
        mask = 1.0 - smoothstep(0.20, 0.66, UV.y);
        float filaments = smoothstep(0.28, 0.76, n1 * 0.65 + n2 * 0.35);
        mask *= mix(0.12, 0.82, rim) * mix(0.22, 1.0, filaments);
        gain = 12.0;
    } else if (effect_kind == 1) {
        // The open cone tapers toward +Y, downstream of the hull.
        float downstream = 1.0 - UV.y;
        mask = smoothstep(0.0, 0.10, UV.y) * smoothstep(0.0, 0.12, downstream);
        // Suppress the cone silhouette: the downstream volume must read as a
        // fading optical trail, rather than a rigid transparent shell.
        mask *= exp(-downstream * 3.5) * mix(0.50, 0.08, rim);
        mask *= smoothstep(0.18, 0.75, n1 * 0.65 + n2 * 0.35);
        gain = 6.0;
    } else {
        mask = mix(0.22, 0.8, rim);
        gain = 1.6;
    }
    ALBEDO = col * gain;
    // Compatibility renders unshaded radiance from ALBEDO and ignores EMISSION.
    // Supply it once here so both renderers display the same optical source.
    EMISSION = vec3(0.0);
    ALPHA = t <= PLASMA_ONSET ? 0.0 : onset * mask * opacity_gain * 0.65;
}
