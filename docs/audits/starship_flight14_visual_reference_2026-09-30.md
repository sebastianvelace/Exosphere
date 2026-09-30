# Starship Flight 14 early-flight visual comparison

Date: 2026-09-30. Status: scoped reference improvement verified in real framebuffer; photographic match remains incomplete.

## Scope and reference reading

Eight user-provided images live under `/home/sebasvelace/.codex/attachments/3e4d8d05-3d9c-4ec1-9769-9e0fea42652d/`.
The broadcast labels on images 2-8 identify Flight 14. The official [SpaceX Flight 14 page](https://www.spacex.com/launches/starship-flight-14) corroborates the mission context. Image 1 has no broadcast timestamp; its precise exposure and acquisition time are unverified.

| Image | Visible state | Visual evidence | Implementation implication |
| --- | --- | --- | --- |
| 1 (4096x2304) | Wide liftoff photo | Two large irregular white/grey lateral cloud banks; warm inner light; slim pale rose exhaust; dark Ship, bright steel booster; coastal wetlands | Preserve a distinct ground cloud and airborne exhaust; compare a wide pad-tracking frame |
| 2 | Broadcast T+0 | White steam obscures the engine deck; low orange/yellow exhaust light; clear upper stack and tower | Source illumination belongs near grade, not over the full hull |
| 3 | Broadcast T+0, adjacent frame | Lobe boundaries and shaded interiors change while the vehicle silhouette remains stable | Use spatial density and evolving volumes, rather than uniform rectangles |
| 4 | Broadcast T+1 | Cloud rises unevenly and is strongly lit near the exhaust, with dark pockets farther away | Attenuate illumination with distance and optical depth; retain grey shadowed water |
| 5 | Broadcast T+7, 65 km/h, 0.1 km (rounded HUD) | High-angle view exposes the outgoing flow and flame underneath the vehicle | Anchor the cloud to the launch site and retain a separate ground interaction source |
| 6 | Broadcast T+10, 122 km/h, 0.2 km | Pale rose merged exhaust becomes visible as the booster clears the tower; cloud remains below | Lengthen the merged optical column without inflating its radius |
| 7 | Broadcast T+18, 259 km/h, 0.5 km | Airborne column remains narrow, with fine turbulent edges; cloud is detached below | Do not attach pad water to an ascending vehicle or use dense smoke in vacuum |
| 8 | Onboard T+30, 520 km/h, 1.7 km | Dark hexagonal TPS, directional steel highlights and reflected lower-stage exhaust; persistent ground cloud; coastline | Preserve PBR steel/TPS; localized engine illumination; onboard composition is a separate comparison |

The broadcast speed/altitude values are displayed measurements rounded by the broadcast HUD. They are not sufficient to recover vehicle masses, throttling, wind, camera optics, or a flight trajectory. No physical parameter is inferred from pixel colour or adjusted to force agreement with those readouts.

## Baseline evidence

The closest existing hardware fixture is the Flight 12 V3/Raptor 3 vehicle at Starbase Pad 2. This work improves the shared Starship presentation; it does not relabel the Flight 12 physics dataset as a verified Flight 14 vehicle. The repo has no independently sourced Flight 14 mass/engine/mission dataset.

Baseline command:

```bash
bash tools/visual_playtest.sh --flight12 --launch-track --camera-preset liftoff_wide --sun-elevation 12 --run-id flight14-before --resolution 1920x1080
```

The 12-degree visual Sun override is a controlled comparison parameter, not a recovered Flight 14 solar epoch. The physical Sun and forces remain unchanged.

Artifacts: `/tmp/exo_play-flight14-before/` and `/tmp/exo_play-flight14-before.log`.
The run reached `SUMMARY reason=LAUNCH_TRACK_OK frames=165` with genuine llvmpipe/OpenGL framebuffer PNGs, but its overall gate failed: the original wide preset frames tower-clear/early-ascent at about 17% of image height, below the existing 18% readability requirement. Physical completion is not visual acceptance.

Observed differences:

- The wide cloud is a pair of nearly featureless bright cards plus an orange patch, unlike the shaded, irregular reference banks.
- Pad steam sheets are solid untextured quads, visible as geometric patches.
- FogMaterial/FogVolume objects are created even on Compatibility, producing an unsupported fog-shader error. That renderer cannot render native volumetric fog ([Godot renderer documentation](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html)).
- LaunchEffects uses commanded throttle, so its visible source can disagree with delivered thrust during engine startup or failures.
- The ground-cloud position follows the point directly below the vessel rather than the fixed launch site.
- Cloud growth follows render delta, while physical progress on llvmpipe is much slower; reference-style transient timing then depends on hardware speed.
- The airborne merged exhaust is short and tapers into a small white cone.
- Steel receives orange emission across the entire hull using a view-space normal against a world-space direction, independent of source distance. This makes the booster look copper.

## Changes and physical boundary

1. Replace distance-specific cards and renderer-specific uniform fog with 12 noisy ellipsoid optical volumes (`PadSteamCloud`, `pad_steam.gdshader`). Both renderers use the same spatial shader. Ray integration uses metre-scaled path length, Beer-Lambert extinction, three sunlight extinction samples, cool shadow fill and distance-attenuated orange source lighting. Local water composites after the planetary transparent background and exhaust, while opaque depth still occludes it. The extinction coefficient, lobe sizes and source radiance are appearance calibrations, not measured water flow, temperature or humidity. Geometry and noise are deterministic visual proxies, not CFD.
2. Guard the reverse-Z background and degenerate homogeneous depth before clipping integration at reconstructed opaque scene depth, using the correct OpenGL versus Vulkan NDC convention ([Godot depth reconstruction](https://docs.godotengine.org/en/stable/tutorials/shaders/advanced_postprocessing.html)). This avoids cloud disappearance when a proxy back face is below the ground, while respecting foreground hull/terrain occlusion.
3. Tie production of steam to delivered engine readouts and a pad-launched episode; an approaching airborne vessel cannot freshly arm a launch deluge cloud. Leave condensed water behind as the source rises and dims, with slower optical dispersal. Growth and source smoothing use simulation seconds; physical pause freezes their evolution.
4. Use the actual launch-complex geodetic transform for the cloud. Keep independent dust near grade and avoid the superseded overlapping opaque steam layers.
5. Remove the pad's untextured deluge rectangles. Keep its well flame and local light, driven by delivered engine readouts, with intensity fading out as ground impingement ceases.
6. Extend the Super Heavy optical column to roughly the stack length, reduce radial broadening, wash out overly legible repeated shock cells in the merged plume, and use a restrained reference-calibrated pale rose sea-level tint. Limit the visible near-pad jet at its intersection with the fixed grade plane along the nozzle axis, leaving ground impingement to the separate fan/cloud. These changes apply to merged booster plumes; individual Ship/vacuum controls remain intact. Tint is an observed appearance calibration, not a gas-temperature measurement.
7. Remove the whole-hull orange steel emission. Direct sun and engine-bay lights still use the PBR lighting path, including actual source position and falloff.
8. Tighten the wide capture preset from 32 to 28 degrees vertical FOV to keep the stack readable, then move to a low angle with the stack against the sky, as in reference 1. Clamp the presentation eye above local grade before release. Apply the corrected look target and grade floor to the normal automatic liftoff camera as well; remaining small-resolution UI occlusion is documented below. The physical camera tracking and gate thresholds are unchanged. The v4 comparison used the original 32-degree aerial preset; v5 used a tighter aerial view; the final comparison uses the explicitly refined low angle.
9. Add `VISUAL_DELUGE` milestone telemetry alongside existing engine readouts, so source power, age, optical weight and launch-site anchoring are reviewable.

The new optical model does not change forces, integrators, masses, propellant, engine thrust/Isp, atmosphere, guidance or thermal protection. The distinction between water interaction and hot exhaust follows the multiphase launch-cloud problem documented by [NASA LAVA](https://www.nas.nasa.gov/SC24/research/project08.php); the rendering proxy does not reproduce that CFD calculation.

## Acceptance and remaining visual limits

Acceptance requires real final PNG inspection at matching resolution, camera preset, solar setting and comparable physical altitude; visible lobe shading instead of flat white cards; readable stack; a longer, narrow merged column; and neutral steel outside local engine illumination. Startup must follow delivered engines, the cloud must remain on the pad, and the normal ascent must reach the 1 km gate.

References show photographic sky/cumulus weather, detailed Pad 2 structures and an onboard downward view. The existing wide gameplay shot has different camera position, HUD, weather and steel surface detail; it is not a pixel-equivalent photograph. Their exact meteorology, lens/exposure, S41 markings and hardware details are not established by this change. Improvements to those areas should be source-backed, measured separately and should not be claimed from plume tests.

## Validation results

The unchanged simulation suite has passed 897/897 tests, zero failures. Both project builds have passed with zero warnings/errors.
The low-angle ground-safe run passed the full `LAUNCH_TRACK_OK` gate at 1920x1080, including whole-stack framing at tower-clear/early-ascent and 33/33 delivered engines. The initial aerial baseline and v4 failed readability; v5 passed framing but the limb-green heuristic sampled wetlands in the aerial composition. These were not final acceptance runs.
The 640x360 depth-disabled diagnostic retained the sharp far-bank cut, excluding scene-depth sampling as its cause. Raising local condensed-water compositing priority above planetary transparency and exhaust removed the cut in the 1280x720 composition run. Opaque hull/terrain clipping remains enabled; this is not a return to opaque cards. The final `flight14-accepted` run passed at 1920x1080 (`LAUNCH_TRACK_OK`, 165 frames) and its PNGs were inspected at startup, liftoff, tower-clear and early ascent. Both shaded cloud banks remain complete; the narrow rose column is distinct from the ground fan; steel no longer receives whole-hull orange engine emission.
Artifacts are preserved locally under `exports/flight14-visual-review/` (gitignored).
Intermediate v2 reached the physical 1 km milestone but exposed a depth-test defect; it is not final acceptance evidence.
The fast v3 640x360 run passed `LAUNCH_OK` and confirmed the repaired optical depth path. It is a diagnostic capture, not the target-resolution comparison.

The comparable original-camera software-rendering samples recorded median frame times of 1831 ms (baseline) and 1948 ms (v4). This is one noisy llvmpipe comparison, not a hardware-GPU performance certification. The optical proxy increases shader sampling; production GPU cost remains unmeasured.

Final reference-preset reproduction:

```bash
bash tools/visual_playtest.sh --flight12 --launch-track --camera-preset liftoff_wide --sun-elevation 12 --run-id flight14-accepted --resolution 1920x1080
```

Final inspected liftoff: `exports/flight14-visual-review/accepted/exo_play_liftoff.png`.
The local `comparison.md` includes the original-camera before/intermediate pair, final composition and all eight supplied reference images. No percentage similarity or CFD validation is claimed.

The final pure-cloud fixture also rendered with OpenGL and Forward+ at 960x540, with a 500,000-unit far plane and foreground pole. The depth-clipped pole remains visible and the water retains shaded structure against clear sky. This verifies the optical shader path in both renderers, not a full Forward+ flight matrix.

The no-preset automatic-camera run `flight14-production` passed `LAUNCH_TRACK_OK`
at 1280x720 through 1.1 km, with geometric stack height fractions of 0.241 at
tower-clear and 0.300 at early ascent. Its PNGs were inspected: the existing
720p HUD overlays part of the lower hull/exhaust. This gate proves projected
framing, not freedom from UI occlusion; the reviewed 1920x1080 reference-preset
images are the presentation acceptance evidence. HUD scaling at small resolutions
remains a separate gap. No gate tolerance was relaxed.

Validation also passed the relevant plume-delivery, orbital-plume, material,
lighting, camera, telemetry, launch-effects cadence and harness contracts. Temporary
autoloads/scripts were removed and `project.godot` restored. The final game build
passed with zero warnings/errors after harness cleanup.

Final headless MainMenu and Construction load/quit checks passed with no ERROR or SCRIPT ERROR entries; these are scene-load checks, not framebuffer evidence.
