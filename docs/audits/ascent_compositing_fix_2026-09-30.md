# Ascent compositor correction — 2026-09-30

## Scope and reference

This change implements the findings in [the preceding audit](ascent_surface_atmosphere_compositing_2026-09-30.md). It addresses the coastal terrain washout, foreground vessel occlusion and fixed cyan atmospheric shell reported by the user. The supplied terrain screenshot depicts the Starbase/Boca Chica imagery; Kennedy/Cape Canaveral retains its own site imagery.

The [NASA ISS photograph](https://science.nasa.gov/earth/earth-observatory/the-top-of-the-atmosphere-7373/) provides a qualitative reference for a thin atmospheric limb against black space. Its 400 mm lens and enhanced contrast prevent treating its pixel thickness as a calibration target for the game's chase camera. The atmospheric density profile follows the existing production atmosphere model, not a new Flight 14 trajectory or invented telemetry.

## Changes

Earth coverage now comes from a fullscreen spatial quad with an analytic camera-to-reference-surface ray intersection. The shader writes depth for the geographic hit, rather than the 50,000-unit proxy mesh. The ship, pad and atmosphere therefore retain their foreground order. Other planetary meshes retain their established proxy projection for depth-buffer precision. Their fragments are rejected when the shared geographic Earth ray test places them behind Earth. This avoids moving them into the poorly resolved far-depth bucket on Compatibility while preserving Earth occlusion. Distances beyond the camera's usable far range are monotonically compressed into the last 2% of that range; this is a rendering approximation with finite depth-buffer precision.

The Earth surface remains opaque at every camera height. The 12–18 km handoff only retires the local detail overlay. The overlay uses actual alpha coverage over Earth instead of substituting a haze colour into an opaque patch. Its depth matches the geographic surface with a 1.5 m visual decal offset. That offset does not move collision geometry, alter elevation or change vessel dynamics.

The fixed 1.016-radius atmosphere shell is removed. The existing spherical sky integrator owns rays outside the solid Earth silhouette. Ground rays integrate their finite camera-to-ground segment using a shared optical shader, 24 warped quadrature samples, RGB extinction and single scattering. Local and global ground both consume the sky's thermodynamic density LUT and solar-transmittance LUT when ready. The sky's observer direction and reference radius now follow the camera rather than the vessel. Scene depth fog is disabled on these surfaces to avoid applying another incompatible optical path.

Both Earth representations share NAIP site imagery, masks, no-data rejection and feathering. The global surface keeps measured coastal detail after local meshes retire. The imagery is anchored to the rotating launch site; the local patch offset follows vessel translation instead of dragging the raster underneath it. Shared LUT references are cleared when the sky scene exits; stable LUT bindings are not reassigned every frame.

## Verification

Actual framebuffer captures verified both Compatibility (960 × 540 diagnostic matrix) and Forward+ (640 × 360 maintained matrix). The Forward+ `ascent-optics-forward-v2` run passed all six fixture gates, with camera heights 715.7, 12,715.7, 15,715.7, 18,715.7, 100,715.7 and 250,715.8 m. The standard pre-ortho far-field run `ascent-surface-fix-v2` also passed its six gates. Each matrix finished 345 frames; their paused ORBIT labels do not establish orbital dynamics. A new `--ascent-optics` playtest mode captures six paused optical fixtures with the camera tracking the vessel. It checks framebuffer presence, clipping, real camera height and detail retirement. It does **not** claim stable orbit, aerodynamic entry or historical Flight 14 accuracy from a paused fixture.

## Limits

- The reference surface is a sphere using the live local ellipsoid radius, matching the existing optical sky approximation. It is not a global analytic ellipsoid ray tracer.
- Global ETOPO slopes still shade the surface. Global mountain silhouette displacement is omitted by the analytic reference-surface coverage; local 3DEP geometry remains present.
- Surface aerial transport uses single scattering. The existing sky also includes higher scattering orders; a residual horizon colour seam can remain.
- Global clouds remain a texture approximation. A complete volumetric cloud replacement and exact Flight 14 weather/lighting reconstruction are outside this correction.
- A thin magenta highlight remains at parts of the orbital limb and needs independent spectral/exposure calibration. Removing the fixed shell is not full photorealistic acceptance.
- Saturn rings retain the parent body’s established proxy depth and share its geographic Earth-occlusion mask. Distant bodies do not use the Earth far-depth compression, which cannot resolve their internal occlusion on the Compatibility depth buffer.
- Dynamics, thermal damage, entry forces and integrator equations are unchanged. Regression tests and a continuous ascent run assess that separation; optical fixtures alone cannot validate it.

## Reproduction

```sh
bash tools/visual_playtest.sh --ascent-optics --renderer forward_plus \
  --run-id ascent-optics-review --resolution 640x360 --max-runtime 600
bash tools/visual_playtest.sh --ascent-optics --renderer compatibility \
  --run-id ascent-optics-review-gl --resolution 960x540 --max-runtime 600
bash tools/visual_playtest.sh --ascent-optics --renderer forward_plus \
  --run-id ascent-optics-review --resolution 640x360 --verify-only
bash tools/ci_check.sh
```

Set `GODOT_BIN` to the installed Godot mono executable if necessary. The harness owns Xvfb and restores its temporary autoload on exit. Local review artifacts are in ignored `exports/ascent-compositing-fix/comparison.html`; raw telemetry is preserved alongside each image matrix. The earlier ad-hoc matrix retains obsolete filenames and correctly failed the standard 2–40 km validator; it is diagnostic evidence, not a standard-mode PASS. The maintained mode has correct optical-height labels and its own camera gates.

The full CI suite passed 902/902 tests with zero build warnings/errors and successful Godot smoke/startup checks. The visual contracts were changed deliberately: prior tests required the fixed atmosphere shell and an ALPHA limb, which contradicted the corrected opaque surface. Replacement contracts require bounded ground transport, geographic depth, shared profiles and single off-limb ownership. Source contracts remain distinct from GPU/framebuffer acceptance.

Forward+ and Compatibility preserve the same geometry and foreground ordering, but their exposure/sky prefilter paths produce different brightness. Their screenshots should not be treated as identical photometric output. All recorded rendering runs used software rendering in this environment; these tests do not certify physical-GPU frame rate.

## Continuous ascent evidence

`ascent-compositor-e2e` passed the production `--ascent --flight12` gate at 640 × 360, `ASCENT_ORBIT_OK`, 2,265 frames. Ignition, ascent, coast, insertion and Done transitions were observed. Insertion ended at 150,201 m altitude with apoapsis 172,569 m and periapsis 148,704 m, above the 140 km atmospheric top. The verifier rejected fallback teleportation and invalid physical states; this run used neither. Framebuffer milestones include pad, liftoff, Max-Q, hot staging, separation and orbit. The low-resolution HUD occludes substantial parts of these images, so the uncluttered optical matrix is used for visual comparison.

This fixture is the Flight 12 V3 scenario. It proves a continuous production trajectory through the revised compositor; it is not claimed to reproduce Flight 14's actual trajectory or timeline. No reentry dynamics acceptance is inferred from this ascent-only run.

```sh
bash tools/visual_playtest.sh --ascent --flight12 --run-id ascent-compositor-review \
  --resolution 640x360 --max-runtime 900
```

## Final rendering safeguards

The final Compatibility optical run `ascent-optics-final` passed all six cases at 960 × 540 after extracting the common geographic ray function. `ascent-depth-saturn-verified` passed its 170-frame Saturn gate, and manual inspection confirmed the planet disc remained visible with correct front/rear ring ordering. The earlier physical-depth attempt for distant bodies lost the disc in Compatibility; it was removed before publication. The continuous ascent and Forward+ optical matrix preceded this distant-body correction; the final Compatibility matrix checks the refactored Earth shader. Physical ascent equations were unchanged throughout.

The harness now rejects `SHADER ERROR`, shader compilation failure and `SCRIPT ERROR` even when a fallback material produces a nonblank PNG. A recorded invalid-shader run (`ascent-depth-saturn-final`) was rechecked with `--verify-only` and correctly rejected. The CI fixture accepts benign UID/audio diagnostics and rejects three shader/script failure diagnostics. Neither a state gate nor a nonblank image certifies reference fidelity.

Final CI was rerun after the geographic-depth scope correction and shader-health gate: 902/902 tests, zero warnings/errors, and successful startup/scene smoke checks.
