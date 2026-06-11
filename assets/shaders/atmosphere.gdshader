// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Atmospheric scattering shader (simplified Rayleigh + Mie).
// Apply to a sphere mesh slightly larger than the planet mesh
// (scale = 1.0 + atmosphere_thickness).
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never;

// Direction toward the sun (world space, normalized).
uniform vec3 sun_direction = vec3(1.0, 0.0, 0.0);

// Rayleigh scattering tint — Earth blue by default.
uniform vec3 rayleigh_color : source_color = vec3(0.18, 0.4, 1.0);

// Mie scattering tint — warm sunset halo.
uniform vec3 mie_color : source_color = vec3(1.0, 0.8, 0.6);

// Atmosphere shell thickness as a fraction of the planet radius.
uniform float atmosphere_thickness : hint_range(0.02, 0.1) = 0.05;

// Henyey-Greenstein asymmetry factor (forward-scattering strength).
uniform float mie_g : hint_range(0.5, 0.99) = 0.76;

// Overall brightness multiplier.
uniform float intensity : hint_range(0.0, 10.0) = 2.0;

varying vec3 world_normal;
varying vec3 world_view;

void vertex() {
	// Surface normal and view ray in world space.
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_view = world_pos - CAMERA_POSITION_WORLD;
}

// Rayleigh phase function: (3/4) * (1 + cos²θ)
float rayleigh_phase(float cos_theta) {
	return 0.75 * (1.0 + cos_theta * cos_theta);
}

// Mie phase function (Henyey-Greenstein):
// (1 - g²) / (4π * (1 + g² - 2g·cosθ)^1.5)
float mie_phase(float cos_theta, float g) {
	float g2 = g * g;
	float denom = 1.0 + g2 - 2.0 * g * cos_theta;
	return (1.0 - g2) / (12.56637 * pow(max(denom, 1e-4), 1.5));
}

void fragment() {
	vec3 view_dir = normalize(world_view);
	vec3 sun_dir = normalize(sun_direction);
	vec3 normal = normalize(world_normal);

	// θ — angle between the view ray and the sun direction.
	float cos_theta = dot(view_dir, sun_dir);

	// ── 1. View ray through atmosphere ──────────────────────────────────────
	// At the limb the view ray grazes the shell and traverses far more
	// atmosphere than at the disc centre. Approximate the relative path
	// length with the chord through a thin spherical shell.
	float ndotv = clamp(dot(normal, -view_dir), 0.0, 1.0);
	float grazing = 1.0 - ndotv;                       // 0 = centre, 1 = limb
	// Optical depth: rises sharply toward the limb, scaled by shell thickness.
	float optical_depth = pow(grazing, 3.0) + atmosphere_thickness * 10.0 * pow(grazing, 1.5);
	optical_depth *= clamp(atmosphere_thickness / 0.05, 0.4, 2.0);

	// ── 4. Limb brightening + day/night terminator ──────────────────────────
	// The atmosphere is lit where the surface below it faces the sun; fade
	// smoothly through the terminator and leave a faint twilight band.
	float sun_facing = dot(normal, sun_dir);
	float daylight = smoothstep(-0.25, 0.25, sun_facing);

	// ── 2 & 3. Phase functions ───────────────────────────────────────────────
	float rayleigh = rayleigh_phase(cos_theta);
	float mie = mie_phase(cos_theta, mie_g);

	// Rayleigh dominates the broad blue glow; Mie adds a warm forward-scatter
	// halo concentrated near the sun direction, strongest at the terminator.
	float twilight = pow(1.0 - abs(sun_facing), 4.0);
	vec3 scatter = rayleigh_color * rayleigh
	             + mie_color * mie * (0.5 + 1.5 * twilight);

	vec3 col = scatter * optical_depth * daylight * intensity;

	ALBEDO = col;
	ALPHA = clamp(optical_depth * daylight, 0.0, 1.0);
}
