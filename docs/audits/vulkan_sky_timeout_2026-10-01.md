# Vulkan cloud traversal timeout fix — 2026-10-01

## Failure and diagnostic certainty

The supplied crash reports `VK_ERROR_DEVICE_LOST`, with the last imprecise GPU
breadcrumbs including `SKY_PASS`. Kernel logs identify a graphics-ring timeout
for the same Godot PID 955302 at 19:42:54, followed by an amdgpu ring reset and
recovery. A second Godot timeout occurred at 19:57:44 (PID 968193). The zero GPU
memory counters are unavailable instrumentation, not evidence of zero allocations.

The pad baseline did not reproduce the reset. The deterministic cloud-traversal
fixture did: entering the occupied cell repeatedly reset the AMD GPU. Accurate
breadcrumbs still ended at `SKY_PASS`, but **removing the sky did not prevent the
reset**. Removing all cloud transport let all six camera positions render without
a reset (its visual occlusion gate correctly failed because clouds were absent).
Consequently, the last breadcrumb is not proof that sky rendering was responsible.
The terrain-side shared cloud transport is implicated by these controls.

Sky-only optimizations, lower sky sample counts and omitting the sky's nested
solar march were insufficient. Bounding terrain-cloud transport and correcting
lookup sampling then allowed the same dense traversal to complete. The tests
establish a reproducible failing scenario and a passing fix; they do not isolate
which individual instruction originally hung or prove a Mesa/driver defect.

## Final changes

- Terrain/cloud transport terminates at transmission below 0.005 rather than
  continuing nested lighting in an effectively opaque ray. Residual throughput
  is below 0.5%; this is a renderer quadrature bound, not a dynamics change.
- Local solar cloud integration uses four quadratic segments in sky and terrain.
  Both sample the same density and extinction. Retain 24 terrain view segments;
  sky local view integration uses 32 tangent-warped segments covering the complete
  spherical interval. The old adaptive 96-step, 80 m path could stop before the
  ray reached its interval end. This is a visual sampling tradeoff; the simulation
  atmosphere and CPU order-four scattering model remain unchanged.
- Atmospheric LUT reads explicitly request mip level zero. These textures have
  no mipmaps, so filtering and physical LUT values are retained. Implicit
  derivatives inside divergent raymarch control flow have undefined results under
  Vulkan. Global weather's center tap also requests level zero; the existing
  latitude-aware mip-six prefilter taps remain unchanged.
- Vulkan cloud sky transport runs in the quarter-resolution pass (1/16 screen pixels).
  RGB carries linear cloud radiance; alpha carries Beer–Lambert transmission
  (1 means clear air). Full-resolution atmosphere, stars and solar disc remain.
  OpenGL retains the same bounded camera integral directly: Godot 4.6.3 emits
  invalid GLSL sampler constructors when `QUARTER_RES_COLOR` is used. The initial
  OpenGL smoke caught this compilation failure before publication. A compile-time
  renderer branch excludes the subpass render mode and its texture reads from GLES.
  Cubemap lighting uses six atmospheric view segments and omits fine cloud
  silhouettes. Pad remains realtime; the 45 km incremental handoff is unchanged.
- Empty coastal samples skip rotation/lobe/noise calculations using an enclosing
  radius of 1.40: maximum aspect 1.20 times footprint cutoff 1.05 plus maximum
  billow offset 0.11 gives `1.20 * (1.05 + 0.11) = 1.392 < 1.40`. No positive
  footprint can be rejected. Also defer unused global noise past the local-core
  return. A 100,000-point randomized bound check rejected no positive footprint.

No simulation forces, gravity, guidance, thermal physics, vehicle mass, or mission
state logic changed. No driver installation, watchdog setting, or permanent
renderer downgrade was made.

Primary references:

