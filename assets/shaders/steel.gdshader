// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Stainless-steel hull shader for Starship / Super Heavy.
//
// PBR bare 304L stainless: high metallic, medium-varied roughness, a faint
// horizontal weld-ring band pattern, a vertical brushed/anisotropic streak, and
// a height-driven soot/heat tint near the engines. Fully lit (picks up the
// scene's directional sun + environment reflection), so it reads as real metal.
//
//   base_tint     : overall steel albedo (Starship brighter, SH duller/warmer)
//   metallic_val  : 0..1 metalness
//   rough_val     : base roughness; modulated up/down by the band noise
//   weld_spacing  : world-units between horizontal weld rings
//   soot_y0/soot_y1 : local-Y band over which soot fades in (engine end)
//   soot_color    : exhaust-staining color blended in near the engines
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3  base_tint    : source_color = vec3(0.85, 0.85, 0.87);
uniform float metallic_val : hint_range(0.0, 1.0) = 0.92;
uniform float rough_val    : hint_range(0.0, 1.0) = 0.20;
uniform float spec_val     : hint_range(0.0, 1.0) = 0.55;

uniform float weld_spacing : hint_range(0.1, 8.0)  = 1.6;
uniform float weld_depth   : hint_range(0.0, 0.5)  = 0.10;
uniform float brush_amt    : hint_range(0.0, 0.5)  = 0.10;

uniform float soot_y0      = -1000.0;   // below: clean steel
uniform float soot_y1      = -1000.0;   // above this toward engines: full soot
uniform vec3  soot_color   : source_color = vec3(0.16, 0.15, 0.15);

// Emission for reentry heat glow (driven from code by raising emit_strength).
uniform vec3  emit_color   : source_color = vec3(1.0, 0.35, 0.0);
uniform float emit_strength : hint_range(0.0, 4.0) = 0.0;

varying vec3 v_local;

// cheap hash noise for subtle per-band variation
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
    // ── Horizontal weld-ring banding ──────────────────────────────────────
    // A repeating soft groove every `weld_spacing` units in local Y, plus a
    // very slight per-band roughness/brightness variation so no two barrel
    // bands read identically (real stacked-ring stainless).
    float band   = v_local.y / max(weld_spacing, 0.001);
    float bandId = floor(band);
    float bandF  = fract(band);

    // narrow groove centred on each integer band boundary
    float groove = smoothstep(0.0, 0.06, bandF) * (1.0 - smoothstep(0.94, 1.0, bandF));
    float seam   = 1.0 - groove;                 // 1 at the weld line, 0 mid-band

    float bandVar = hash11(bandId) - 0.5;        // -0.5..0.5 per band

    // ── Vertical brushed / anisotropic streaking ──────────────────────────
    // Derived from the local azimuth so highlights smear vertically like
    // brushed stainless. Cheap: just modulate roughness by an angular ripple.
    float ang     = atan(v_local.z, v_local.x);
    float brush   = sin(ang * 64.0) * 0.5 + 0.5;

    // ── Base colour ───────────────────────────────────────────────────────
    vec3 col = base_tint * (1.0 + bandVar * 0.06);
    col     *= 1.0 - seam * weld_depth;          // darken the weld grooves

    // ── Soot / heat tint near the engines ─────────────────────────────────
    float soot = clamp((v_local.y - soot_y0) / max(soot_y1 - soot_y0, 0.001), 0.0, 1.0);
    soot = 1.0 - soot;                           // 1 near engine end, 0 above
    soot = clamp(soot, 0.0, 1.0);
    col = mix(col, soot_color, soot * 0.85);

    ALBEDO   = col;
    METALLIC = metallic_val * (1.0 - soot * 0.45);   // soot is less metallic
    SPECULAR = spec_val;

    // Roughness: base + band variation + brushed streak + soot roughening.
    float rough = rough_val + bandVar * 0.05 + seam * 0.10
                + brush * brush_amt + soot * 0.35;
    ROUGHNESS = clamp(rough, 0.04, 1.0);

    if (emit_strength > 0.001) {
        EMISSION = emit_color * emit_strength;
    }
}
