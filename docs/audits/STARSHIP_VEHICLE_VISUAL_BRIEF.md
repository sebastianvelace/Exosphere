# Starship + Super Heavy — vehicle visual brief

**Status:** research + implementation brief for the *vehicle-owned* workstream.
**Date:** 2026-08-23
**Branch:** `visual/starship-vehicle-realism`
**Scope:** hull, TPS, flaps, grid fins, raceway, chines, engine bells, and **vehicle-side lighting** (sun response, self-shadow, exhaust bounce, ascent rim).
**Out of scope:** pad tile / ocean skirt / sky / Earth limb / deluge sheets (`EarthGround`, `Sky`, `space_sky`, planet shaders). Particle/mesh ownership of `PlumeSystem` is another agent; this brief may still specify *lights parented to the vehicle* driven by engine state.

Claim tags: `[verified]` page opened this session · `[index]` search hit, page not fully opened · `[synthesis]` reading of Exosphere vs photos · `[guess]` not sourced from a photo/CAD — do not treat as fact.

---

## 1. What the 23 Aug play screenshots fail (vehicle only)

User pad / ascent stills (morning 23 Aug) vs IFT pad cameras:

| Failure | What IFT stills actually do | Owner in this repo |
| --- | --- | --- |
| Stack reads as a toy grey cylinder | Mill-finish 304L/30X rings with weld HAZ, raceway, chines, 33 bells, 4 grid fins, black hex TPS vs bare steel | `VesselRenderer.cs`, `assets/shaders/steel.gdshader` |
| Flat lighting, no self-shadow, no sun specular on stainless | Hard Texas sun, grazing specular on rings, grid-fin lattice casting on the barrel, flap shadows on TPS | Vehicle materials + `DirectionalLight3D` receive/cast. Do **not** retune global tonemap (PLAN_VISUAL_REALISM V4: ACES/global glow already failed). |
| 33/33, THR 100%, vehicle not lit by exhaust | Photosphere-white plume is a **second key light**: orange-white bounce on the aft skirt, copper/soot bells glowing, OLM steel lit from below | Vehicle OmniLights + bell emission. Plume *meshes* belong elsewhere. |
| Ascent: black silhouette + white teardrop | Stainless still shows a lit limb and a darker leeward; TPS stays charcoal, not a cutout. Sky is bright; metal is **not** a black cookie-cutter | Lower unlit `fill_strength`, sky-colored Fresnel rim, engine rim light |
| Tiles / flaps / fins / welds / raceway / bells not reading Starship | See anatomy §3 | Extend existing procedural toolkit — do not rewrite `BuildFullStack` from scratch |

`[synthesis]` from the play stills plus IFT webcast language below.

---

## 2. References (URLs) and what they imply for shaders / meshes / lights

### Official vehicle / flights

| URL | What it taught | Shader / mesh / light implication |
| --- | --- | --- |
| https://www.spacex.com/vehicles/starship | Stack **124 m / 9 m Ø**; Ship **52 m**; Super Heavy **72 m**; Raptor **1.3 m Ø × 2.9 m**; 33 SH engines (13 center gimbal + 20 outer); Ship 3 SL + 3 RVac | Keep 9 m barrel (`BodyR = 1.607 u` at 2.8 m/u). Do not shrink to a Falcon-like stick. Bells must read as a dense cluster under a flared skirt, not a 3-engine toy. `[verified]` |
| https://www.spacex.com/launches/starship-flight-test | IFT-1, 20 Apr 2023, **08:33 CT** daylight liftoff | Pad camera is *morning hard sun*, not studio fill. Shadows are long and sharp. `[verified]` |
| https://www.spacex.com/launches/starship-flight-13 | Flight 13 (24 Jul 2026) 33/33 ascent, hot-stage, ship reentry | Current operational visual language: V3 stack still 9 m steel + black TPS. Do not mix HLS legs/solar. `[verified]` |
| https://www.spacex.com/content/starship-flight-tests/flight-1 | Official IFT-1 webcast archive | Use as motion/lighting reference, not mesh CAD. `[verified]` |
| https://www.youtube.com/watch?v=hI9HQfCAw64 | Flight 5 official webcast (catch + ship entry) | Stainless glare on booster during catch; TPS remains black next to steel; plume lights the aft during landing burn. `[index]` |

