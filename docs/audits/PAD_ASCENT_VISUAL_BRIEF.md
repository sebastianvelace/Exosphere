# Pad → 60 km visual brief (Starbase daylight / IFT)

**Status:** environment pass implemented on `visual/pad-ascent-realism` (disc-to-horizon, pad shadows, limb AA, daytime stars off). Plume/deluge remain a sibling owner.
**Scope:** play-camera look from OLM hold-down through ~60 km, Super Heavy + Starship, daylight.  
**Date:** 2026-08-23  
**Branch intent:** `visual/pad-ascent-realism` from `fix/pad-sky-engine-cap`.  
**Stale names in `PLAN_VISUAL_REALISM.md`:** that plan still says `SkyController`, `EarthGroundController`, `PlumeSystem`, `LaunchPadController`. Live owners are listed in §3.

---

## Resumen (es)

Las cinco capturas de cámara de juego no se leen como un IFT diurno en Boca Chica. El pad a THR 100% no tiene pluma ni muro de vapor, no hay sombras, el suelo es un tile y el cielo está lavado. A ~2–6 km el vehículo es una silueta y el terreno se vuelve una galleta cuadrada sobre un plano. A ~19 km el nadir sigue siendo un cuadrado de tierra con un sol que quema el frame. A ~58 km el limbo es un sierra blanco y el cielo está sobreexpuesto: no hay la línea cian de Rayleigh. El trabajo de esta tanda es **entorno + exposición + pluma de pad**, no fotogrametría global de la Tierra.

Los cinco must-fix: (1) pluma + deluge en T-0, (2) sombras direccionales en pad, (3) matar la galleta cuadrada de terreno, (4) cielo diurno azul (no blanco) y (5) limbo cian antialiasado a 50–80 km.

---

## Research question

**What must change so Exosphere’s play camera, from pad to the first 60 km, reads as a daylight Starship/Super Heavy IFT at Starbase (Boca Chica) rather than a toy diorama on a cookie of land?**

Sub-questions:

1. What is actually in an IFT T-0 / first-kilometre still (plume, steam wall, tower, wetland, sky)?
2. What does looking **down** and **to the horizon** look like at ~5, ~20, and ~50–80 km?
3. Why is a clear daytime sky blue, why are stars invisible, and why is the 50–80 km limb a cyan band rather than a white sawtooth?
4. What is the real Starbase geographic scale (Gulf, mudflats, barrier beach) versus a 1 km island on infinite ocean?

### Search protocol

| Theme | Queries | Inclusion | Exclusion |
| --- | --- | --- | --- |
| IFT pad / T-0 | SpaceX Flight 13 page; IFT-1 stills; deluge; chopsticks | Primary stills/pages opened this session, or file pages with EXIF | Fan renders, night-only, catch-only |
| High-altitude Earth | NASA balloon NTRS; ISS limb catalog; Red Bull Stratos 39 km; New Shepard window | Photos with stated altitude or spacecraft ID | Unsourced Pinterest, “AI sky” |
| Scattering | NOAA Jetstream; NASA Space Place; Gibbs Physics FAQ | Sources opened | Blog posts that only rephrase Wikipedia |
| Geography | Wikipedia Starbase (coords); Airbus Pléiades Neo 2022-05-05; wetland reporting | Opened satellite/encyclopedia pages | Unverified “1 km island” claims |

**Claim tags used below:** `[verified]` page opened this session · `[index]` search hit / filename confirmed, file page not opened · `[synthesis]` this brief’s reading of Exosphere vs photos · `[assumption]` not sourced.

**Name map (plan → live files):**

| `PLAN_VISUAL_REALISM.md` alias | Live file |
| --- | --- |
| `SkyController` | `scripts/SkyController.cs` + `assets/shaders/space_sky.gdshader` |
| `EarthGroundController` | `scripts/EarthGroundController.cs` + `assets/shaders/earth_ground.gdshader` |
| `PlumeSystem` | `scripts/PlumeSystem.cs` + `assets/shaders/raptor_plume.gdshader` |
| `LaunchPadController` | `scripts/LaunchPadController.cs` |
| (not named there) | `scripts/LaunchEffectsController.cs`, `scripts/PhaseLightingController.cs`, `scripts/SunController.cs`, `scripts/PlanetMaterials.cs`, `scripts/FloatingOrigin.cs`, `assets/shaders/earth_surface.gdshader` |

