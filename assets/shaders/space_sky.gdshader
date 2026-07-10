// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Combined atmosphere + deep-space sky.
//
// One sky material that cross-fades, by altitude (`space_blend`: 0 = ground,
// 1 = space), between:
//   • a procedural-style blue atmosphere gradient (zenith → horizon → ground), and
//   • a real equirectangular Milky-Way / star panorama for deep space,
// The physical Sun is rendered as a correctly scaled celestial body; this shader does
// not add a second decorative Sun or lens halo.
//
// Driven entirely by SkyController (colours, energy, space_blend, sun_dir) so the
// same altitude bands that used to fade to pure black now fade to a living star sky.
// ─────────────────────────────────────────────────────────────────────────────
shader_type sky;

// Star panorama (8K equirectangular). Loaded at runtime by SkyController.
uniform sampler2D star_tex : source_color, filter_linear_mipmap;

// Atmosphere gradient colours (fed per-body by SkyController).
uniform vec3 sky_top_color     : source_color = vec3(0.06, 0.22, 0.72);
uniform vec3 sky_horizon_color : source_color = vec3(0.40, 0.65, 1.00);
uniform vec3 ground_horizon    : source_color = vec3(0.30, 0.55, 0.90);
uniform vec3 ground_bottom     : source_color = vec3(0.18, 0.30, 0.50);

// Overall energy of the blue atmosphere (driven to ~0 in space).
uniform float atmo_energy : hint_range(0.0, 2.0) = 1.0;

// 0 = ground (blue sky owns the dome), 1 = space (stars own the dome).
uniform float space_blend : hint_range(0.0, 1.0) = 0.0;

// Brightness of the star panorama (kept low so daytime ground sky isn't speckled).
uniform float star_energy : hint_range(0.0, 4.0) = 1.0;

// Equirectangular lookup for the star panorama from a world direction.
vec2 dir_to_equirect(vec3 d) {
	float lon = atan(d.z, d.x);
	float lat = asin(clamp(d.y, -1.0, 1.0));
	return vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI);
}

void sky() {
	vec3 dir = normalize(EYEDIR);

	// ── Atmosphere gradient (procedural-sky look) ───────────────────────────
	// Up hemisphere: horizon → zenith. Down hemisphere: ground horizon → bottom.
	float up = dir.y;
	vec3 atmo;
	if (up >= 0.0) {
		atmo = mix(sky_horizon_color, sky_top_color, pow(clamp(up, 0.0, 1.0), 0.5));
	} else {
		atmo = mix(ground_horizon, ground_bottom, pow(clamp(-up, 0.0, 1.0), 0.5));
	}
	atmo *= atmo_energy;

	// ── Star panorama ───────────────────────────────────────────────────────
	vec2 suv  = dir_to_equirect(dir);
	vec3 stars = texture(star_tex, suv).rgb * star_energy;

	// Cross-fade: blue atmosphere near the ground → stars in space.
	// Additive blend at the seam reads as stars emerging through a thinning sky.
	vec3 col = mix(atmo, stars, space_blend);

	COLOR = col;
}
