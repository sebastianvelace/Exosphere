// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Reentry plasma glow shader.
// Apply to rocket part meshes (or a duplicate shell mesh) during reentry.
// Drive `heat_level` from the simulation's aerodynamic heating value.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled;

uniform float heat_level : hint_range(0.0, 1.0) = 0.0; // 0=cold, 1=max temp
uniform sampler2D noise_tex;

// How far vertices are pushed along normals at heat_level = 1 (metres).
uniform float halo_size : hint_range(0.0, 0.5) = 0.08;

void vertex() {
	// Expand vertices along normals slightly at high heat for plasma halo
	// effect. Quadratic in heat so the halo only appears when glowing hot,
	// with a fast flicker so the envelope looks alive.
	float flicker = 1.0 + 0.15 * sin(TIME * 23.0 + VERTEX.y * 17.0 + VERTEX.x * 11.0);
	VERTEX += NORMAL * halo_size * heat_level * heat_level * flicker;
}

void fragment() {
	// ── Animated plasma turbulence ───────────────────────────────────────────
	// Two noise samples scrolling at different speeds break up tiling.
	float n1 = texture(noise_tex, UV + vec2(0.0, TIME * 0.3)).r;
	float n2 = texture(noise_tex, UV * 2.3 + vec2(TIME * 0.17, TIME * 0.41)).r;
	float turbulence = 0.6 + 0.6 * (0.65 * n1 + 0.35 * n2);

	// Effective heat, modulated by turbulence.
	float t = clamp(heat_level * turbulence, 0.0, 1.0);

	// ── Temperature → colour ramp ────────────────────────────────────────────
	// 0.0–0.2 : no emission
	// 0.2–0.5 : dark orange
	// 0.5–0.8 : bright orange / yellow
	// 0.8–1.0 : white-hot with blue plasma edges
	const vec3 DARK_ORANGE   = vec3(0.55, 0.13, 0.01);
	const vec3 BRIGHT_ORANGE = vec3(1.0, 0.55, 0.08);
	const vec3 YELLOW        = vec3(1.0, 0.85, 0.30);
	const vec3 WHITE_HOT     = vec3(1.0, 0.97, 0.90);
	const vec3 PLASMA_BLUE   = vec3(0.45, 0.65, 1.0);

	vec3 col = vec3(0.0);
	float strength = 0.0;

	if (t > 0.2) {
		if (t < 0.5) {
			// Faint → dark orange glow.
			float k = smoothstep(0.2, 0.5, t);
			col = mix(DARK_ORANGE * 0.4, BRIGHT_ORANGE, k);
			strength = k * 0.6 + 0.1;
		} else if (t < 0.8) {
			// Bright orange → yellow.
			float k = smoothstep(0.5, 0.8, t);
			col = mix(BRIGHT_ORANGE, YELLOW, k);
			strength = 0.7 + k * 0.8;
		} else {
			// White-hot core.
			float k = smoothstep(0.8, 1.0, t);
			col = mix(YELLOW, WHITE_HOT, k);
			strength = 1.5 + k * 2.0;

			// Blue plasma sheath on grazing edges (fresnel).
			// NORMAL and VIEW are both view-space in the fragment stage.
			float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 3.0);
			col = mix(col, PLASMA_BLUE, fresnel * k * 0.7);
		}
	}

	vec3 emission = col * strength;

	ALBEDO = emission;
	EMISSION = emission;
	// t <= 0.2 → fully transparent (no emission at all).
	ALPHA = t <= 0.2 ? 0.0 : clamp(strength * 0.6, 0.0, 1.0);
}