---

## 1. Frame-by-frame diagnosis (user play-camera stills)

Telemetry is taken from the HUD in each shot (`THR`, `Ap`, attitude). Visual claims are `[synthesis]` against those pixels and against opened references in §2.

### Frame A — Pad, `THR 100%`, `Ap 0 km`

**File:** `image-017b9277-b4fb-488c-a89e-37fe632a384d.png`

**What the frame shows:** Stack and tower on a pale, almost white-blue sky. Ground is a dark repeating ripple/tile to a **knife-edge horizon**. Tank farm and a long dark road read as grey boxes. **No engine plume, no steam wall, no ground cloud.** **No cast shadows** from tower, stack, or tanks. Lighting is ambient-only. 33/33 engines indicated.

**What an IFT T-0 still has, and this frame lacks:**

| Real still (see §2) | Missing in Frame A |
| --- | --- |
| Fused Raptor column under the OLM, photosphere-white core `[verified]` Space.com IFT-1 gallery; `[verified]` Space.com deluge article | Exhaust volume is absent at 100% throttle. Owner: `PlumeSystem` + `LaunchEffectsController`. |
| Horizontal steam/dust wall from water deluge (~T−17 s, hundreds of thousands of gal/min) `[verified]` Space.com 2023-07-28 deluge test; `[index]` Flight 13 flame-diverter T−17 s | Pad is dry. The N5 ground cloud exists in code and is marked “do not retune blindly” in `PLAN_VISUAL_REALISM.md`; **it is not reaching this camera**. |
| Hard sunlight, long shadows on steel and concrete; 18 mm daylight still of B7/S24 `[verified]` Wikimedia *Full Stack starship.jpg* (2023-04-16, 16:25 CDT, 1/1600 s, f/10, ISO 400) | No directional shadows. `EarthGroundController` sets `CastShadow = Off` on the local patch. Ground shader is `render_mode … unshaded`. Pad floodlights are `Visible = false` in daylight (`LaunchPadController.BuildNightFloodlights`) — correct for day, but the **sun path is not replacing them**. |
| Saturated blue zenith, paler horizon (Rayleigh + path length) `[verified]` NASA Space Place; NOAA Jetstream | Sky is a washed slab. Matches `SkyController` comment that the visible-band solar proxy was scaled down (`VisibleSolarRadianceScale = 0.35`) specifically to **avoid white-clipping the lower limb**, plus `space_sky.gdshader` `sun_disc_radiance = 32.0` and `sun_illuminance = 20.0`. The play camera is losing the blue, not the sun. |
| Wetland / beach / Gulf continuing past the industrial plot `[verified]` Airbus Pléiades Neo 2022-05-05; Wikipedia Starbase | Infinite tiled grey plane. The 700 m civil `Ground` box in `LaunchPadController.BuildConcretePad` is a **square apron**, not a coast. |

**Toy read:** powered rocket on a model-railroad board, lights off.

### Frame B — ~2 km `Ap`, chase / wide

**File:** `image-b245b19c-3ff8-42c2-8aea-86014843a6b1.png`

**What the frame shows:** Stack is a **black silhouette**. Exhaust is an **opaque white teardrop**. Land is a small brown **square** on a pale grey-blue sheet. Horizon is a straight line with a dirty yellow strip. Pitch ~89°.

**Vs IFT chase stills (Patrick T. Fallon / AFP frames on Space.com IFT-1 gallery `[verified]` page):** those frames show a bright, **wide** exhaust, vehicle **lit on the sun side**, and a **continuous** coastal plain — not a cookie. White teardrop is a single emissive cone (`PlumeSystem` merged column) with no steam/soot sheath and no illumination of the aft skirt.

**Toy read:** icon of a rocket, not a 121 m vehicle in Gulf air.

### Frame C — ~6 km, looking down

**File:** `image-53022641-1c97-4e57-a048-a4064ca18130.png`

**What the frame shows:** Isolated **square tile** of repeating gravel, grey pad cross in the centre, vehicle silhouette, a few soft white puffs. Background is empty haze. No coast, no Gulf, no horizon ring.

**Cause in repo `[synthesis]`:** two overlapping mistakes.

