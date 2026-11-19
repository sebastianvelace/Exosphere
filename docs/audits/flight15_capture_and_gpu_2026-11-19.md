# Flight 15 capture, orbital return, and integrated GPU

## Ship capture

The 70 km entry chapter reproduced a crash after both pins touched the cradle.
A descent-rate gate evaluated on each control step resumed thrust during compression.
The first two-pin contact became a single-pin contact as the ship moved beyond the
5 m footprint. The contact trace is opt-in via `EXOSPHERE_TRACE_CATCH=1`.

Guidance now checks both actual pin centres against that same footprint and measures
clearance along the cradle normal. A committed cutoff leaves the finite shutdown and
contact integrator in charge of the settling phase. The settle timer requires two pins
in contact, the existing relative-speed limits, and loads and compression inside
the existing limits. A static 200 t regression catches the previous false positive.
The V3 preset declares 2.5 MN/m per pin as an estimated effective compliance. At
225 t the two springs deflect about 0.44 m under static Earth gravity. The existing
0.60 m travel and 3 MN load limit per pin remain in effect.

The nominal seed reached `CAUGHT` at all three render cadences with two contacts.
Compression and the highest of the two pin loads below are from the settled sample.

| Render cadence | Mass | Compression | Pin load |
| --- | ---: | ---: | ---: |
| 50 Hz headless | 208.1 t | 0.430 m | 1.029 MN |
| 60 Hz with framebuffer | 198.2 t | 0.367 m | 0.921 MN |
| 120 Hz headless | 207.0 t | 0.425 m | 1.062 MN |
| 60 Hz, initial point 5 m farther upstream and north | 207.3 t | 0.394 m | 1.029 MN |

The runs took different amounts of propellant through the terminal phase.
The measured settled loads do not establish real vehicle or tower limits.

The entry state and compliance are engineering estimates. This chapter starts from
its own 70 km state and uses the existing demonstration attitude guidance before
cutoff. Continuous orbital return and booster diversion remain separate work.

Run the full chapter with:

```sh
CAPTURE_MENU_MODAL=flight15catchverify "$GODOT_BIN" --headless --path .   --fixed-fps 60 --script tools/capture_menu.gd
```

Run the same command on a real framebuffer and set `CAPTURE_MENU_OUTPUT` to a PNG
path to draw a frame after the physical `CAUGHT` event.

## Orbital return feasibility

The simulated ascent and all 26 releases produced a 237.381 × 296.885 km
orbit at T+4,660.02 s, with a 5,382.45 s period and a 210.523 t ship. A temporary
study propagated this orbit for a window of one period centred on T+21:35:57.
It tried retrograde impulses targeting periapses between −90 and +50 km, then
propagated the resulting inbound 120 km crossing to a 2.5 km altitude gate with
the existing reduced-order entry model. Fixed bank angles ranged from −75° to
+75°. The shallow-bank cases reached the predictor’s 80 m/s speed floor above
the altitude gate. The expanded 60 s grid produced 1,260 candidates that passed
the altitude gate. The closest passed 93.65 km from Starbase with 71.26 m/s of
retrograde impulse, a 35 km vacuum periapsis, and +75° bank.

Three local refinements around the 10 s grid minimum reduced the closest
modeled pass to 1.45 km from Starbase at 2.5 km altitude. The candidate burn is
at T+78,552.89 s, with 63.17 m/s retrograde impulse, a 53.33 km vacuum
periapsis, and +62.89° bank. Its inbound interface is at T+79,972.82 s and
7.44 km/s airspeed. This is a starting point for a closed-loop return trial
from the active ship.

This study assumes a Keplerian coast, an instantaneous burn, constant bank,
and a spherical projection onto the rotating Earth. It estimates a search
window for a continuous return.

### Finite-burn trial

Run the pad-to-entry diagnostic from the repository root.

```sh
dotnet run --project tools/Flight14TrajectoryProbe -c Release -- \
  data /tmp/exo-flight15-return --flight15-return
```

