// Optical proxy for condensed deluge water, not a fluid or combustion solver.
// Integrate extinction through a noisy ellipsoid in metres, in both renderers.
shader_type spatial;
render_mode unshaded, blend_mix, cull_front, depth_draw_never, depth_test_disabled, shadows_disabled;

uniform sampler3D density_noise : filter_linear, repeat_enable;
uniform sampler2D scene_depth : hint_depth_texture, repeat_disable, filter_nearest;
uniform vec3 size_m = vec3(100.0);
uniform vec3 sun_local = vec3(0.0, 1.0, 0.0);
uniform vec3 fire_local = vec3(0.0);
uniform float solar_visibility = 1.0;
uniform float source_power = 0.0;
uniform float opacity = 0.0;
uniform float age = 0.0;
uniform float seed = 0.0;
varying vec3 local_eye;
varying vec3 local_surface;

void vertex() {
    local_eye = (inverse(MODEL_MATRIX) * INV_VIEW_MATRIX[3]).xyz;
    local_surface = VERTEX;
}

float density(vec3 p) {
    vec3 q = p * 2.0;
    float r2 = dot(q, q);
    float n = texture(density_noise, p * 1.2 + vec3(seed, age * -0.012, seed * 0.31)).r;
    float rim = 1.0 - smoothstep(0.48, 1.0, r2 + (0.5 - n) * 0.72);
    return rim * smoothstep(0.22, 0.65, n);
}

void fragment() {
    vec3 ray = normalize(local_surface - local_eye);
    vec3 inv_ray = 1.0 / (ray + vec3(0.000001));
    vec3 ta = (-vec3(0.5) - local_eye) * inv_ray;
    vec3 tb = (vec3(0.5) - local_eye) * inv_ray;
    vec3 lo = min(ta, tb);
    vec3 hi = max(ta, tb);
    float start = max(max(lo.x, lo.y), max(lo.z, 0.0));
    float end = min(min(hi.x, hi.y), hi.z);
    // Proxy back faces can lie below grade or behind the hull. Clip the actual
    // volume integral at opaque depth instead of depth-testing the proxy surface.
    float z = texture(scene_depth, SCREEN_UV).r;
    // Reverse-Z clear depth is background, not an occluder. With a planetary
    // far plane its reconstructed homogeneous w can round to zero.
    if (z > 0.0000001) {
        vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, z);
        #if CURRENT_RENDERER == RENDERER_COMPATIBILITY
            ndc.z = z * 2.0 - 1.0;
        #endif
        vec4 view_point = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
        if (abs(view_point.w) > 0.0000000001) {
            view_point /= view_point.w;
            vec3 local_opaque = (inverse(MODEL_MATRIX) * INV_VIEW_MATRIX * view_point).xyz;
            end = min(end, dot(local_opaque - local_eye, ray));
        }
    }
    if (end <= start || opacity < 0.001) { discard; }
    float step_size = (end - start) / 20.0;
    float step_m = length(ray * size_m) * step_size;
    vec3 sun_step = normalize(sun_local / size_m) * 0.13;
    float sun_step_m = length(sun_step * size_m);
    float transmittance = 1.0;
    vec3 radiance = vec3(0.0);
    for (int i = 0; i < 20; i++) {
        vec3 p = local_eye + ray * (start + (float(i) + 0.5) * step_size);
        float rho = density(p);
        float alpha = 1.0 - exp(-rho * opacity * step_m * 0.085);
        if (alpha > 0.001) {
            float sun_depth = density(p + sun_step) + density(p + sun_step * 2.0)
                + density(p + sun_step * 3.0);
            float sunlight = exp(-sun_depth * sun_step_m * opacity * 0.085);
            // Cool ambient fill and attenuated direct light give shaded white water.
            vec3 light = vec3(0.16, 0.20, 0.27)
                + vec3(0.95, 0.93, 0.88) * sunlight * solar_visibility;
            vec3 fire_distance = (p - fire_local) * size_m;
            float fire = source_power * 800.0 / (800.0 + dot(fire_distance, fire_distance));
            light += vec3(3.6, 1.7, 0.12) * fire;
            radiance += transmittance * alpha * light;
            transmittance *= 1.0 - alpha;
        }
        if (transmittance < 0.015) { break; }
    }
    float a = 1.0 - transmittance;
    if (a < 0.008) { discard; }
    ALBEDO = radiance / max(a, 0.001);
    ALPHA = a;
}