1. `LaunchPadController` spawns `Ground` as `BoxMesh` **700 m × 700 m**. At several kilometres nadir that box is a postage stamp — unless it is the **only opaque ground**, in which case it **is** the floating square.
2. `earth_ground.gdshader` ends with `ALPHA = fade * (1.0 - edge)` where `edge` is a smoothstep past `horizon_dist`. The shader also **washes** `ground_radiance` toward `haze_color` with a strong horizon term. If haze ≈ sky, the 450 km tangent patch (`PatchHalfUnits = 160_700` units ≈ 450 km) **disappears** and the 700 m apron remains. `PLAN_VISUAL_REALISM.md` V4.1 claims a ~1.6 km wetland skirt; **this camera does not show it**.

NASA balloon photography from ~30 km over Chesapeake `[verified]` NTRS 19710020434 records **continuous** coast and ~1260 km² per frame, not a square island. A 6 km nadir over Boca Chica must show **beach + tidal flats + SH 4 + Gulf**, not a cookie.

### Frame D — ~19 km nadir

**File:** `image-92198da8-4fdf-4940-8ab8-12e32c546d80.png`

**What the frame shows:** Same **square land** in a grey-blue void. Huge **overexposed sun glow**. Thick white haze. Sky not darkening.

**Cause in repo `[synthesis]`:** 19 km sits **inside** `FloatingOrigin.EarthVisualHandoffLowM … HighM` = **18–42 km**. Local patch `fade = 1 - EarthGlobeAlpha` is already dropping while the scaled globe is not yet a full disc in this look-down. Result: a fading square of procedural land, blown solar bloom, no continuous Earth. This is the documented handoff, failing the play camera.

At ~20 km a real nadir (balloon nested series, same NTRS report, frames from 30.5 km down to 9 km) still shows **geography and haze**, not a tile. Sky at 19 km is still **blue**, not white; you are only above ~90% of the mass of the atmosphere, not out of Rayleigh.

### Frame E — ~58 km, limb

**File:** `image-aa99e17e-f3e6-4e9e-8fa3-58303c10bb42.png`

**What the frame shows:** Lower half blurry tiled ocean/cloud. Upper half **blown white**. Thin blue strip. **Jagged white sawtooth** on the limb. `Ap 125 km`, pitch 90°.

**Vs physics and photos:**

- At ~58 km you are in the stratosphere. Overhead sky is **darkening**, not bleaching. Baumgartner’s 39 km Stratos jump is widely reported as **black overhead, blue below** `[index]` CNN / Red Bull interviews; treat colour as qualitative until a still is opened. ISS limb stills from ~350 km `[verified]` ISS023-E-57948 show **layered** orange–pink–**blue**–black, a **smooth** geometric limb, not a 1-pixel alias.
- White sawtooth is a **geometry/AA** failure of the scaled disc (`earth_surface.gdshader` limb terms `pow(limb, 5–13)` plus sky overexposure), not “more atmosphere.”
- Cyan/ozone edge is already **attempted** in `earth_surface.gdshader` (`atmosphere_limb`, Chappuis comment) and in `PLAN_VISUAL_REALISM.md` V4.1. **This frame does not show it** because the sky behind the limb is clipped to white.

**Toy read:** posterized planet in a blown skybox.

---

## 2. Reference board (copy this, not intuition)

Each row: openable URL, what to copy, which Exosphere frame it indicts.