The diagnostic keeps the original ship and 26 released satellites in the
isolated-Earth universe. It coasts with the ordinary ×100 warp scheduler after
deployment, then uses the 20 ms control cadence for alignment, deorbit, and
entry. The return estimate keeps the main-tank reserve and uses a signed lift
bank about the air-relative velocity.

The ignition command at T+78,549.00 s reached the osculating periapsis target
at T+78,554.10 s. Thrust then took 0.24 s to fall below the shutdown threshold.
The one-engine burn used 4,040.28 kg and left a 48.30 km radial periapsis after
shutdown, below the 53.33 km command. The physical 120 km geodetic interface
was reached at T+79,862.48 s at 7.42 km/s and −125.6 m/s vertical speed.
At the descending 70 km geodetic witness the ship still had 7.02 km/s airspeed
and was 1,127 km from Starbase along the surface. The independent catch chapter
begins at about 1.80 km/s. The full approach needs
a guided hypersonic descent, geographic targeting, and validation of heating
and control authority before handing off to terminal guidance.

A separate trial with the same commands and a 2.5 km diagnostic altitude
repeated the burn and 120 km entry. At T+80,740.20 s it reached 2.5 km at
26.6965° N, 94.9326° W, about 235 km from Starbase by spherical great-circle
distance. Airspeed was 120.6 m/s, vertical speed was −120.1 m/s, and the
angular rate in the adjacent integration sample was 0.51 rad/s. This is an
altitude crossing with substantial geographic and attitude errors. Between
44.7 and 30.7 km, the sampled angular rate climbed from 0.003 to 0.269 rad/s.
That interval gives a starting point for validating the authority-aware
aerodynamic descent controller on the Flight 15 trajectory. For a repeat,
set `diagnosticEndAltitudeM` to `2500` in a
copy of the estimate and run the same diagnostic against that data directory.

## Integrated GPU

Native X11, AMD Radeon Graphics (RADV RENOIR), Vulkan Forward+, Integrated GPU preset
(0.75 render scale). Xvfb selected llvmpipe and was excluded from GPU evidence.
A moving ascent run collected 292 samples between T+10.28 and T+35.14. GPU times were
55.06 ms at p50 and 133.87 ms at p95. CPU render submission was 5.54 ms at p50.
The moving run reached its wall-clock limit before the final screenshot.

A separate run paused at T+10.04 with a 1920 × 1136 viewport. Each row reports the
median of 72 samples following 24 warm-up frames.

| Isolation | GPU time |
| --- | ---: |
| Baseline | 53.27 ms |
| Ground hidden | 53.04 ms |
| Directional shadows disabled | 51.31 ms |
| Sky disabled | 44.56 ms |
| Vehicle and its effects hidden | 47.97 ms |
| Launch effects hidden | 48.85 ms |
| 3D render scale set to 0.60 | 32.98 ms |
| Baseline repeated | 47.44 ms |

Fog was already disabled in the preset. A separate repeat with fewer conditions
measured 50.45 ms at baseline, 44.07 ms with the sky disabled, and 49.02 ms at the
repeated baseline. Ground, shadow, vehicle, and launch-effect differences were within
the baseline drift of the extended run. Render scale produced the largest observed
improvement, with GPU time still around 33 ms in this paused scene. The experiment
left the shipped preset at 0.75. A separate framebuffer comparison at T+10.08
showed a comparable HUD and softer vehicle panel seams and thin tower edges at
0.60. A moving comparison is needed to assess temporal stability before selecting
a lower scale as a default. The hardware run covered the integrated GPU.

```sh
EXOSPHERE_GRAPHICS_PRESET=integrated "$GODOT_BIN" --path . --display-driver x11   --rendering-method forward_plus --rendering-driver vulkan --resolution 1920x1080   --script tools/perf/flight15_gpu_probe.gd
```

Set `EXOSPHERE_GPU_CAPTURE_DIR` to an existing directory to save an image for each arm.