### Anatomy, TPS, steel

| URL | What it taught | Implication |
| --- | --- | --- |
| https://en.wikipedia.org/wiki/SpaceX_Starship | ~18 000 hex silica tiles, pin-mounted, expansion gaps; leeward is **bare 300-series stainless**; Block 2 Ship 52.1 m | Windward ≠ leeward. Uniform grey cylinder is wrong. Tiles are dielectric, high-ε black glass, **not** dark metal. `[verified]` |
| https://en.wikipedia.org/wiki/Super_Heavy_(booster_rocket) | 33 Raptors; Block 1/2 **four** steel grid fins ~3 t each, remain extended in ascent; Block 3 is 3 larger fins — **later variant** | This product’s IFT-era stack is **4 fins**. Do not silently switch to T-layout. Fins are steel lattice, not painted paddles. `[verified]` |
| https://en.wikipedia.org/wiki/SAE_304_stainless_steel | Early ships 301 → **304L** (SN7/SN8+); SpaceX later “30X” | Shader is 304L mill analog. 30X BRDF is unpublished. `[verified]` / `[guess]` on 30X |
| https://spacelaunchlive.com/materials/stainless-steel-304l/ | Rings ~1.83 m tall, ~4 mm wall; 304L chosen for weldability + cryo strength + melt point | Weld spacing on a 9 m barrel should read as stacked rings (~1.8 m), not a smooth soda-can. `[index]` |
| https://starship-spacex.fandom.com/wiki/Starship_Thermal_Protection_System_(TPS) | Windward black ceramic; leeward steel only; TUFROC-like / silica with black coating | Tile albedo ≪ steel albedo. No metallic on TPS. `[index]` |
| https://doi.org/10.1007/s12567-025-00625-8 (DLR/CEAS, IFT-4 TPS model) | Hex tiles **~33 mm thick**, short diagonal **~24 cm**, 3 pin attach; 7–8 side rows past midline; insulation mat similar thickness | Hex scale in a tile shader should be tens of cm, not meters. Side wrap is not a perfect 180° paint job — it overwraps the shoulder. `[index]` |
| https://ntrs.nasa.gov/api/citations/20230009259/downloads/EDL%20Seminar%20-%20Reusable%20TPS%20Past%20Present%20and%20Future%20v4.0.pdf | Shuttle HRSI: black RCG glass, ε ≥ 0.9, high-temp reusable | Use as **optical analog** for Starship tiles (black glass, matte-gloss, dielectric). Not identical chemistry. `[verified]` |
| https://ntrs.nasa.gov/citations/19840015630 | NASA RP-1121 spacecraft coating α/ε tables | Do not invent α = 0.89 for mill 304. Measure-or-analog: mill stainless is a *reflector*; black RCG is an *absorber*. `[verified]` |
| https://pubs.rsc.org/en/content/articlehtml/2023/ra/d3ra03873d | Unmodified AISI 304 solar absorptance can sit ~0.4 band (paper’s coated samples go 0.59–0.92) | Mill steel is **not** chrome-mirror (albedo 0.88) and **not** grey plastic. PBR albedo ~0.55–0.72, metallic high, roughness ~0.22–0.35 mill / 2B. `[verified]` on the paper; `[guess]` mapping to SpaceX mill rings |
| https://oceanplayer.com/ultimate-guide-to-weld-heat-tint-colors/ (BSSA temper-color sequence) | Straw → brown → purple → blue oxide on 304 HAZ | Subtle gold/blue weld bands, not black decals. `[index]` |

### Lights, plume as illuminant, pad silhouette