| ID | URL | What to copy | Indicts |
| --- | --- | --- | --- |
| R1 | https://commons.wikimedia.org/wiki/File:Full_Stack_starship.jpg `[verified]` | Daylight stainless, **cast shadows**, OLM/chopsticks massing, **blue sky not white**, industrial plot **embedded in pale coastal ground**. EXIF: 2023-04-16 16:25, 18 mm, 1/1600, f/10, ISO 400, 25.996°N 97.154°W. | Frame A lighting, sky, pad-in-landscape |
| R2 | https://www.space.com/spacex-starship-1st-launch-april-2023-photos `[verified]` | IFT-1 **liftoff**: wide exhaust, spectators under **blue** sky, vehicle as a **streak with a bright base**, not a teardrop sprite. | Frames A–B plume + exposure |
| R3 | https://www.space.com/spacex-starship-water-deluge-system-first-test-video `[verified]` | Deluge as an **upside-down shower**: water **up and out**, white steam volume **wider than the vehicle**. IFT-1 without this **excavated the pad**. | Frame A missing steam wall |
| R4 | https://www.spacex.com/launches/starship-flight-13 `[index — fetch timed out]` | Current daylight IFT language (Flight 13, 2026-07-24, 17:51 CDT, Pad 2, 33/33). Use for **epoch** (OLP-2, V3) not for copying HLS legs. | Pad generation / chopsticks |
| R5 | https://space-solutions.airbus.com/newsroom/satellite-image-gallery/pleiades-neo/pleiades-neo-star-base-space-x/ `[verified]` | 30 cm, **2022-05-05**: launch site **on the water’s edge**, production site inland, **Gulf**, roads, **vegetation**, not a square cookie on infinite ocean. | Frames A–D geography |
| R6 | https://en.wikipedia.org/wiki/SpaceX_Starbase `[verified]` | 25.98750°N, 97.18639°W; adjacent to South Padre / Boca Chica; **wildlife refuge / tidal flats**; ~27 km east of Brownsville. Industrial footprint is **small**; the **landform is a peninsula**. | Cookie-island scale |
| R7 | https://ntrs.nasa.gov/api/citations/19710020434/downloads/19710020434.pdf `[verified]` | Balloon stills ~**30 km**, ~1260 km² per frame, **nested descent 30.5 → 9 km**. Coast stays **continuous**; haze lowers contrast. | Frames C–D nadir |
| R8 | https://eol.jsc.nasa.gov/SearchPhotos/photo.pl?mission=ISS023&roll=E&frame=57948 `[verified]` | ISS023-E-57948, 2010-05-25, 400 mm. Limb: troposphere orange/yellow, stratosphere pink-white, **blue** fade to space. **Smooth** horizon. (Orbital, not 58 km — use for **limb structure**, not curvature amount.) | Frame E |
| R9 | https://eol.jsc.nasa.gov/SearchPhotos/photo.pl?mission=ISS028&roll=E&frame=18218 `[verified]` | ISS028-E-18218: limb + **airglow** (~80 km+). Useful for “thin luminous layer,” not for daylight pad. | Frame E (what 80 km air looks like from above) |
| R10 | https://spaceplace.nasa.gov/blue-sky/ `[verified]` | Day sky is **scattered sunlight**; horizon **paler**; no mention of visible stars. | Frames A, D, E sky |
| R11 | https://www.noaa.gov/jetstream/clouds/color-of-clouds `[verified]` | Rayleigh: molecules ≪ λ, blue sky. Mie: droplets ~ λ, **white** clouds/steam. | Sky vs deluge colour |
| R12 | https://math.ucr.edu/home/baez/physics/General/BlueSky/blue_sky.html `[verified]` | Rayleigh ~ λ⁻⁴; Einstein 1911 molecules (not haze dust) suffice. Clouds/dust **white** (Mie). | Do not “grey out” the sky to fake distance |
| R13 | https://cimss.ssec.wisc.edu/satellite-blog/archives/51792 `[index — fetch timed out]` | GOES-16 IFT-1: **km-scale** steam/exhaust cloud, not a 10 m puff. | Frame A–B energy of the ground cloud |
| R14 | https://commons.wikimedia.org/wiki/File:StarshipLaunch_(cropped).jpg `[index]` | IFT-1 liftoff still (Osunpokeh, 2023-04-20 08:34 local). Open before grading plume colour. | Frames A–B |
| R15 | https://commons.wikimedia.org/wiki/File:SpaceX_Starship_IFT-1_NASA_WB-57_Cam_0.webm `[index]` | NASA WB-57 chase of IFT-1: **right altitude band** for 5–20 km side views (vehicle + Earth, not nadir cookie). | Frames B–D |

**Copy palette (play camera, daylight, `[synthesis]` from R1–R3 + R10–R12):**

- Zenith: medium blue, not `#d0d8e0`. Horizon: milky blue-white from long-path Rayleigh + Mie aerosols, **not** a yellow crayon stripe.
- Sun: small disc, **huge** bloom is wrong; air around the sun is brighter (forward Mie), ground still has **contrast**.
- Deluge: Mie-white, **horizontal**, optically thick for seconds, then a rising column. Does not paint the whole sky white.
- Raptor SL plume: short, **wide**, incandescent core, shock structure in the core, **not** a teardrop billboard.
- Land: beige/green **flats** + dark water inlets + pale Gulf; industrial grey is a **tiny** fraction of the frame even from 2 km.

