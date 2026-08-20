// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Generic procedural body shader (rocky worlds & gas giants).
// Scale-independent: all detail sampled from the normalized local position.
//
//   mode 0 = rocky/cratered (Moon, Mercury, Mars)
//   mode 1 = banded gas giant (Jupiter, Saturn)
//   mode 2 = smooth creamy cloud (Venus)
//
// `surface_tint` is the base color; `detail_tint` is mixed in by noise.
// A `sun_dir` terminator and faint Fresnel rim are applied to all modes.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
// Unshaded: this shader does its own day/night lighting, so bypass Godot's light loop
// (the 1.5-energy DirectionalLight would otherwise relight it to white).
render_mode cull_back, unshaded;

uniform int   mode : hint_range(0, 2) = 0;
uniform vec3  sun_dir = vec3(0.4, 0.5, 0.8);
// Shared solar-disc visibility from SunController. This includes the body's own
// horizon occlusion and external eclipses, so the procedural surface cannot stay
// fully sunlit while the atmosphere/sky is in shadow.
uniform float solar_visibility : hint_range(0.0, 1.0) = 1.0;
uniform vec3  surface_tint : source_color = vec3(0.5, 0.5, 0.5);
uniform vec3  detail_tint  : source_color = vec3(0.3, 0.3, 0.3);
uniform vec3  rim_color    : source_color = vec3(0.4, 0.5, 0.7);
uniform float detail_scale : hint_range(0.5, 12.0) = 4.0;
uniform float roughness_val : hint_range(0.0, 1.0) = 0.95;
uniform float rim_strength  : hint_range(0.0, 2.0) = 0.0;
uniform float band_warp     : hint_range(0.0, 0.5) = 0.08;
uniform float cloud_band_strength : hint_range(0.0, 1.0) = 0.0;
// The night term is a bounded presentation proxy for starlight/ambient glow.
// It is deliberately much lower than the former 0.05 floor because exposure
// adapts upward in darkness. Direct daylight remains controlled independently.
uniform float day_gain       : hint_range(0.0, 1.5) = 1.0;
uniform float night_floor    : hint_range(0.0, 0.05) = 0.006;

// Orbital rocky bodies need a separate, bounded earthshine/starlight proxy:
// at orbital scale their illuminated disc can occupy very few pixels, making
// the configured Mars floor (0.012) numerically finite but visually unreadable.
// Keep this calibration local to mode 0; Venus clouds and gas giants retain
// their material-provided floor. This never contributes to direct sunlight.
const float ROCKY_ORBITAL_NIGHT_FLOOR = 0.024;

// Optional real surface map (Mars, Moon, …). When tex_amount > 0 the equirect texture
// replaces the procedural base colour; procedural micro-detail still modulates it.
uniform sampler2D surface_tex : source_color, hint_default_white, filter_linear_mipmap;
uniform float tex_amount : hint_range(0.0, 1.0) = 0.0;

varying vec3 v_local;
varying vec3 v_world_normal;
varying vec3 v_world_pos;

vec3 hash3(vec3 p) {
	p = vec3(dot(p, vec3(127.1, 311.7, 74.7)),
	         dot(p, vec3(269.5, 183.3, 246.1)),
	         dot(p, vec3(113.5, 271.9, 124.6)));
	return -1.0 + 2.0 * fract(sin(p) * 43758.5453123);
}

float gnoise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	return mix(
		mix(mix(dot(hash3(i + vec3(0,0,0)), f - vec3(0,0,0)),
		        dot(hash3(i + vec3(1,0,0)), f - vec3(1,0,0)), u.x),
		    mix(dot(hash3(i + vec3(0,1,0)), f - vec3(0,1,0)),
		        dot(hash3(i + vec3(1,1,0)), f - vec3(1,1,0)), u.x), u.y),
		mix(mix(dot(hash3(i + vec3(0,0,1)), f - vec3(0,0,1)),
		        dot(hash3(i + vec3(1,0,1)), f - vec3(1,0,1)), u.x),
		    mix(dot(hash3(i + vec3(0,1,1)), f - vec3(0,1,1)),
		        dot(hash3(i + vec3(1,1,1)), f - vec3(1,1,1)), u.x), u.y),
		u.z);
}

float fbm(vec3 p, int octaves) {
	float v = 0.0; float amp = 0.5; float freq = 1.0;
	for (int i = 0; i < 8; i++) {
		if (i >= octaves) break;
		v += amp * gnoise(p * freq);
		freq *= 2.0; amp *= 0.5;
	}
	return v;
}

