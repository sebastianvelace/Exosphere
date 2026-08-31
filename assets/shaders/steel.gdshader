// Mill 304L analog for Starship / Super Heavy.
//
// Compatibility + metallic PBR ignores Environment ambient, so a low sky fill
// can turn the hull into a black cutout against the HDR sky. This shader stays
// unshaded and bakes a bounded wrap-sun + sky rim + engine bounce into ALBEDO.
shader_type spatial;
render_mode cull_back, unshaded;

uniform vec3  base_tint    : source_color = vec3(0.66, 0.66, 0.68);
uniform float metallic_val : hint_range(0.0, 1.0) = 0.22;
uniform float rough_val    : hint_range(0.0, 1.0) = 0.28;
uniform float spec_val     : hint_range(0.0, 1.0) = 0.62;

uniform float weld_spacing : hint_range(0.1, 8.0)  = 1.6;
uniform float weld_depth   : hint_range(0.0, 0.5)  = 0.14;
uniform float brush_amt    : hint_range(0.0, 0.5)  = 0.16;

uniform float soot_y0      = -1000.0;
uniform float soot_y1      = -1000.0;
uniform vec3  soot_color   : source_color = vec3(0.16, 0.15, 0.15);

uniform float fill_strength : hint_range(0.0, 0.12) = 0.028;
uniform vec3  rim_color : source_color = vec3(0.55, 0.72, 1.0);
uniform float rim_strength : hint_range(0.0, 0.45) = 0.28;
uniform float sky_bounce : hint_range(0.0, 8.0) = 0.92;

uniform vec3  emit_color   : source_color = vec3(1.0, 0.35, 0.0);
uniform float emit_strength : hint_range(0.0, 4.0) = 0.0;

uniform vec3  sun_dir = vec3(0.35, 0.78, 0.25);
uniform float engine_light : hint_range(0.0, 8.0) = 0.0;

varying vec3 v_local;

float hash11(float p) {
    p = fract(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return fract(p);
}

void vertex() {
    v_local = VERTEX;
}

void fragment() {
    float band   = v_local.y / max(weld_spacing, 0.001);
    float bandId = floor(band);
    float bandF  = fract(band);
    float groove = smoothstep(0.0, 0.06, bandF) * (1.0 - smoothstep(0.94, 1.0, bandF));
    float seam   = 1.0 - groove;
    float bandVar = hash11(bandId) - 0.5;

    float ang   = atan(v_local.z, v_local.x);
    float brush = sin(ang * 64.0) * 0.5 + 0.5;

    vec3 col = base_tint * (1.0 + bandVar * 0.06);
    col     *= 1.0 - seam * min(weld_depth, 0.10);
    // Brushed steel should vary subtly along the barrel, not change hue at
    // every weld line. The old warm/cool seam tint read as random damage.
    col     *= 0.985 + (brush - 0.5) * brush_amt * 0.06;

    float soot = 1.0 - clamp((v_local.y - soot_y0) / max(soot_y1 - soot_y0, 0.001), 0.0, 1.0);
    col = mix(col, soot_color, soot * 0.85);

    vec3 n = normalize(NORMAL);
    vec3 v = normalize(VIEW);
    float nDotV = clamp(dot(n, v), 0.0, 1.0);
    vec3 L = normalize(sun_dir);
    // Wrap Lambert: a vertical cylinder's n.y is ~0, so sky-hemi is useless
    // for barrel form. Sun wrap + view rim give the terminator.
    float nDotL = dot(n, L);
    float wrap = clamp((nDotL + 0.42) / 1.42, 0.0, 1.0);
    float rim = pow(1.0 - nDotV, 2.15) * rim_strength;

    float sky = mix(0.88, 1.10, clamp(sky_bounce / 1.10, 0.0, 1.0));
    vec3 lit = col * mix(0.72, 1.16, wrap) * sky;
    lit += rim_color * rim * 0.72;
    lit += col * fill_strength;
    lit += vec3(1.0, 0.52, 0.22) * engine_light * (1.0 - wrap) * 0.28;
    if (emit_strength > 0.001) {
        lit += emit_color * emit_strength;
    }

    ALBEDO = lit;
    ALPHA = 1.0;
}