---

## 3. Ordered fix list (environment vs plume vs lighting)

Do **environment and exposure first**. A correct plume on a white sky still looks fake. Do **not** retune the N5 deluge cloud in isolation (`PLAN_VISUAL_REALISM.md` bitácora).

### P0 — Environment (makes or breaks every frame)

| # | Fix | Owner | Why |
| --- | --- | --- | --- |
| E1 | **Stop the 700 m square from being the only ground.** Either hide/shrink the civil `Ground` box after ~1 km AGL, or make it **receive** the same fade as the tangent patch. | `LaunchPadController.BuildConcretePad` (`Ground` `BoxMesh` 700 m) | Frames C–D **are** that box. |
| E2 | **Do not alpha-kill the tangent patch into the sky.** `ALPHA = fade * (1.0 - edge)` plus aggressive `haze_color` mix is how a 450 km mesh becomes invisible. Keep the patch **opaque** to the geometric horizon; haze in **RGB**, not alpha. | `assets/shaders/earth_ground.gdshader` driven by `EarthGroundController` (`horizon_dist = sqrt(2 R h)`) | Frames A, C. V4.1 skirt never reaches the play camera. |
| E3 | **Handoff 18–42 km must not reveal a cookie.** At 19 km nadir, local fade and globe alpha currently leave a square. Need overlap that still looks like **continuous Earth** (globe already covering the patch footprint, or patch that does not have a square silhouette). | `FloatingOrigin.EarthGlobeAlpha` (18–42 km) + `PlanetMaterials` / `earth_surface.gdshader` + `EarthGroundController` fade | Frame D. |
| E4 | **Starbase geography, not a 1 km island.** Reconstruct **east=Gulf, west=flats/SH 4, south=Mexico barrier, north=laguna** at tens of km. Procedural marsh in `earth_ground.gdshader` (`site_core` 80–1400 m, `coastal_belt` to 48 km) is the right *idea* and the wrong *read* in the captures. | `earth_ground.gdshader` (`reconstructed_water`, `gulf`, `lagoon`) + launch-site lat/lon already in the Boca Chica profile (`docs/audits/STARBASE_RECONSTRUCTION_V1.md`: 25.9972°N, 97.1566°W) | Frames A–D. **Not** an 8K Texas photogrammetry project. |
| E5 | **Limb at 50–80 km: cyan line, no sawtooth, no white clip.** Lower sky energy at altitude; MSAA / disc coverage / premultiplied limb; keep Chappuis/cyan as a **thin** band. | `SkyController` (observer altitude → `space_sky.gdshader`) + `earth_surface.gdshader` (`limb_strength`, `atmosphere_limb`) + tonemap (`PhaseLightingController` keeps Filmic) | Frame E. |

### P0 — Lighting / exposure

| # | Fix | Owner | Why |
| --- | --- | --- | --- |
| L1 | **Daylight must cast shadows on pad.** Unshaded ground + `CastShadow = Off` + high ambient (`PhaseLightingController` `AmbientEnergyPad = 0.45`, sky ambient in `SkyController.UpdateEnvironment`) = Frame A. Need a **shaded** ground response to `DirectionalLight3D` *or* a shader-side N·L that still writes a shadow map. | `EarthGroundController` (`CastShadow`), `earth_ground.gdshader` (`unshaded`), `SunController` (orients light, **does not** write energy), `PhaseLightingController` (`SunEnergyPad = 1.5`) | Frame A, R1 |
| L2 | **Stop bleaching the sky to protect the limb.** `VisibleSolarRadianceScale = 0.35` and huge `sun_disc_radiance` fight each other: zenith dies, sun blob wins. Need altitude-dependent exposure, not a global dim. | `SkyController`, `space_sky.gdshader` (`sun_illuminance`, `sun_disc_radiance`, `VisibleSolarRadianceScale`), `PhaseLightingController` (glow 0 at pad → 0.6 in space) | Frames A, D, E |
| L3 | **Vehicle must have a sunlit side at 2 km.** Silhouette means ambient ≈ albedo or the stack is unlit. | `VesselRenderer` steel shader + `PhaseLightingController` ambient vs sun | Frame B |
| L4 | **Stars stay off in daylight.** Already `star_energy` gated (`SkyController` “Daylight pad/ascent: kill the starmap”). Do not “add stars at 58 km” while the sky is still bright. `[verified]` R10: daytime sky is scattered sunlight. | `SkyController` + `space_sky.gdshader` `star_energy` | Frame E (if anyone “fixes” white sky by showing stars) |

