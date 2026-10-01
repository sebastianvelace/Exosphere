# Ascent compositor correction — 2026-09-30

## Scope and reference

This change implements the findings in [the preceding audit](ascent_surface_atmosphere_compositing_2026-09-30.md). It addresses the coastal terrain washout, foreground vessel occlusion and fixed cyan atmospheric shell reported by the user. The supplied terrain screenshot depicts the Starbase/Boca Chica imagery; Kennedy/Cape Canaveral retains its own site imagery.

The [NASA ISS photograph](https://science.nasa.gov/earth/earth-observatory/the-top-of-the-atmosphere-7373/) provides a qualitative reference for a thin atmospheric limb against black space. Its 400 mm lens and enhanced contrast prevent treating its pixel thickness as a calibration target for the game's chase camera. The atmospheric density profile follows the existing production atmosphere model, not a new Flight 14 trajectory or invented telemetry.

## Changes

Earth coverage now comes from a fullscreen spatial quad with an analytic camera-to-reference-surface ray intersection. The shader writes depth for the geographic hit, rather than the 50,000-unit proxy mesh. The ship, pad and atmosphere therefore retain their foreground order. Other planetary meshes retain their established proxy projection for depth-buffer precision. Their fragments are rejected when the shared geographic Earth ray test places them behind Earth. This avoids moving them into the poorly resolved far-depth bucket on Compatibility while preserving Earth occlusion. Distances beyond the camera's usable far range are monotonically compressed into the last 2% of that range; this is a rendering approximation with finite depth-buffer precision.

The Earth surface remains opaque at every camera height. The 12–18 km handoff only retires the local detail overlay. The overlay uses actual alpha coverage over Earth instead of substituting a haze colour into an opaque patch. Its depth matches the geographic surface with a 1.5 m visual decal offset. That offset does not move collision geometry, alter elevation or change vessel dynamics.

The fixed 1.016-radius atmosphere shell is removed. The existing spherical sky integrator owns rays outside the solid Earth silhouette. Ground rays integrate their finite camera-to-ground segment using a shared optical shader, 24 warped quadrature samples, RGB extinction and single scattering. Local and global ground both consume the sky's thermodynamic density LUT and solar-transmittance LUT when ready. The sky's observer direction and reference radius now follow the camera rather than the vessel. Scene depth fog is disabled on these surfaces to avoid applying another incompatible optical path.

Both Earth representations share NAIP site imagery, masks, no-data rejection and feathering. The global surface keeps measured coastal detail after local meshes retire. The imagery is anchored to the rotating launch site; the local patch offset follows vessel translation instead of dragging the raster underneath it. Shared LUT references are cleared when the sky scene exits; stable LUT bindings are not reassigned every frame.

## Limits

- The reference surface is a sphere using the live local ellipsoid radius, matching the existing optical sky approximation. It is not a global analytic ellipsoid ray tracer.
- Global ETOPO slopes still shade the surface. Global mountain silhouette displacement is omitted by the analytic reference-surface coverage; local 3DEP geometry remains present.
- Surface aerial transport uses single scattering. The existing sky also includes higher scattering orders; a residual horizon colour seam can remain.
- Global clouds remain a texture approximation. A complete volumetric cloud replacement and exact Flight 14 weather/lighting reconstruction are outside this correction.
- A thin magenta highlight remains at parts of the orbital limb and needs independent spectral/exposure calibration. Removing the fixed shell is not full photorealistic acceptance.
- Saturn rings retain the parent body’s established proxy depth and share its geographic Earth-occlusion mask. Distant bodies do not use the Earth far-depth compression, which cannot resolve their internal occlusion on the Compatibility depth buffer.
- Dynamics, thermal damage, entry forces and integrator equations are unchanged. Regression tests and a continuous ascent run assess that separation; optical fixtures alone cannot validate it.

## Validation

The compositor was inspected in real Compatibility and Forward+ framebuffer captures from the coast to 250 km, and in a continuous Flight 12 V3 ascent that reached stable orbit without teleport fallback. These observations are not Flight 14 trajectory or weather telemetry. Source contracts cover opaque geographic coverage and bounded optical transport; GPU compilation and appearance require actual rendering. Capture workflow and exact run evidence are documented in the follow-up work unit.