| URL | What it taught | Implication |
| --- | --- | --- |
| https://commons.wikimedia.org/wiki/File:Full_Stack_starship.jpg | 16 Apr 2023, 16:25 CDT, 1/1600 s, f/10, ISO 400 — B7/S24 on OLM | Hard sun, long shadows, steel brighter than TPS, grid fins and flaps readable at pad distance. **This is the pad-camera acceptance still.** `[verified]` |
| https://everydayastronaut.com/starship-superheavy-integrated-flight-test-3/ | Four elongated triangular **chines** near the aft holding COPVs; BQD door; flap roles | Super Heavy is not a clean cylinder: chines + raceway + grid-fin mounts. `[verified]` |
| https://www.funkystuff.org/spacex-starship-iii-sequence-of-events-ift-3/ | Deluge ~T−10 s; Raptor ignition sequence **T−3 s**; liftoff ~T+2 s | Bells glow *before* the vehicle leaves the mount. Vehicle lights must come up with throttle while still held. `[verified]` |
| https://www.eonmsk.com/2025/03/10/watch-super-heavy-33-raptor-engines-ignition-from-under-launch-pad/ | Under-OLM view of 33-engine ignition (Flight 8 clip) | Looking up the cluster: bright disk, bells rim-lit, skirt underside hot. `[index]` |
| https://www.americaspace.com/2023/11/18/spacex-achieves-successful-first-stage-burn-starship-separation-in-ift-2-test-flight/ | IFT-2: 33 lights at the tail through MECO; hot-stage | Exhaust is a light source along the whole first-stage burn, not only at T-0. `[index]` |
| https://space.stackexchange.com/questions/38410/why-did-starhoppers-exhaust-plume-become-brighter-just-before-landing | Clean Raptor plume is **blue** (excited H2O); yellow = dust/soot/fuel-rich | Atmospheric SL plume: pale blue-white core, not orange Merlin. Vehicle bounce light: cool-white with a warm skirt bounce from the photosphere. `[verified]` |
| https://space.stackexchange.com/questions/50620/why-are-the-rocket-plumes-on-sn10-different-colors | Throttled / fuel-rich → yellow; green hint = copper liner erosion | Bell *interior* can go copper/orange; exterior is dark Inconel/soot on Raptor 2. `[verified]` |
| https://www.nasaspaceflight.com/2024/10/starship-flight-5-catch/ | Flight 5: full TPS rework; black-painted catch stringers on booster | Steel vs tile contrast is the Ship’s identity; booster is almost all steel + soot + a few black cues. `[verified]` |
| https://www.space.com/spacex-starship-flight-5-launch-super-heavy-booster-catch-success-video | Catch stills: stainless in daylight, chopsticks, no silhouette pancake | Even in a busy sky, the booster keeps form via specular + shadow. `[index]` |
| https://www.teslaoracle.com/2025/08/16/spacex-reveals-grid-fins-of-the-next-gen-starship-super-heavy-booster/ | Block 3 fins ~7.5×3.75 m (50% up from ~5×2.5 m Block 1/2 **estimates**) | Block 1/2 fin ~5 m class. Current mesh ~1.85 u × 2.8 ≈ **5.2 m** height — order-correct. Width may be slightly generous. `[index]` / `[guess]` on exact CAD |
| https://forum.nasaspaceflight.com/index.php?topic=53555.2320 | Raptor copper liner + Ni alloy jacket; regenerative channels; film/soot inside | Exterior bell ≠ polished copper flowerpot (that is a KSP tell). Interior throat can read copper. `[index]` |

### What KSP / SimpleRockets / “Kerbal toys” get wrong

`[synthesis]` — these are the tropes the play stills currently match:

1. **Uniform albedo cylinder** — real stack is two materials (steel / black glass) with weld HAZ, frost bands, soot.
2. **Unshaded or sky-only ambient** — real pad is a directional sun with contact shadow.
3. **Plume as a sprite that does not illuminate anything** — 33 Raptors are a 10⁸-cd-class source; the skirt *must* pick up bounce.
4. **Chrome or plastic “metal”** — mill 304 is satin, anisotropic, F0 of iron/steel (~0.55), not a car paint flake and not a mirror probe.
5. **Grid fins as solid plates** — they are lattices; sky shows through.
6. **Engine bells as orange cones** — Raptor 2 SL: dark sooted exterior, glow in the throat when lit.
7. **Ascent black cutout** — cameras expose for sky *and* still see the sunlit limb of steel. A 0.12 unlit emission fill *and* a too-dark metal both fail: the first flattens, the second pancakes.
8. **Flaps glued shut on the pad** — stacked IFT vehicles show aft flaps visibly away from the barrel.

---

## 3. Anatomy checklist (visual, not physics)

| Feature | Real (sourced) | Exosphere before this pass `[synthesis]` |
| --- | --- | --- |
| Diameter | 9 m `[verified]` SpaceX | 9 m (`BodyR`) — keep |
| Stack height | ~121–124 m depending on block `[verified]` | Flight-7-ish 71+50 m — keep; do not retune physics |
| Super Heavy | 33 Raptors, raceway, **4 chines**, 4 grid fins (Block 1/2), hot-stage ring | 33 bells + raceway + seams; **chines missing** |
| Ship | Ogive, Pez/payload door cue, fwd+aft flaps, windward hex TPS, 3+3 Raptors | Ogive + door outline + flaps + stave TPS (not hex) |
| Steel | Mill rings, weld HAZ, cryo frost, soot near engines | Steel shader exists but **fill_strength 0.12** + albedo ~0.88 → grey plastic / chrome hybrid |
| TPS | Hex ~24 cm, black glass, gaps | Box staves + emission 0.05 → grey slab in daylight |
| Bells | Dark exterior, throat glow when firing, 1.3 m exit | Dark Inconel path exists; **no throttle-driven emission** |
| Flaps on pad | Partly open `[synthesis]` from stacked photos | `ComputeFlapDeployment` → 0 at low q → **boards glued on** |
| Lights | Sun key + plume fill + (twilight) tower floods | Ambient 0.45 + material emission fill; plume OmniLights exist but sit on `PlumeSystem` and do not save a silhouette if plumes are off/unowned |

Twilight/night Starbase: many IFTs are dawn/dusk; tower floods keep the stack from becoming a cutout. **Pad flood geometry is another agent.** Vehicle-side: keep a tiny sky-bounce Omni so the hull never dies if the environment wash is high.

RCS / header vents: visually secondary at pad distance. Existing vent boxes on the Ship are enough; do not invent glowing RCS jets without a photo of that flight state.

---

## 4. Lighting model (vehicle-owned)

Daytime pad (IFT-1 08:33 CT, Wikimedia 16:25 CDT):

1. **Key:** `DirectionalLight3D` (already `shadow_enabled = true` in `scenes/flight/Flight.tscn`). Stainless must *receive* that key: low unlit fill, mill roughness, metallic from albedo.
2. **Contact / self-shadow:** meshes already `CastShadow = On`. Fill emission of 0.10–0.12 plus tile emission 0.05 **erases** those shadows. Kill the fill first; do not add a second sun.
3. **Plume as fill:** vehicle Omni at the engine bay (warm photosphere bounce on the skirt) + cooler Omni just below the bells (methalox blue-white). Driven by delivered throttle. Independent of plume particles.
4. **Bell glow:** emission on throat > lip > exterior, gated on throttle > ~2%.
5. **Ascent against washed sky:** sky-colored Fresnel rim on steel (not grey albedo add). TPS stays dark; steel limb stays readable.
6. **Do not** change `PhaseLightingController` global ACES/glow (PLAN_VISUAL_REALISM V4 gotcha).

---

## 5. Acceptance criteria (pad-camera screenshot must pass)

A 1920×1080 pad-lateral (or 3/4) still at THR 100% / 33/33, same class as Wikimedia *Full Stack starship.jpg*:

1. **Self-shadow:** at least one of: flap on TPS, grid-fin lattice on barrel, or barrel terminator (lit vs shaded side). Not a constant grey.
2. **Tiles vs steel:** windward Ship is charcoal hex/stave TPS; leeward and booster are paler satin steel. Not one mid-grey.
3. **Engine-lit skirt:** with throttle 1.0, aft booster skirt / inner bells brighter/warmer than the cold mid-barrel, even if another agent’s plume sprites are missing.
4. **No silhouette pancake:** in a bright-sky ascent crop, the stack shows a sunlit limb or sky rim; not a black cookie-cutter with a white teardrop.
5. **Readable IFT silhouette:** 4 grid fins, 4 flaps (aft larger), raceway, dense engine cluster, 9 m / ~120 m proportions.

Ascent crop (~2–6 km chase) must still show (4) and a non-zero steel/TPS contrast.

---

## 6. Implementation order (highest leverage)

1. Cut steel `fill_strength` and tile daylight emission; mill albedo/roughness; weld HAZ; sky Fresnel rim.
2. Vehicle engine OmniLights + bell/throat emission from delivered throttle.
3. Pad rest pose for flaps; SH chines; grid-fin steel + darker lattice; hex cue on TPS shader.
4. Drive fill/rim vs altitude so ascent against sky keeps a limb.

`[G]` ascent and EDL control laws are not touched. Visual flap rest is a **pose floor**, not aero.

---

## 7. Explicit guesses (not photos)

- Exact SpaceX 30X mill BRDF and ring height on the flying article.
- Exact Block 1 grid-fin CAD (height order-of-magnitude only).
- Exact pad flap hinge angles (sourced as “visibly off the barrel,” not degrees from a drawing).
- Chine length/chord (Everyday Astronaut confirms existence and COPV role, not meters).
- Hex tile shader is a **procedural stand-in** for 24 cm tiles, not photogrammetry.
- Plume candela / Omni energy is tuned for Godot Forward+, not a radiometric match to 8 240 tf of methalox.

If a later pass has a surveyed still with EXIF and a matching camera, retune numbers — do not treat this file as CAD.

---

## 8. What this pass implemented (2026-08-23)

Vehicle-owned only. `[G]` ascent / EDL untouched. No `PlumeSystem` particle ownership. No Earth/sky/pad shaders.

| Change | File | Why |
| --- | --- | --- |
| Unshaded wrap-Lambert mill (sun_dir + sky rim + engine fill baked into HDR ALBEDO) | `assets/shaders/steel.gdshader` | Compatibility metallic PBR **ignores** Environment ambient 0.22. Shaded ShaderMaterial emission was crushed by the HDR sky (pad2 = cutout). `n.y` hemisphere is useless on a vertical cylinder. `[synthesis]` |
| Hex TPS unshaded wrap | `assets/shaders/heat_tile.gdshader` | Same Compatibility path; charcoal must keep form. Procedural ~24 cm stand-in. `[guess]` |
| `sun_dir` from `DirectionalLight3D`; `engine_light` from throttle | `VesselRenderer.UpdateVehicleLighting` | Unshaded hull cannot receive OmniLights; bake throttle warmth on the leeward wrap. |
| Vehicle OmniLights + bell/throat emission | `VesselRenderer` | Bells are StandardMaterial3D and still receive emission at THR>0. |
| Pad flap floor 0.28; 4 SH chines; grid fins on steel shader | `VesselRenderer` | IFT silhouette cues. Chine length / flap degrees `[guess]`. |

**Verified captures (xvfb, then harness deleted, `project.godot` restored):**
- `/tmp/exo_vehicle_brdf_pad2.png` — shaded emission: still a cutout (before unshaded).
- `/tmp/exo_vehicle_brdf_pad5.png` / `pad6.png` — mill grey on the sun wrap, charcoal TPS, not a full-stack black pancake. Shadow side still too dark vs IFT stainless.

Engram `visual/starship-vehicle` could not be written (MCP cwd ambiguous: intgrascale vs kicad).