### P1 — Plume / pad FX (after the sky is blue)

| # | Fix | Owner | Why |
| --- | --- | --- | --- |
| P1 | **THR 100% on the mount ⇒ visible merged plume.** Frame A is a hard fail. Debug: is `PlumeSystem` parented below the OLM deck, clipped, or `Visible` false until `!IsGroundHeld`? Recent `fix/pad-sky-engine-cap` replaced exhaust **spheres** — confirm the replacement is in the play frustum. | `PlumeSystem.cs`, `assets/shaders/raptor_plume.gdshader`, hold-down path (`EngineStartupController` / `SimulationBridge`) | Frame A |
| P2 | **Deluge / ground cloud must exist in the same frame as 33/33.** Horizontal Mie-white wall, vehicle still readable (already an AmountRatio cap in `LaunchEffectsController`). If particles are emitting under the mesh, raise or add a guaranteed billboard bank that the play camera sees. | `LaunchEffectsController.cs` (N5 layers). **Do not** globally crank N5. | Frame A, R3, R13 |
| P3 | **Kill the white teardrop.** SL column: wide, short, core + sheath; smoke/soot only while `expansion` is low (`PLAN_VISUAL_REALISM.md` already documents this). | `PlumeSystem.SetupSH` / `BuildUnit` (`mouthR`, `energy`) + `raptor_plume.gdshader` | Frame B |
| P4 | **Plume lights the pad and aft skirt.** Omni at nozzle already exists in `PlumeSystem`; Frame A/B show zero bounce. Check energy, range, and whether unshaded ground ignores it. | `PlumeSystem` lights + L1 ground shading | Frames A–B |

### Explicitly later / not this brief

- Chopstick micro-detail vs IFT-13 OLP-2 (`LaunchPadController` V1.1 already has rails/sheaves; grade against R1/R4 **after** lighting works).
- Hot-stage, EDL, vacuum plume, map-view Earth.

---

## 4. Acceptance tests (what shots MUST NOT look like)

Capture with real framebuffer (`xvfb-run`, not `--headless`). Same play camera family as the user’s five stills. Compare to R1–R3 and R7–R8.

### Pad (T-0, `THR` 100%, stack on OLM)

**MUST NOT:**

- Show 33/33 and **no** exhaust volume.
- Show a **dry** concrete stage with no lateral steam/dust.
- Show **no** shadows of tower/stack on the apron at high sun.
- Show a **repeating tiled** plane to a perfectly sharp horizon.
- Show a **white** zenith (daylight IFT is blue).
- Hide the stack inside the deluge (existing N5 cap).

**MUST:**

- Read as Starbase: tower + chopsticks + OLM + tank farm **plus** flats/water in the same frame (R1, R5).
- Plume **wider** than the 9 m barrel at the mouth.

### ~20 km nadir (`Ap` ~19 km, pitch ~90°)

**MUST NOT:**

- Show a **square** of land.
- Show the 700 m apron as the dominant continent.
- Blow the sun into a disc that **erases** the ground.
- Show a cartoon two-tone grey/blue split.

**MUST:**

- Show **continuous** coast/water at tens of km (R5, R7).
- Keep a **blue** sky; stars still **invisible** (R10).
- Survive the 18–42 km handoff without a hole or a cookie (`FloatingOrigin`).

### ~58 km limb (`Ap` tens to >100 km, looking to horizon)

**MUST NOT:**

- Show a **jagged white sawtooth** limb.
- Show an **overexposed white** sky with a sticker-blue strip.
- Show stars competing with a still-bright limb (R10, R12).

**MUST:**

- Show a **smooth** geometric limb and a **cyan/blue** atmospheric line that fades to darker blue/black overhead (R8 structure; R12 physics).
- Keep HUD legible (existing glow rules in `PhaseLightingController`).

