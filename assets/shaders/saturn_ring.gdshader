// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Saturn's rings. A flat annulus (in the body's local XZ plane) whose
// radial UV samples an 8192×500 ring strip (RGBA: colour + alpha gaps). Double-sided,
// unshaded, alpha-blended; a soft day/night dim from the sun so the shadowed arc fades.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode cull_disabled, unshaded, blend_mix, depth_draw_opaque;

uniform sampler2D ring_tex : source_color, filter_linear_mipmap;
uniform vec3  sun_dir = vec3(0.4, 0.5, 0.8);
uniform float brightness = 1.15;

varying vec2 v_uv;
varying vec3 v_world_pos;

void vertex() {
	v_uv = UV;                                   // u = radial (inner→outer)
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 c = texture(ring_tex, vec2(v_uv.x, 0.5));
	// Gentle sunlit/shadow gradient across the ring plane.
	vec3 toSun = normalize(sun_dir);
	float lit = 0.55 + 0.45 * clamp(dot(normalize(v_world_pos), toSun) * 0.5 + 0.5, 0.0, 1.0);
	ALBEDO = c.rgb * brightness * lit;
	ALPHA  = c.a;
}
