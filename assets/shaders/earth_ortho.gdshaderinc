// Shared site imagery, masks and feathering for local and global Earth surfaces.
uniform sampler2D regional_ortho_tex : source_color, filter_linear_mipmap;
uniform sampler2D regional_mask_tex : hint_default_white, filter_linear;
uniform float regional_extent_m = 10000.0;
uniform sampler2D macro_ortho_tex : source_color, filter_linear_mipmap;
uniform sampler2D macro_mask_tex : hint_default_black, filter_linear;
uniform float macro_extent_m = 50000.0;
uniform vec2 site_patch_offset_m = vec2(0.0);

// Mesh +X is geodetic east and +Z is geodetic north. Imported rasters use
// the conventional top-to-bottom image origin, so north maps toward V=0.
vec2 regional_uv(vec2 patch_m) {
    patch_m += site_patch_offset_m;
	return vec2(0.5 + patch_m.x / regional_extent_m,
		0.5 - patch_m.y / regional_extent_m);
}

float regional_edge_weight(vec2 uv) {
	// The raster is square, but its handoff must follow the circular tangent
	// patch. A max-component distance leaves a visible rectangular cookie over
	// the coast when the regional layer retires in the mid-altitude view.
	float radial_distance = length(uv - vec2(0.5)) * 2.0;
	return 1.0 - smoothstep(0.84, 1.0, radial_distance);
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

float regional_validity(vec2 uv) {
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
		return 0.0;
	return regional_edge_weight(uv)
		// The exported mask is Gaussian-feathered around the provider footprint.
		// Keep its low-valued tail invalid so the neutral JPEG fill cannot leak
		// back into the scene as a rectangular patch.
		* smoothstep(0.62, 0.80, texture(regional_mask_tex, uv).r)
		* regional_no_data_factor(uv);
}

vec2 macro_uv(vec2 patch_m) {
    patch_m += site_patch_offset_m;
	return vec2(0.5 + patch_m.x / macro_extent_m,
		0.5 - patch_m.y / macro_extent_m);
}

float macro_edge_weight(vec2 uv) {
	// Keep the broad measured layer circular as well. Otherwise the 50 km
	// bridge reintroduces the same square silhouette after the 10 km layer fades.
	float radial_distance = length(uv - vec2(0.5)) * 2.0;
	return 1.0 - smoothstep(0.84, 1.0, radial_distance);
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
	if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
		return 0.0;
	return macro_edge_weight(uv)
		// The macro provider footprint is irregular; reject its feather tail
		// before sampling the neutral fill colour stored in the JPEG void.
		* smoothstep(0.62, 0.80, texture(macro_mask_tex, uv).r)
		* macro_no_data_factor(uv);
}
