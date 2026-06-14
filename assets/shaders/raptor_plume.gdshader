// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Raptor engine plume shader (full-flow staged-combustion methalox).
//
// Apply to a vertical cone/cylinder mesh whose APEX sits at the nozzle exit
// (local +Y) and whose body extends DOWN along local -Y. The mesh is built once
// per ring at unit-ish scale; PlumeSystem stretches/recolours it via uniforms.
//
// The shader paints, from nozzle outward:
//   • a bright blue-white supersonic core,
//   • periodic Mach (shock) diamonds — bright nodes along the axis whose spacing
//     widens with altitude (lower ambient pressure),
//   • a translucent, additive outer sheath that broadens and fades downstream,
//     with faint incandescent orange edges,
//   • high-frequency flicker so the flame is alive.
//
// Methalox burns nearly translucent blue — NOT orange/yellow. Orange only
// appears as faint recombination at the plume edges.
// ─────────────────────────────────────────────────────────────────────────────
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// 0 = off, 1 = full thrust. Scales brightness & opacity.
uniform float throttle : hint_range(0.0, 1.0) = 1.0;

// 0 = sea level (tight plume, tight diamonds), 1 = vacuum (broad, long, widely
// spaced diamonds — an underexpanded vacuum plume).
uniform float expansion : hint_range(0.0, 1.0) = 0.0;

// Engine-specific tint of the core (blue-white). Vacuum Raptors read slightly
// bluer/purpler than sea-level.
uniform vec3 core_color : source_color = vec3(0.70, 0.85, 1.0);
// Incandescent edge tint (faint orange recombination glow).
uniform vec3 edge_color : source_color = vec3(1.0, 0.45, 0.12);

// Number of Mach diamonds at sea level (they get sparser as `expansion` rises).
uniform float diamond_count : hint_range(2.0, 14.0) = 8.0;

// Master energy multiplier.
uniform float energy : hint_range(0.0, 8.0) = 3.2;

varying float v_axial;   // 0 at nozzle exit → 1 at plume tip (along the mesh -Y)
varying float v_radial;  // 0 on axis → 1 at mesh skin

void vertex() {
	// The cone mesh is unit height (1.0), centred on its own origin: the wide
	// mouth (toward the nozzle) sits at VERTEX.y = +0.5 and the fine tip at
	// VERTEX.y = -0.5. Re-derive normalised axial/radial coords so the fragment
	// stage is scale-independent (PlumeSystem pre-scales the mesh non-uniformly).
	v_axial  = clamp(0.5 - VERTEX.y, 0.0, 1.0);          // 0 at mouth → 1 at tip
	// Local skin radius shrinks from 0.5 (mouth) to ~0.05 (tip); normalise by it.
	float skin = mix(0.5, 0.05, v_axial);
	v_radial = clamp(length(VERTEX.xz) / max(skin, 0.02), 0.0, 1.0);

	// Subtle breathing of the skin so the envelope shimmers (cheap, vertex-rate).
	float wob = 1.0 + 0.04 * sin(TIME * 37.0 + VERTEX.y * 9.0);
	VERTEX.xz *= wob;
}

// Cheap hash noise for high-frequency combustion flicker.
float hash(float n) { return fract(sin(n) * 43758.5453123); }
float noise1(float x) {
	float i = floor(x);
	float f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(hash(i), hash(i + 1.0), f);
}

void fragment() {
	float a = v_axial;            // 0 = nozzle, 1 = tip
	float r = v_radial;           // 0 = axis, 1 = skin

	// ── Radial profile: bright on axis, fades to skin. The plume narrows
	// downstream at sea level but stays fat in vacuum (underexpanded). ───────
	float widthAtA = mix(1.0 - a * 0.65, 1.0 - a * 0.15, expansion);
	float coreR    = clamp(r / max(widthAtA, 0.05), 0.0, 1.4);
	float radial   = exp(-coreR * coreR * 3.2);          // gaussian falloff

	// ── Axial envelope: full near the nozzle, fades toward the tip. Vacuum
	// plumes reach much farther before fading. ──────────────────────────────
	float reach    = mix(0.7, 1.0, expansion);
	float axialEnv = smoothstep(1.0, 0.55 * reach, a) * smoothstep(-0.05, 0.06, a);

	// ── Mach diamonds: periodic bright nodes along the axis. Spacing widens
	// (fewer visible diamonds) as ambient pressure drops. They are strongest
	// near the nozzle and damp out downstream. ──────────────────────────────
	float count   = mix(diamond_count, diamond_count * 0.4, expansion);
	float phase   = a * count * 3.14159;
	float diamond = pow(max(sin(phase), 0.0), 6.0);      // sharp bright nodes
	// Diamonds only live in the supersonic near field and on-axis.
	float diamondMask = (1.0 - smoothstep(0.0, 0.75, a)) * exp(-coreR * coreR * 5.0);
	diamond *= diamondMask;

	// ── Flicker: high-frequency turbulence so the flame is alive. ───────────
	float flick = 0.85
		+ 0.15 * noise1(TIME * 40.0 + a * 30.0)
		+ 0.08 * sin(TIME * 90.0 + r * 20.0);

	// ── Compose colour. Core is blue-white & hottest near the nozzle, cooling
	// toward white then losing energy. Edges pick up faint orange. ──────────
	vec3 whiteHot = vec3(1.0);
	vec3 col = mix(whiteHot, core_color, smoothstep(0.0, 0.4, a)); // white at throat → blue downstream
	// Faint incandescent orange recombination at the plume skin.
	col = mix(col, edge_color, smoothstep(0.45, 1.0, r) * 0.55);

	// Brightness build-up.
	float coreBright = radial * axialEnv;
	float bright = coreBright * (1.0 + diamond * 2.5) * flick;

	// Diamonds flash bright white.
	col = mix(col, whiteHot, clamp(diamond * 1.2, 0.0, 1.0));

	float thr = clamp(throttle, 0.0, 1.0);
	float e   = energy * (0.35 + 0.65 * thr);

	vec3 emission = col * bright * e;

	ALBEDO   = emission;
	EMISSION = emission;
	// Additive: alpha modulates contribution. Keep the translucent sheath soft.
	ALPHA = clamp(bright * (0.55 + 0.45 * thr), 0.0, 1.0);
}