### Physics gates (for reviewers, not a new renderer)

- Day sky colour is **Rayleigh** (molecules, λ⁻⁴), `[verified]` R10–R12. Steam/clouds are **Mie** (white), `[verified]` R11.
- Stars are invisible because the **sky is bright**, not because the starmap is missing `[verified]` R10.
- 50–80 km is **not** space. It is stratosphere/low mesosphere; the limb is an optically thick **grazing** path, which is why it is a **bright thin band**, not a white aliased edge `[synthesis]` from R8 + R12.

---

## 5. Non-goals (this sprint)

- Photogrammetry or streaming 8K tiles of the whole Earth.
- Accurate cadastral Starbase / OLP-2 as-built (use **recognizable** peninsula + tower; R5 is the scale reference).
- Night launch, fog, lightning, or Flight 13-specific V3 livery.
- Rewriting `raptor_plume.gdshader` from scratch (tune mouths/energy; shader is an intentional asset per `PLAN_VISUAL_REALISM.md`).
- Cranking global ACES/glow (`PLAN_VISUAL_REALISM.md` V4: global tonemap **failed**; use phase lighting).
- Retuning N5 deluge “until it looks bigger” without a pad still.
- Breaking `[G]` ascent, hold-down, or HUD.
- Stars, Milky Way, or airglow as a pad/ascent feature.

---

## 6. Physics note (Rayleigh / Mie / limb) — for implementers

**Verified:**

1. **Blue day sky:** short wavelengths scatter more from **air molecules** (Rayleigh). NASA Space Place (R10); NOAA Jetstream (R11); Gibbs FAQ states Rayleigh intensity ∝ λ⁻⁴ and ~(700/400)⁴ ≈ 10 (R12).
2. **White steam/clouds:** droplet size ~ visible λ → **Mie**, all colours scatter similarly (R11, R12). Deluge must be white-grey, **not** Rayleigh-blue.
3. **Pale horizon:** long path, multiple scatter, surface bounce mix colours toward white (R10). Frame A’s **yellow crayon** band is not this.
4. **Stars:** drowned by scattered sunlight (R10). Do not fade in the 8K starmap until the **sky** is actually dark.
5. **Limb:** grazing rays integrate a long column → bright, coloured, **thin** shell. ISS023-E-57948 (R8) is the colour grammar (even though it is orbital). Frame E’s **white jagged** edge is rasterization + clip, not optics.

**Not independently opened this session (do not treat as primary):** Strutt 1899 *Phil. Mag.* paper; Einstein 1911 molecular-scattering paper (both **mentioned** in R12). Red Bull Stratos colour quotes `[index]`.

**Gap:** this sprint does not need a full Bruneton atmosphere rewrite if `space_sky.gdshader` already integrates Rayleigh/Mie LUTs (`SkyController` header). The play-camera failure is **exposure, ground alpha, pad box, and missing T-0 FX**, not “we forgot Rayleigh exists.”

---

## 7. Starbase geography (why the cookie is wrong)

`[verified]` Wikipedia Starbase: 25.98750°N, 97.18639°W, on the Boca Chica **subdelta**, next to the Gulf, wildlife/tidal-flat context, ~27 km east of Brownsville.

`[verified]` Airbus Pléiades Neo 2022-05-05: launch complex **left / water’s edge**, production **inland**, roads and vegetation **continuous**. The whole industrial site is a few kilometres, sitting on a **much larger** coastal landform.

`[verified]` SpaceNews (opened in search session / article fetch): scouting narrative of **marsh, no bedrock, water around** — not a desert mesa and not an atoll.

**Scale for art direction `[synthesis]`:**

| Feature | Order of magnitude |
| --- | --- |
| Stack height | ~121 m |
| OLM / tower plot | hundreds of metres |
| Civil fill / Highway 4 spur | ~1–3 km |
| Tidal flats + beach + Gulf in one wide shot | **tens of km** |
| “Cookie on infinite ocean” | **forbidden** |

Repo already knows Boca Chica as default (`STARBASE_RECONSTRUCTION_V1.md`). The play camera is not using that knowledge at 2–20 km.

---

## 8. Annotated sources (verification)

