// Black heat-shield tiles for the Starship windward surface.
//
// The surface stays in Godot's opaque PBR path so sunlight, Earth bounce and
// self-shadowing shape the vehicle. Tile joints use derivative filtering; this
// keeps them visible in close views without turning into white/black noise when
// the ship occupies only a few dozen pixels.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3  albedo_color : source_color = vec3(0.070, 0.068, 0.072);
uniform float roughness_val : hint_range(0.0, 1.0) = 0.92;
// Compatibility uniforms retained for existing material setup code. They are
// intentionally not used to create a second procedural grid.
uniform float hex_scale = 11.0;
uniform float gap_width : hint_range(0.0, 0.2) = 0.055;
uniform float char_amt : hint_range(0.0, 1.0) = 0.0;
uniform vec3  char_color : source_color = vec3(0.085, 0.050, 0.038);
uniform vec3  rim_color : source_color = vec3(0.45, 0.62, 0.92);
uniform float rim_strength : hint_range(0.0, 0.4) = 0.12;
uniform float sky_bounce : hint_range(0.0, 8.0) = 1.15;
uniform vec3  emit_color : source_color = vec3(1.0, 0.28, 0.05);
uniform float emit_strength : hint_range(0.0, 4.0) = 0.0;
uniform vec3  sun_dir = vec3(0.35, 0.78, 0.25);

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

void fragment() {
    vec2 grid = UV * vec2(hex_scale, hex_scale * 1.22);
    vec2 tile_id = floor(grid);
    grid.x += mod(tile_id.y, 2.0) * 0.5;
    tile_id = floor(grid);
    vec2 cell = fract(grid);
    float edge_distance = min(min(cell.x, 1.0 - cell.x), min(cell.y, 1.0 - cell.y));
    float filter_width = max(fwidth(edge_distance) * 1.35, 0.0015);
    float seam = 1.0 - smoothstep(gap_width - filter_width,
        gap_width + filter_width, edge_distance);

    float tile_variation = (hash21(tile_id) - 0.5) * 0.055;
    vec3 tile = mix(albedo_color, char_color, char_amt);
    tile *= 1.0 + tile_variation;
    tile = mix(tile, vec3(0.007, 0.007, 0.009), seam * 0.78);

    vec3 n = normalize(NORMAL);
    vec3 v = normalize(VIEW);
    float nDotV = clamp(dot(n, v), 0.0, 1.0);
    float rim = pow(1.0 - nDotV, 2.2) * rim_strength;

    // A tiny bounded floor keeps the black TPS legible against space. It is
    // intentionally far below direct lighting and never replaces scene light.
    float sky_floor = clamp(sky_bounce / 1.15, 0.0, 1.0);
    vec3 emission = tile * mix(0.012, 0.026, sky_floor);
    emission += rim_color * rim * 0.035;
    emission += emit_color * emit_strength;

    ALBEDO = tile;
    METALLIC = 0.0;
    ROUGHNESS = roughness_val;
    SPECULAR = 0.18;
    EMISSION = emission;
}
