# Starbase clouds and lighting audit

Status: bounded visual cloud transport implemented; final Flight 14 visual acceptance remains open.

## Reference and root cause

[Flight 14 footage](https://www.youtube.com/watch?v=lw0nj_XiUoU), particularly video
0–75 seconds, shows broken coastal cumulus, brighter lit faces, shaded bases and
clear geographic gaps. The rebroadcast changes camera and exposure; it does not
provide a measured cloud-height, water-content or optical-radiance dataset.
Cloud appearance and the mission reconstruction are separate contracts; see
[video feasibility](../research/STARSHIP_FLIGHT14_VIDEO_FEASIBILITY_2026-09-30.md).

The prior sky used a quaternion-derived texture frame, while the geographic Earth
surface uses a reflected full basis. The quaternion cannot represent the negative
determinant. Surface clouds were a flat weather-map overlay with Godot `TIME`
advection; sky clouds were an elevated field with simulation-time advection.
The local ground also covered sky cloud rays without composing those clouds.
These differences caused geography, depth and paused-clock disagreements.

## Change

- One metre-coordinate density field now serves sky, global Earth and local ground.
  Earth uses the full reflected texture basis and simulation-clock wind offset.
  Every surface consumer binds the same historical global weather map.
- Ground rays intersect an elevated shell, integrate Beer–Lambert opacity and
  receive cloud attenuation along the solar path. The cloud radiance is transported
  through the molecular foreground at a weighted mean distance.
- The explicitly labelled Starbase appearance profile uses a procedural 1.2–3.6 km
  envelope and kilometre-scale broken cells within a smoothly bounded region.
  Clear holes bypass the global-map horizon threshold softening. Its heights and
  density multiplier are visual approximations, not recovered meteorology.
- Bounded diffuse sunlight and a Rayleigh-column-derived sky fill distinguish
  warm lit faces from cooler shaded faces. Both are gated by daylight and solar
  visibility. Elevated samples resolve their own planetary shadow, independently
  of whether the surface observer can see the Sun. External eclipse attenuation
  uses the same atmosphere-body-excluding geometry as the sky. This is a
  higher-order scattering closure, not a droplet solver.
- Surface view integration uses 24 direction-stable stratified samples and four
  solar samples; the existing sky ceiling remains 24 view/five solar samples.
  Within the local core, empty space above its cloud envelope is skipped.
  Procedural core samples avoid seven unnecessary global weather/detail reads.

No pure-simulation code, mass, forces, atmospheric density, entry speed frame,
heating coefficients or coupled-6DoF defaults are changed.

## Verification

Solar-fixture validation was completed on 2026-10-01.

Compatibility six-altitude fixtures passed with real framebuffer images at
960×540, presentation Sun elevation 12°, using the existing Flight 12 vehicle:
`clouds-light-before` versus `clouds-light-v5`. Vessel altitudes are 0/12/15/18/
100/250 km; the camera sits approximately 716 m above the vessel. This is an
optical comparison fixture, not Flight 14 trajectory evidence.

Actual bound parameters report zero sky/global/local frame and paused-clock
errors, a full Earth determinant of −1 and shared coverage textures. Capture
inspection shows clear geographic gaps, cloud shading and continuity through
local/global terrain handoff. The coarse cloud morphology and residual far-edge
sample grain are still visible; exact reference appearance is not claimed.
Intermediate orange overcast (`v3`) and ridged sampling (`v4`) were rejected.

On this llvmpipe capture run the median frame time increased from 391 ms to
491 ms (approximately 26%). This is an exploratory software-renderer comparison,
not a controlled hardware-GPU benchmark or a realtime performance acceptance.
Skipping global-map reads within the procedural core improved the earlier
623 ms intermediate result. Profile actual target hardware before increasing
sample counts or adding a foreground pass.

Forward+ six-altitude fixtures also passed at 640×360 (`clouds-forward-final`),
with actual Vulkan renderer identity and the same frame/clock/texture gates.
The final capture also verifies identical atmosphere-body-excluding solar
visibility in the sky, local ground and global surface. The final Compatibility
twilight matrix (`clouds-twilight-final`, −1° presentation Sun, 640×360) passed
those same gates after the elevated-cloud solar-visibility correction.
Day/sunrise/sunset/night fixture captures now verify the actual bound sky Sun.
Their former green result was invalid: the inherited launch daylight override
kept the night shader at daytime while physical telemetry reported −35°. The
harness clears that override for physical fixtures and rejects missing, nonfinite
or mismatched bound solar states. The corrected night frame is dark with stars,
and the sunrise frame shows a twilight gradient.

The aggregate `tools/ci_check.sh` passed on the final game/shader revision:
both builds completed with zero warnings/errors, all 902 xUnit tests passed,
the flight reached 60 frames and main-menu/construction smoke loads succeeded.
Four solar-state regression cases run in CI; three reference-extraction tests
passed separately, including real synthetic-video decoding and source-hash
mismatch rejection. Temporary autoloads are restored by the capture harness.

Local review: `exports/flight14-cloud-light-review/index.html`; selected video
references: `exports/flight14-cloud-light-review/reference/index.html`.
These generated images and logs are deliberately excluded from Git.

## Limits and next work

The surface shader composes background cloud radiance at the ground's depth.
A foreground hull is therefore not fully occluded when it crosses a cloud;
a depth-aware foreground cloud pass remains required for convincing in-cloud
flight. Cloud shadow quadrature, weighted-distance molecular composition and
higher scattering orders remain bounded approximations. There is no measured
Flight 14 weather reconstruction, terrain-parallax shadow map or inferred wind
field. No actual Flight 14 continuous mission was flown by these optical fixtures.

Final comparison still requires matched onboard/ground camera placement,
field of view, sun azimuth, exposure and the actual vehicle state. The six-altitude
paused fixture checks geographic continuity and optical handoff; it cannot certify
Flight 14 dynamics or prove an exact photographic match.
