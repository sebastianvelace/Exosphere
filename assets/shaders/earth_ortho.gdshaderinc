// Shared georeferenced coverage, independent of camera altitude and atmosphere.
uniform sampler2D regional_ortho_tex : source_color, filter_linear_mipmap;
uniform sampler2D regional_mask_tex : hint_default_white, filter_linear_mipmap;
uniform float regional_extent_m = 10000.0; // legacy fallback for other sites
uniform vec2 regional_size_m = vec2(10000.0);
uniform sampler2D macro_ortho_tex : source_color, filter_linear_mipmap;
uniform sampler2D macro_mask_tex : hint_default_black, filter_linear_mipmap;
uniform float macro_extent_m = 50000.0; // legacy fallback for other sites
uniform vec2 macro_size_m = vec2(50000.0);
uniform vec2 site_patch_offset_m = vec2(0.0);
uniform vec2 site_raster_offset_m = vec2(0.0);
uniform bool precise_coverage_masks = false;
uniform bool context_ortho_enabled = false;
uniform sampler2D context_ortho_tex : source_color, filter_linear_mipmap;
uniform vec2 context_size_m = vec2(333000.0);

vec2 raster_uv(vec2 patch_m, vec2 size_m) {
    vec2 offset = patch_m + site_patch_offset_m + site_raster_offset_m;
    return vec2(0.5 + offset.x / size_m.x, 0.5 - offset.y / size_m.y);
}

// Only the actual rectangle boundary retires the raster. Never discard valid
// geography to hide the tangent mesh; geographic coverage and mesh LOD differ.
float raster_edge_weight(vec2 uv) {
    if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return 0.0;
    vec2 edge = min(uv, vec2(1.0) - uv);
    return smoothstep(0.0, 0.015, min(edge.x, edge.y));
}

vec2 regional_uv(vec2 patch_m) {
    return raster_uv(patch_m, precise_coverage_masks ? regional_size_m : vec2(regional_extent_m));
}
float regional_no_data_factor(vec2 uv) {
	// The downloaded NAIP tile is JPEG, so its provider no-data area is an
	// opaque neutral olive instead of alpha. The separate mask is only a broad
	// feather and cannot describe the irregular tile footprint on its own.
	// Reject a locally flat neutral plateau while preserving real water/land
	// pixels and the narrow transition at the data boundary.
	const vec2 texel = vec2(1.0 / 4096.0);
	vec3 centre = textureLod(regional_ortho_tex, uv, 0.0).rgb;
	vec3 east = textureLod(regional_ortho_tex,
		clamp(uv + vec2(texel.x, 0.0), vec2(0.0), vec2(1.0)), 0.0).rgb;
	vec3 north = textureLod(regional_ortho_tex,
		clamp(uv + vec2(0.0, texel.y), vec2(0.0), vec2(1.0)), 0.0).rgb;
	float neutral = 1.0 - smoothstep(0.06, 0.18,
		length(centre - vec3(0.54, 0.54, 0.45)));
	float local_variation = max(length(east - centre), length(north - centre));
	float flat_fill = 1.0 - smoothstep(0.004, 0.025, local_variation);
	return 1.0 - neutral * flat_fill;
}

float legacy_radial_weight(vec2 uv) {
    return 1.0 - smoothstep(0.84, 1.0, length(uv - vec2(0.5)) * 2.0);
}
float regional_validity(vec2 uv) {
    float mask = texture(regional_mask_tex, uv).r;
    return precise_coverage_masks ? raster_edge_weight(uv) * mask
        : raster_edge_weight(uv) * legacy_radial_weight(uv)
            * smoothstep(0.62, 0.80, mask) * regional_no_data_factor(uv);
}
vec2 macro_uv(vec2 patch_m) {
    return raster_uv(patch_m, precise_coverage_masks ? macro_size_m : vec2(macro_extent_m));
}
float macro_no_data_factor(vec2 uv) {
	// The 50 km composite contains large opaque JPEG voids outside the
	// available NAIP swaths. Detect their neutral, locally flat fill before it
	// can be mistaken for real Gulf water or wetland in the LOD bridge.
	const vec2 texel = vec2(1.0 / 4096.0);
	vec3 centre = textureLod(macro_ortho_tex, uv, 0.0).rgb;
	vec3 east = textureLod(macro_ortho_tex,
		clamp(uv + vec2(texel.x, 0.0), vec2(0.0), vec2(1.0)), 0.0).rgb;
	vec3 north = textureLod(macro_ortho_tex,
		clamp(uv + vec2(0.0, texel.y), vec2(0.0), vec2(1.0)), 0.0).rgb;
	float neutral = 1.0 - smoothstep(0.06, 0.18,
		length(centre - vec3(0.54, 0.54, 0.45)));
	float local_variation = max(length(east - centre), length(north - centre));
	float flat_fill = 1.0 - smoothstep(0.004, 0.025, local_variation);
	return 1.0 - neutral * flat_fill;
}

float macro_validity(vec2 uv) {
    float mask = texture(macro_mask_tex, uv).r;
    return precise_coverage_masks ? raster_edge_weight(uv) * mask
        : raster_edge_weight(uv) * legacy_radial_weight(uv)
            * smoothstep(0.62, 0.80, mask) * macro_no_data_factor(uv);
}
vec3 context_surface(vec3 fallback, vec2 patch_m) {
    if (!context_ortho_enabled) return fallback;
    vec2 uv = raster_uv(patch_m, context_size_m);
    return mix(fallback, texture(context_ortho_tex, uv).rgb,
        smoothstep(0.0, 0.15, min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y))));
}
