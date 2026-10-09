# Entry advance presentation — 2026-10-09

## Cause and change

The exploration HUD deliberately suspends viewport 3D rendering while advancing
continuously to entry. Without a replacement background, the viewport clear
color produced a flat gray flight view, although simulation and telemetry were
still running.

The shortcut now holds a darkened flight image behind live telemetry and a
centered progress card. The mission clock and coast/deorbit/entry phase labels
come from the current simulation state. No percentage or estimated wall-clock
completion time is synthesized. Cancellation and arrival restore 3D rendering.

One clean world frame is drawn and read back when the user requests the shortcut.
The UI CanvasLayer is hidden for that draw and restored in a finally block, so
the held image contains no duplicate HUD when the window is resized. The image
is reduced to 640 pixels wide while retaining its aspect ratio. No world draw or
GPU readback is added to the advancing frame loop; the headless renderer skips
capture. Simulation propagation, step sizes and entry conditions are unchanged.

## Verification

A native Linux OpenGL framebuffer playtest used the integrated preset and the
actual menu → exploration route. It propagated from liftoff through all 26
payload releases, activated the shortcut, resized the window, cancelled, resumed
the shortcut, reached entry, restarted and returned to the menu.

- Reviewed 1280×720 and 1920×1080 captures: clean held world image, live HUD,
  and progress card clear of the controls and bottom telemetry band.
- Observed all three progress phases: orbital coast, deorbit maneuver,
  approaching entry.
- Cancellation restored 3D and held mission elapsed time constant while paused.
- Entry restored 3D and paused at 119,997.10911045 m altitude.
- Restart replaced the flight; returning to the menu removed its presentation.
- The initial shortcut request took 134 ms in this single native run, including
  its one-off draw/readback. This is not a general performance benchmark.
- Before and after the clean-image refinement, entry state matched exactly in
  mission elapsed time (9,546.3800001156 s), altitude, mass (191,120.028310322 kg),
  vertical speed (−92.2719156194526 m/s) and released payload count (26).
- A separate native Forward+/Vulkan check on AMD RADV RENOIR passed clean
  capture, cancellation and return to the menu. Its one-off request took 154 ms;
  no device loss or engine errors were reported. Reviewed its framebuffer at
  1280×720. Evidence: `/tmp/entry-vulkan-check.log` and
  `/tmp/entry-progress-vulkan.png`.
- Final game build: zero warnings and errors.
- Full `tools/ci_check.sh` passed: 1,059 xUnit tests, source/data contracts,
  asynchronous flight startup, headless scene smokes and the 1280-pixel graphics
  menu framebuffer check.

Evidence: `/tmp/entry-progress-final.log`, `/tmp/entry-progress-1280.png`,
`/tmp/entry-progress-1920.png`, and phase/cancellation/arrival captures under
`/tmp/entry-progress-*.png`. These are local playtest artifacts, not release assets.
Windows GPU validation and a rebuilt Windows download are outside this change.