| Source | Opened? | Tier | Use |
| --- | --- | --- | --- |
| NASA Space Place, “Why Is the Sky Blue?” (updated 2022-08-29) | yes | agency explainer | Day sky, pale horizon, no stars |
| NOAA Jetstream, “The Color of Clouds” | yes | agency explainer | Rayleigh vs Mie |
| Gibbs, P. (1997). Physics FAQ, “Why is the sky blue?” | yes | FAQ citing Tyndall/Rayleigh/Einstein | λ⁻⁴, white Mie clouds |
| Wikimedia File:Full_Stack_starship.jpg (Hautmann, 2023-04-16) | yes | primary still | Pad daylight, shadows, sky |
| Space.com IFT-1 photo gallery (2023-04-20) | yes | journalism + Getty/AFP stills | Liftoff energy |
| Space.com deluge first test (2023-07-28) | yes | journalism | Steam wall |
| Airbus Pléiades Neo Starbase (2022-05-05) | yes | commercial EO | Geography |
| Wikipedia “SpaceX Starbase” | yes | encyclopedia | Coords, setting |
| NASA NTRS 19710020434 (1971) balloon photography | yes | NASA report | 9–30 km nadir |
| EOL ISS023-E-57948 | yes | NASA photo | Limb layers |
| EOL ISS028-E-18218 | yes | NASA photo | Limb/airglow |
| SpaceX Flight 13 official page | **timeout** | primary | Epoch only `[index]` |
| CIMSS GOES-16 IFT-1 blog | **timeout** | agency blog | Cloud scale `[index]` |
| Commons StarshipLaunch (cropped).jpg | **not opened** | primary still | `[index]` |
| Strutt 1899 / Einstein 1911 | **not opened** | primary physics | cited via Gibbs only |

---

## 9. Suggested implementation order (for the next agent)

1. **L2 + E5 exposure** so pad/20 km/58 km stills are not white. No new scattering model.
2. **E1 + E2 + E3** until 6 km and 19 km nadir **cannot** show a square.
3. **E4** wetland/Gulf read at pad and 2 km (procedural, site-local).
4. **L1 + L3** shadows and sunlit steel.
5. **P1 + P2 + P3** plume and deluge in the **same** pad still as 33/33.

Stop when pad / 20 km / 58 km pass §4. Do not start Earth photogrammetry.

---

## References (opened URLs)

Airbus Defence and Space. (2022, May 5). *StarBase SpaceX satellite image from Pléiades Neo*. https://space-solutions.airbus.com/newsroom/satellite-image-gallery/pleiades-neo/pleiades-neo-star-base-space-x/

Gibbs, P. (1997). Why is the sky blue? *Physics FAQ*. https://math.ucr.edu/home/baez/physics/General/BlueSky/blue_sky.html

Hautmann, J. (2023). *Full Stack starship* [Photograph]. Wikimedia Commons. https://commons.wikimedia.org/wiki/File:Full_Stack_starship.jpg

NASA Earth Observatory / JSC. (2010). *Sunset seen from the International Space Station* (ISS023-E-57948). https://eol.jsc.nasa.gov/SearchPhotos/photo.pl?mission=ISS023&roll=E&frame=57948

NASA JSC. (2011). *ISS028-E-18218* (Pacific Ocean, limb, airglow). https://eol.jsc.nasa.gov/SearchPhotos/photo.pl?mission=ISS028&roll=E&frame=18218

NASA Space Place. (2022, August 29). *Why is the sky blue?* https://spaceplace.nasa.gov/blue-sky/

National Oceanic and Atmospheric Administration. *The color of clouds*. https://www.noaa.gov/jetstream/clouds/color-of-clouds

Rabchevsky, G. A. (1971). *Earth photography from high-altitude balloons* (NASA CR-111456 / NTRS 19710020434). https://ntrs.nasa.gov/api/citations/19710020434/downloads/19710020434.pdf

Space.com. (2023, April 20). *Relive SpaceX's 1st Starship test flight in these incredible launch photos*. https://www.space.com/spacex-starship-1st-launch-april-2023-photos

Space.com. (2023, July 28). *SpaceX tests new Starship water-deluge system for 1st time*. https://www.space.com/spacex-starship-water-deluge-system-first-test-video

Wikipedia. (n.d.). *SpaceX Starbase*. https://en.wikipedia.org/wiki/SpaceX_Starbase
