// Black heat-shield tiles for the Starship windward surface.
//
// The surface stays in Godot's opaque PBR path so sunlight, Earth bounce and
// self-shadowing shape the vehicle. Tiles are hexagons laid out in METRES on the
// mesh (cylindrical arc length × height for hull and nose, the local XY plane for
// flaps), at the real ~0.36 m pitch. Earlier versions scaled a rectangular grid by
// each mesh's 0..1 UVs, which turned a 30 m band into 1.0 × 1.75 m "bricks".
// Tile joints use derivative filtering and fade to the mean tile tone once a joint
// is narrower than a pixel, so a distant ship reads as matte black with a faint
// mottling instead of a moiré grid.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3  albedo_color : source_color = vec3(0.070, 0.068, 0.072);
uniform float roughness_val : hint_range(0.0, 1.0) = 0.92;
// Compatibility uniform retained for existing material setup code; the metric
// grid below uses tile_pitch instead.
uniform float hex_scale = 11.0;
// Joint half-width as a fraction of the tile apothem.
uniform float gap_width : hint_range(0.0, 0.2) = 0.055;
// Flat-to-flat tile size in render units (1 u = 2.8 m): 0.36 m tiles.
uniform float tile_pitch = 0.1286;
// 0 = cylindrical (hull, nose), 1 = planar local XY (flaps).
uniform int   tile_mapping = 0;
uniform float char_amt : hint_range(0.0, 1.0) = 0.0;
uniform vec3  char_color : source_color = vec3(0.085, 0.050, 0.038);
uniform vec3  rim_color : source_color = vec3(0.45, 0.62, 0.92);
uniform float rim_strength : hint_range(0.0, 0.4) = 0.12;
uniform float sky_bounce : hint_range(0.0, 8.0) = 1.15;
uniform vec3  emit_color : source_color = vec3(1.0, 0.28, 0.05);
uniform float emit_strength : hint_range(0.0, 4.0) = 0.0;
uniform vec3  sun_dir = vec3(0.35, 0.78, 0.25);

varying vec3 v_local;

void vertex() {
    v_local = VERTEX;
}

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Nearest hexagon (pointy-top lattice) to p, in units of the flat-to-flat size.
// Returns the offset from its centre (xy) and a stable id (zw).
vec4 hex_cell(vec2 p) {
    const vec2 s = vec2(1.0, 1.7320508);
    vec4 centres = floor(vec4(p, p - vec2(0.5, 1.0)) / s.xyxy) + 0.5;
    vec4 offsets = vec4(p - centres.xy * s, p - (centres.zw + 0.5) * s);
    return dot(offsets.xy, offsets.xy) < dot(offsets.zw, offsets.zw)
        ? vec4(offsets.xy, centres.xy)
        : vec4(offsets.zw, centres.zw + 0.5);
}

void fragment() {
    vec2 metric;
    if (tile_mapping == 1) {
        metric = v_local.xy;
    } else {
        float radius = length(v_local.xz);
        metric = vec2(atan(v_local.z, v_local.x) * radius, v_local.y);
    }
    vec4 hex = hex_cell(metric / max(tile_pitch, 1e-4));
    vec2 q = abs(hex.xy);
    // Distance from the hexagon edge (0.5 = apothem at the centre).
    float edge_distance = 0.5 - max(dot(q, normalize(vec2(1.0, 1.7320508))), q.x);
    float filter_width = max(fwidth(edge_distance) * 1.35, 0.0015);
    float seam = 1.0 - smoothstep(gap_width - filter_width,
        gap_width + filter_width, edge_distance);
    // Once a joint is thinner than about a pixel it cannot be resolved: blend it
    // toward its area-weighted mean so the far field stays a uniform matte.
    float resolvable = 1.0 - smoothstep(gap_width * 0.6, gap_width * 2.0, filter_width);
    float seam_mean = gap_width * 2.3;
    seam = mix(seam_mean, seam, resolvable);

    // Real TPS is not a perfectly uniform black: tiles differ slightly in tone and
    // a few replacements read lighter. Variation survives at distance as mottling.
    float h = hash21(hex.zw);
    float tile_variation = (h - 0.5) * 0.09;
    float replaced = step(0.985, hash21(hex.zw + 17.0)) * 0.35;
    vec3 tile = mix(albedo_color, char_color, char_amt);
    tile *= 1.0 + tile_variation + replaced;
    tile = mix(tile, vec3(0.007, 0.007, 0.009), seam * 0.78);

    vec3 n = normalize(NORMAL);
    vec3 v = normalize(VIEW);
    float nDotV = clamp(dot(n, v), 0.0, 1.0);
    float rim = pow(1.0 - nDotV, 2.2) * rim_strength;

    // A tiny bounded floor keeps the black TPS legible against space. It is
    // intentionally far below direct lighting and never replaces scene light.
    float sky_floor = clamp(sky_bounce / 1.15, 0.0, 1.0);
    // Compatibility/llvmpipe can lose the PBR environment contribution on the
    // shadow side of the ship. Keep a bounded cool-sky fill tied to view-facing
    // surface area so TPS remains a textured black surface instead of a flat
    // silhouette; direct sunlight still owns the dominant response.
    vec3 sky_tint = vec3(0.72, 0.82, 1.0);
    float view_fill = mix(0.28, 1.0, nDotV) * sky_floor;
    vec3 emission = tile * mix(0.012, 0.026, sky_floor);
    emission += sky_tint * tile * view_fill * 0.30;
    emission += rim_color * rim * 0.035;
    emission += emit_color * emit_strength;

    ALBEDO = tile;
    METALLIC = 0.0;
    ROUGHNESS = roughness_val;
    SPECULAR = 0.18;
    EMISSION = emission;
}
