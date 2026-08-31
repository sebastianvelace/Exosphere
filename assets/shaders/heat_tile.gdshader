// Black heat-shield tiles for the Starship windward surface.
//
// Tile separation is real geometry in VesselRenderer (staves plus raised seam
// boxes). A second high-frequency fragment grid aliases into black/white noise
// at game distance, especially on small flap and nose boxes. This shader keeps
// the material stable and matte; the geometry carries the panel detail.
shader_type spatial;
render_mode cull_back, unshaded;

uniform vec3  albedo_color : source_color = vec3(0.070, 0.068, 0.072);
uniform float roughness_val : hint_range(0.0, 1.0) = 0.92;
// Compatibility uniforms retained for existing material setup code. They are
// intentionally not used to create a second procedural grid.
uniform float hex_scale = 11.0;
uniform float gap_width : hint_range(0.0, 0.2) = 0.045;
uniform float char_amt : hint_range(0.0, 1.0) = 0.0;
uniform vec3  char_color : source_color = vec3(0.085, 0.050, 0.038);
uniform vec3  rim_color : source_color = vec3(0.45, 0.62, 0.92);
uniform float rim_strength : hint_range(0.0, 0.4) = 0.12;
uniform float sky_bounce : hint_range(0.0, 8.0) = 1.15;
uniform vec3  emit_color : source_color = vec3(1.0, 0.28, 0.05);
uniform float emit_strength : hint_range(0.0, 4.0) = 0.0;
uniform vec3  sun_dir = vec3(0.35, 0.78, 0.25);

void fragment() {
    vec3 tile = mix(albedo_color, char_color, char_amt);
    vec3 n = normalize(NORMAL);
    vec3 v = normalize(VIEW);
    float nDotV = clamp(dot(n, v), 0.0, 1.0);
    float wrap = clamp((dot(n, normalize(sun_dir)) + 0.38) / 1.38, 0.0, 1.0);
    float rim = pow(1.0 - nDotV, 2.2) * rim_strength;
    // Keep the manual fill display-referred and bounded. The previous 3.2–8.5
    // multiplier made small box faces alternate between clipped white and black.
    float sky = mix(0.88, 1.08, clamp(sky_bounce / 1.15, 0.0, 1.0));
    vec3 lit = tile * mix(0.76, 1.10, wrap) * sky;
    lit += rim_color * rim * 0.70;
    lit += emit_color * emit_strength;
    ALBEDO = lit;
    ALPHA = 1.0;
}