// Crater field: distance-to-feature pattern that reads as round craters.
float craters(vec3 p, float scale) {
	vec3 sp = p * scale;
	vec3 cell = floor(sp);
	float c = 0.0;
	for (int x = -1; x <= 1; x++)
	for (int y = -1; y <= 1; y++)
	for (int z = -1; z <= 1; z++) {
		vec3 g = cell + vec3(float(x), float(y), float(z));
		vec3 o = 0.5 + 0.5 * hash3(g);
		float r = 0.18 + 0.22 * fract(hash3(g).x * 13.0);
		float d = length(sp - (g + o));
		// Rim-bright, floor-dark crater profile.
		float crater = smoothstep(r, r * 0.6, d) - 0.6 * smoothstep(r * 0.55, 0.0, d);
		c = max(c, crater);
	}
	return c;
}

void vertex() {
	v_local = normalize(VERTEX);
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 p = normalize(v_local);
	vec3 N = normalize(v_world_normal);
	vec3 L = normalize(sun_dir);
	vec3 V = normalize(CAMERA_POSITION_WORLD - v_world_pos);

	vec3 col = surface_tint;

	if (mode == 0) {
		// Rocky: large maria/regolith variation + crater field.
		float maria = 0.5 + 0.5 * fbm(p * detail_scale, 5);
		col = mix(surface_tint, detail_tint, smoothstep(0.45, 0.7, maria));
		float cr = craters(p, detail_scale * 2.0);
		col *= 1.0 + 0.5 * cr;                 // bright rims, dark floors
		col *= 0.9 + 0.2 * fbm(p * detail_scale * 6.0, 3);
	} else if (mode == 1) {
		// Gas giant: horizontal bands (function of latitude) with turbulence.
		float warp = band_warp * fbm(p * 3.0, 4);
		float lat = p.y + warp;
		float bands = 0.5 + 0.5 * sin(lat * detail_scale * 6.2831 + fbm(p * 6.0, 3) * 2.0);
		col = mix(surface_tint, detail_tint, bands);
		// Subtle storm swirls.
		col *= 0.92 + 0.16 * fbm(p * vec3(8.0, 2.0, 8.0), 4);
	} else {
		// Venus: smooth creamy clouds, very low contrast.
		float n = 0.5 + 0.5 * fbm(p * detail_scale, 5);
		col = mix(surface_tint, detail_tint, n * 0.4);
	}

	// Real surface map overrides the procedural base when bound, keeping a touch of
	// procedural micro-detail so it doesn't look flat when magnified up close.
	if (tex_amount > 0.001) {
		float lon = atan(p.z, p.x);
		float lat = asin(clamp(p.y, -1.0, 1.0));
		vec3 texCol = texture(surface_tex, vec2(lon / (2.0 * PI) + 0.5, 0.5 - lat / PI)).rgb;
		texCol *= 0.92 + 0.16 * fbm(p * detail_scale * 6.0, 3);
		col = mix(col, texCol, tex_amount);
	}
	if (mode == 2 && cloud_band_strength > 0.001) {
		// Venus' deck is not a flat beige sphere. Broad latitude streaks and a
		// low-frequency shear remain visible in near-cloud and orbital views while
		// the loaded texture still supplies the base colour.
		float shear = fbm(p * vec3(7.0, 1.4, 7.0), 3);
		float band = 0.5 + 0.5 * sin((p.y + shear * 0.055) * 46.0);
		vec3 band_tint = mix(vec3(0.72, 0.61, 0.47), vec3(1.08, 0.96, 0.76), band);
		col *= mix(vec3(1.0), band_tint, cloud_band_strength);
	}

	// Day/night lighting.
	float ndl = dot(N, L);
	float day = smoothstep(-0.12, 0.22, ndl);
	float direct = day * day_gain * solar_visibility;
	float mode_night_floor = mode == 0 ? max(night_floor, ROCKY_ORBITAL_NIGHT_FLOOR) : night_floor;
	col *= mode_night_floor + direct;

	// Faint atmospheric rim (Venus / gas giants set rim_strength > 0).
	float fres = pow(1.0 - clamp(dot(N, V), 0.0, 1.0), 3.0);
	float rim_day = smoothstep(-0.3, 0.4, ndl) * solar_visibility;
	// Venus' bright cloud deck needs a bounded sun-facing limb. Preserve the
	// 0.2 night-side rim term exactly, while reducing only the daylight portion;
	// solar_visibility remains the authority for the illuminated contribution.
	float rim_day_response = mode == 2 ? 0.55 : 0.9;
	col += rim_color * fres * rim_strength * (0.2 + rim_day_response * rim_day);

	ALBEDO = col;
	ROUGHNESS = roughness_val;
	METALLIC = 0.0;
}