- [Godot sky passes and reduced-resolution clouds](https://docs.godotengine.org/en/stable/tutorials/shaders/shader_reference/sky_shader.html).
- [Vulkan shader implicit derivatives and divergent control flow](https://docs.vulkan.org/spec/latest/chapters/shaders.html).

## Validation environment and evidence

Godot 4.6.3 mono / Forward+ / Vulkan 1.4.318, AMD Radeon Graphics (RADV RENOIR),
integrated GPU on desktop X display `:0`, 1920×1200. Kernel 7.0.0-34-generic,
Mesa Vulkan 25.2.8-0ubuntu0.24.04.2. A temporary wrapper adds fullscreen and
accurate breadcrumbs. `EXOSPHERE_RENDER_PROBE=1` records in-process GPU timings.
Khronos validation layers are not installed; API validation is not established.
Extra GPU memory tracking was not enabled.

Evidence is retained under `/tmp/exosphere-gpu-crash-2026-10-01/` and isolated
`/tmp/exo_play-gpu-*` directories. These are local, ephemeral artifacts.

The first Xvfb control selected llvmpipe and is not AMD validation. The initial
AMD control failed capture dimensions because window decorations shrank the
framebuffer; fullscreen corrected this. Intermediate optics runs were deliberately
terminated and have failing gates/shutdown errors. Neither these nor the failed
cloud traversals count as final acceptance.

### Completed dense-cloud regression

`gpu-amd-bounded-surface-cloud`: all six below/inside/above/domes/nadir captures
completed (`STARBASE_FAR_OK`, 183 frames), with no device loss. Vessel contrast
fell from 0.46971 to 0.09212 with cloud occlusion enabled: 80.4% attenuation.
Manual inspection confirms the ship fades into the occupied cloud instead of
remaining opaque or disappearing through a geometry/depth workaround.

The unchanged pad baseline median was 962.321 ms GPU time over nine samples from
frame 14 onward. Intermediate sky reductions improved pad cost but still failed
traversal; those numbers do not establish a fix. The final-revision pad median was
144.635 ms using the same nine-sample/frame-14-onward procedure: 85.0% less GPU time (6.65x).
Both short pad runs used accurate breadcrumbs and cancelled the asynchronous LUT
worker at shutdown, so this is a matched startup fixture, not sustained gameplay FPS.

Dense traversal remains expensive (roughly 1 s GPU frames in this diagnostic
fixture); passing the watchdog regression does not mean the renderer is fully
optimized or that all future GPU/device-loss failures are impossible.

### Final-revision validation

- `bash tools/ci_check.sh`: clean builds, all source contracts, 949 xUnit tests
  passing and flight/VAB headless startup checks.
- `gpu-amd-normal-final-cloud`: all six captures passed with the normal Vulkan
  renderer **without** accurate-breadcrumb barriers, 183 frames. Occlusion contrast
  reduction 80.3% (0.46985 to 0.09245). Both previously failing occupied-cell
  views rendered; no new kernel graphics-ring timeout was recorded.
- `gpu-amd-final-pad`: matched Forward+ pad smoke passed, manual capture review
  retained the mapped launch complex, sky and clouds.
- `gpu-amd-final-flight12`: normal Forward+ smoke passed for Flight 12 V3 at
  `starbase_pad2`, the launch variant/site identified in the supplied crash.
- `gpu-amd-fixed-gl`: AMD OpenGL smoke passed after the compile-time fallback;
  manual capture review confirmed clouds and launch complex, and no shader errors
  were reported. This is pad rendering evidence, not a dense-traversal GL stress test.
- `gpu-amd-final-altitudes`: normal Forward+ matrix passed all six geographic
  rays at 0, 12, 15, 18, 100 and 250 km, 345 frames. Manual capture review
  retained the mapped terrain and planetary limb; no shader error/device loss.
  This is optical coverage, not an end-to-end Flight 14 trajectory test.

### Computer-performance observations

Live read-only checks found a Ryzen 5 7530U (six cores / twelve threads), integrated
Radeon graphics, AC connected, and the balanced platform profile. `amd-pstate-epp`
uses `balance_performance`; the `powersave` governor label alone does not imply
that CPU boost is disabled. Boost was enabled. About 8 GiB RAM was available,
memory PSI averages were zero, and the two live `vmstat` intervals had zero
swap-in/swap-out. Allocated swap therefore does not establish current memory
thrashing. Shader GPU times, rather than the user's CPU-only screenshot, identify
the dominant graphics cost in the measured fixtures.

System tuning cannot substitute for reducing this renderer's work. A future
graphics profile should expose 3D resolution scale, clouds and screen-space
effects while retaining simulation step rates and forces. For example, a 0.75
3D scale renders 56.25% as many pixels, with a sharpness tradeoff and unchanged
2D UI resolution; it does not promise the same FPS improvement. No host power,
swap, driver, or kernel settings were changed.

References for performance interpretation:

- [Godot 3D resolution scaling](https://docs.godotengine.org/en/stable/tutorials/3d/resolution_scaling.html).
- [Linux AMD P-State/EPP](https://docs.kernel.org/admin-guide/pm/amd-pstate.html).
- [Linux memory pressure measurements](https://docs.kernel.org/accounting/psi.html).

The wide cloud-dome capture retains coherent cloud cover and mapped coastlines,
but visible quadrature contours and overly soft silhouettes remain. Visual
realism refinement is separate from this watchdog fix; these captures are not
photorealism acceptance.
