# Integrated GPU graphics preset — 2026-10-02

## Behavior and boundaries

Settings → Graphics Profile cycles Auto, Integrated GPU and Quality. The choice
is stored separately from interface settings in `user://graphics.cfg`. Invalid
values fall back to Auto. Auto uses Godot's adapter type; an integrated device
selects Integrated GPU, while discrete/unknown/software adapters select Quality.
Explicit selection overrides detection. Compatibility reports this Radeon as Other,
so Auto uses Quality there; choose Integrated GPU manually for that backend. The diagnostic environment variable
`EXOSPHERE_GRAPHICS_PRESET=integrated|quality|automatic` overrides loading without
writing the player's preference.

Integrated GPU uses 75% resolution per 3D axis (56.25% as many pixels), bilinear
upscaling, FXAA on Forward+/Mobile and no MSAA. Compatibility has no screen-space
AA support, so its integrated profile leaves that effect disabled. Quality restores native 3D resolution and 2× MSAA.
Viewport dimensions, CanvasLayer telemetry and UI scaling remain unchanged.
The policy applies at menu/flight startup and to the VAB's 3D preview viewport;
cockpit instrument viewports retain their native 2D rendering policy.

The lighter profile disables SSAO, SSIL and SSR in flight, and uses two solar
cloud quadrature segments instead of the Quality profile's four local segments.
Quality's global sky lighting retains its existing three-to-five segment policy.
Camera integration still uses 24 terrain and 32 sky segments. Density, placement,
extinction, weather clock, silhouettes and the opaque-ray cutoff are unchanged.
Sun shadows, atmospheric LUT values and HDR tone mapping are retained. No force,
mass, guidance, thermal state, timestep or time-warp code changes.

The tradeoff is a softer world image, less geometric edge smoothing and coarser
cloud self-lighting. This is a graphics preset, not a physical approximation or a
claimed solution to all integrated-GPU performance limits.

## Paired target-hardware measurements

Godot 4.6.3 mono, Forward+ Vulkan, AMD Radeon Graphics (RADV RENOIR), Ryzen 5
7530U integrated GPU, Mesa 25.2.8, desktop display `:0`, fullscreen 1920×1200.
Both runs use the same current shaders, fixed cloud-traversal fixture and normal
renderer (no accurate-breadcrumb barriers). `EXOSPHERE_RENDER_PROBE=1` enables
in-process GPU timings. Runs are sequential with no concurrent builds/tests.
The probe and desktop workloads affect timing; these are short deterministic
fixtures, not long-running gameplay benchmarks or a promised FPS level.

The table uses the median of the last four reported GPU samples before each
capture. Earlier transition samples are excluded; samples have backend latency.

| Fixed view | Quality GPU ms | Integrated GPU ms | Reduction |
| --- | ---: | ---: | ---: |
| Below clouds | 181.54 | 84.29 | 53.6% |
| Inside, hull occlusion disabled control | 1092.53 | 393.88 | 63.9% |
| Inside, hull occlusion enabled | 1054.04 | 379.73 | 64.0% |
| Above clouds | 553.64 | 231.27 | 58.2% |
| Cumulus domes | 824.62 | 368.28 | 55.3% |
| Orbital nadir | 464.59 | 205.20 | 55.8% |

Both runs completed all six views in 183 frames (`STARBASE_FAR_OK`). The
integrated run explicitly selected Auto, which resolved to Integrated GPU on this
Radeon. Vessel contrast attenuation was 78.5% integrated and 80.3% Quality.
Manual capture review confirms occupied-cloud attenuation and retained cloud
shapes, with softer ground/edge detail in the integrated preset. No new graphics
ring timeout or GPU reset appeared in the checked kernel window.

Separate startup pad fixtures passed for both profiles. Frame-14-onward medians
were 93.18 ms integrated (six samples) and 160.49 ms Quality (eight samples).
The asynchronous LUT worker was active, so this is startup evidence only.
Reported video resource memory was approximately 1.47 GB versus 1.71 GB; this is
Godot's resource counter, not a measurement of physical dedicated VRAM on the APU.

Dense clouds still require about 380 ms GPU frames in the occupied-cell fixture.
Further shader/geometry profiling is necessary for smooth gameplay; the preset
preserves visual occlusion rather than hiding the expensive cloud field.

Local ephemeral evidence:

- `/tmp/exo_play-integrated-preset-cloud/` and matching `.log.console`
- `/tmp/exo_play-quality-preset-cloud/` and matching `.log.console`
- `/tmp/exo_play-integrated-preset-pad/`, `/tmp/exo_play-quality-preset-pad/`
- `/tmp/exo-integrated-menu/graphics-1280*.png` and matching process logs

## Verification

The graphics menu case starts with a malformed preference, presses the actual
production control, checks 3D scale and MSAA restoration, verifies native UI size,
checks keyboard focus stays on the graphics action during modal replacement,
saves Quality, then starts a fresh Godot process and verifies the saved choice,
layout and Escape behavior. It runs in isolated user data under Xvfb; its llvmpipe
renderer is behavior/layout evidence, not Radeon performance evidence. The case
is included in the CI aggregate when Godot is available.

Refreshing the settings modal no longer queues a focus request to its immediately
re-suspended background button. Closing it normally still restores background focus.
The graphics case fails on that focus warning and checks the actual focus owner.

The full local CI aggregate passed: all three builds with zero warnings/errors,
949 xUnit tests, shader/performance contracts, asynchronous flight startup,
menu/VAB headless startup and the isolated graphics preference/restart case.
The first OpenGL smoke exposed Godot's unsupported screen-space-AA warning;
FXAA is now enabled only on Forward+/Mobile. The final AMD Compatibility pad smoke
passed (`integrated-preset-gl-final`, `SMOKE_OK`, 50 frames) without that warning
or shader errors. Compatibility retains disabled screen-space AA.

Primary API references:

- [Godot 3D resolution scaling and native 2D UI](https://docs.godotengine.org/en/stable/tutorials/3d/resolution_scaling.html).
- [Viewport render scale and antialiasing](https://docs.godotengine.org/en/stable/classes/class_viewport.html).
- [RenderingServer adapter type](https://docs.godotengine.org/en/stable/classes/class_renderingserver.html).
