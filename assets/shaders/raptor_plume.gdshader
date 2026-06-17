// ─────────────────────────────────────────────────────────────────────────────
// Exosphere — Raptor engine plume shader (full-flow staged-combustion methalox).
//
// Apply to a vertical cone/cylinder mesh whose MOUTH sits at the nozzle exit
// (local +Y) and whose body extends DOWN along local -Y. The mesh is built once
// per ring at unit-ish scale; PlumeSystem stretches/recolours it via uniforms.
//
// The shader paints, from nozzle outward:
//   • a bright, near-white incandescent base right at the throat (over-expanded
//     sea-level flame — hot, slightly orange at the very root),
//   • a bright blue-white supersonic core down the axis,
//   • periodic Mach (shock) diamonds — sharp bright nodes along the axis whose
//     spacing widens (fewer diamonds) as ambient pressure drops with altitude,
//   • a translucent, additive outer sheath that broadens and fades downstream,
//     with faint incandescent orange edges,
//   • high-frequency flicker so the flame is alive.
//
// SL→vacuum behaviour (driven by `expansion`, 0 = sea level, 1 = vacuum):
//   • sea level  → short, tight, bright plume; many closely-spaced sharp diamonds
//                  packed near the throat; strong hot/orange base.
//   • vacuum     → very long, broad, faint plume dominated by the smooth bright
//                  core; diamonds few, soft and pushed downstream.
//
// Methalox burns nearly translucent blue — NOT orange/yellow. Orange only
// appears as hot recombination at the very base and along the plume skin.
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
// Incandescent edge/base tint (hot orange recombination glow).
uniform vec3 edge_color : source_color = vec3(1.0, 0.45, 0.12);

// Number of Mach diamonds at sea level (they get sparser as `expansion` rises).
uniform float diamond_count : hint_range(2.0, 14.0) = 8.0;

// Master energy multiplier.
uniform float energy : hint_range(0.0, 10.0) = 3.2;

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
	// downstream at sea level but stays fat (underexpanded) in vacuum. ───────
	float widthAtA = mix(1.0 - a * 0.70, 1.0 - a * 0.08, expansion);
	float coreR    = clamp(r / max(widthAtA, 0.05), 0.0, 1.4);
	// Tighter gaussian at SL (a pencil core), softer/broader in vacuum.
	float falloff  = mix(3.6, 2.2, expansion);
	float radial   = exp(-coreR * coreR * falloff);          // gaussian falloff

	// ── Axial envelope: full near the nozzle, fades toward the tip. Vacuum
	// plumes reach much farther before fading (the long faint core). ─────────
	float reach    = mix(0.62, 1.0, expansion);
	float axialEnv = smoothstep(1.0, 0.5 * reach, a) * smoothstep(-0.04, 0.05, a);

	// ── Mach diamonds: periodic sharp bright nodes along the axis. At sea level
	// they are numerous, sharp and packed near the throat; in vacuum they are
	// few, soft and pushed downstream. ───────────────────────────────────────
	float count   = mix(diamond_count, diamond_count * 0.35, expansion);
	// Diamonds bunch up toward the throat (sqrt warps spacing tighter near a=0).
	float warpedA = sqrt(max(a, 0.0));
	float phase   = warpedA * count * 3.14159;
	float sharp   = mix(8.0, 3.0, expansion);                // sharper at SL
	float diamond = pow(max(sin(phase), 0.0), sharp);        // sharp bright nodes
	// Diamonds only live in the supersonic near field and on-axis; their reach
	// downstream grows a little in vacuum.
	float dReach  = mix(0.65, 0.9, expansion);
	float diamondMask = (1.0 - smoothstep(0.0, dReach, a)) * exp(-coreR * coreR * 4.5);
	diamond *= diamondMask;

	// ── Flicker: high-frequency turbulence so the flame is alive. ───────────
	float flick = 0.85
		+ 0.15 * noise1(TIME * 40.0 + a * 30.0)
		+ 0.08 * sin(TIME * 90.0 + r * 20.0);

	// ── Compose colour. Hot near-white incandescent base at the throat (with a
	// faint orange root), cooling to the blue-white core down the axis. The hot
	// base is strongest at sea level (over-expanded), nearly gone in vacuum. ──
	vec3 whiteHot = vec3(1.0);
	// White at the throat → blue-white core downstream.
	vec3 col = mix(whiteHot, core_color, smoothstep(0.0, 0.45, a));
	// Hot orange root, only at the very base and mostly at sea level.
	float hotBase = (1.0 - smoothstep(0.0, 0.18, a)) * (1.0 - expansion * 0.85);
	col = mix(col, edge_color, hotBase * 0.5);
	// Faint incandescent orange recombination at the plume skin.
	col = mix(col, edge_color, smoothstep(0.5, 1.0, r) * 0.5);

	// Brightness build-up. The base burns brighter at SL (over-expanded shock
	// structure); the vacuum plume is dimmer but reaches much farther.
	float baseBoost = 1.0 + hotBase * (0.9 - expansion * 0.6);
	float coreBright = radial * axialEnv * baseBoost;
	float bright = coreBright * (1.0 + diamond * 3.0) * flick;

	// Diamonds flash bright white.
	col = mix(col, whiteHot, clamp(diamond * 1.3, 0.0, 1.0));

	float thr = clamp(throttle, 0.0, 1.0);
	// Sea-level plumes are visually punchier per unit length; vacuum a touch
	// dimmer (very thin gas), but its length makes it dominant in frame.
	float eScale = mix(1.15, 0.85, expansion);
	float e   = energy * eScale * (0.35 + 0.65 * thr);

	vec3 emission = col * bright * e;

	ALBEDO   = emission;
	EMISSION = emission;
	// Additive: alpha modulates contribution. Keep the translucent sheath soft.
	ALPHA = clamp(bright * (0.55 + 0.45 * thr), 0.0, 1.0);
}
