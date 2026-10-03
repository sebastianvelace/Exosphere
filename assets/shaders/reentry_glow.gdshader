// Optical plasma proxy driven by measured convective flux, not a gas-temperature solver.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled;

uniform float heat_level : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D noise_tex;
uniform float halo_size : hint_range(0.0, 0.5) = 0.08; // render units
uniform float simulation_time = 0.0;
uniform int effect_kind = 0; // bow cap / downstream wake / surface edge / conforming sheath
uniform float opacity_gain = 1.0;
uniform vec3 flow_dir = vec3(-1.0, 0.0, 0.0);
varying vec3 surface_normal;

void vertex() {
    surface_normal = NORMAL;
    float expansion = effect_kind == 4 ? sqrt(heat_level) : heat_level * heat_level;
    VERTEX += NORMAL * halo_size * expansion;
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
    // Flight 14 onboard frames show violet/rose line emission around a white
    // luminous core. Camera exposure and gas spectrum cannot be recovered from
    // these colours; this palette is an optical reference calibration only.
    vec3 rose = mix(vec3(0.30, 0.10, 0.95), vec3(1.0, 0.32, 0.62), smoothstep(0.12, 0.75, t));
    vec3 col = mix(rose, vec3(1.0, 0.89, 0.96), smoothstep(0.20, 0.55, t) * (0.20 + 0.80 * rim));
    if (effect_kind == 0) col = warm;
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
    } else if (effect_kind == 4) {
        float incidence = dot(normalize(surface_normal), normalize(flow_dir));
        mask = smoothstep(-0.18, 0.55, incidence) * pow(rim, 0.65);
        // Fade at the outer silhouette rather than drawing a neon contour.
        mask *= 1.0 - pow(rim, 12.0);
        mask *= smoothstep(0.16, 0.73, n1 * 0.65 + n2 * 0.35);
        mask *= smoothstep(0.0, 0.035, UV.y);
        col = mix(vec3(0.32, 0.14, 0.85), col, 0.30);
        gain = 5.0;
    } else if (effect_kind == 3) {
        float incidence = dot(normalize(surface_normal), normalize(flow_dir));
        float windward = smoothstep(-0.08, 0.62, incidence);
        // Keep the shield readable through the sheath; radiance concentrates
        // at grazing optical paths and moving strands, not an opaque flat cap.
        float strands = smoothstep(0.23, 0.78, 0.65 * n1 + 0.35 * n2);
        mask = windward * mix(0.065, 0.85, rim) * mix(0.18, 1.0, strands);
        mask *= smoothstep(0.0, 0.035, UV.y);
        gain = 7.0;
    } else {
        float incidence = dot(normalize(surface_normal), normalize(flow_dir));
        mask = smoothstep(-0.12, 0.55, incidence) * mix(0.04, 0.72, rim);
        mask *= smoothstep(0.20, 0.72, n1 * 0.65 + n2 * 0.35);
        gain = 5.0;
    }
    ALBEDO = col * gain;
    // Compatibility renders unshaded radiance from ALBEDO and ignores EMISSION.
    // Supply it once here so both renderers display the same optical source.
    EMISSION = vec3(0.0);
    ALPHA = t <= PLASMA_ONSET ? 0.0 : onset * mask * opacity_gain * 0.65;
}
