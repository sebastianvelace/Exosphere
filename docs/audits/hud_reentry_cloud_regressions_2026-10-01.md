# Reported HUD, entry and cloud regressions — 2026-10-01

## Scope

Address the three reported regressions before resuming historical Flight 14 work.
This audit distinguishes a numerical production-controller replay from rendered
presentation checks. Neither is an end-to-end validation of Flight 14.

## Broadcast HUD

The running game's saved interface profile had `hud_density=2` (legacy Clean).
That mode hid the broadcast band and restored the old engine/apsides cluster.
The process also predates the rebuilt assembly; it must be restarted to load it.

A versioned migration changes legacy Clean to Broadcast once, preserving language,
scale, reduced motion and unknown configuration keys. Full and Minimal remain as
selected. Choosing Clean explicitly after migration persists and displays only the
navball and critical alerts. Main-menu settings now expose Flight Telemetry, and F3
cycles the same modes with localized names. The old exterior engine/telemetry
panels no longer reappear in Clean. This shared policy applies to all vehicles;
it does not change their engine layouts or derive values from a Starship constant.

An isolated profile check passed migration/persistence assertions. Real-framebuffer
captures verified Broadcast, Clean, Full and the Spanish settings row. The running
user process was preserved rather than terminated or its live profile edited.

## Uncommanded entry climb

An unpowered diagnostic entry reproduced a positive radial speed of **157.2 m/s**
around 40 km. Gravity remained approximately 9.69 m/s² and specific orbital energy
continued decreasing: this was a lifting skip, not missing gravity or energy creation.
The baseline peak aerodynamic load was 15.61 g.

Two control defects contributed: the previous lift reference was cleared before
its lateral side was read, and projecting that reference off vertical could retain
an axial airflow component as the flight path rotated. The aerodynamic axis
conversion removes axial components, silently losing the requested bank. The bank
side now uses `up × airflow` with sign continuity. A measured climb-rate envelope
banks excess lift sideways, preserving the normal attitude slew and actuators.
The production force integrator still owns gravity, drag, lift and state evolution.
No velocity or position clamps, demo catch or injected forces are used.

The maintained runner seeds the legacy default Starship in the real Flight scene,
using body-centred inertial speed, geodetic altitude and a belly-first orientation.
It does not load a dated vehicle variant or a recorded user save. It runs ordinary
Universe ticks and EDL control, ending above powered landing. Nearby baseline
conditions were approximately 109 km, 6100 m/s and −9.7°; the original spherical
seed and the maintained geodetic seed differ by about 77 m.

| Production replay | Highest radial speed | Peak aerodynamic load | Final sampled altitude |
| --- | ---: | ---: | ---: |
| Steep entry, 109 km / 6100 m/s / −9.7°, no propellant reserve | −87.2 m/s | 12.85 g | 10.16 km |
| Shallow entry, 120 km / 7600 m/s / −1.6°, 6% reserve | −109.1 m/s | 5.22 g | 10.22 km |

Both remained descending in the sampled trace and lost orbital energy. The final
steep replay also sampled the actual production gravity-force model (rather than
only `mu/r²`); it remained nonzero throughout, with throttle exactly zero. The steep
entry remains unsafe despite removal of the rebound. These feedback constants are
reduced-order guidance choices, not measured Flight 14 parameters. Physical skip
entries can exist; removing gravity or forbidding every possible climb would be wrong.
The HUD's 0 g is proper acceleration in near-freefall, not the magnitude of gravity.

Background: [NASA on lifting skip entry](https://www.nasa.gov/missions/orion-spacecraft-to-test-new-entry-technique-on-artemis-i-mission/),
[NASA entry guidance using climb-rate feedback](https://ntrs.nasa.gov/citations/19620002529).

Reproduce the steep regression (mono Godot required):

```sh
python3 tools/audit_entry_rebound.py --require-descent --output /tmp/entry-audit-steep.jsonl
python3 tools/audit_entry_rebound.py --altitude 120000 --speed 7600 --angle -1.6 --reserve .06 --require-descent --output /tmp/entry-audit-shallow.jsonl
```

The runner generates a temporary diagnostic script from a template, never modifies
autoloads, and removes its script/UID on exit. Build the game again after running
it if distributing the assembly; the fixture source is not part of the product.

## Cloud morphology and foreground transmission

Repeated, scaled copies of the geographic weather texture were removed from the
fine erosion field. Domain-warped spherical noise supplies detail, with the fine
octave removed by the existing low-altitude prefilter. Coastal clouds now have
regional occupied/clear areas, a smoothly warped cell lattice, varied orientation,
size, aspect and crown lobes. View rays, shadows and foreground transmission use
the same geometry and conservative empty-space bounds. Meteorological shell bounds
and ray-step budgets remain unchanged.

Six real-framebuffer views cover below, inside with transmission off/on, above,
crowns and a downward view at 110 km. The dense-cloud A/B fixture reduced resolved
hull-feature contrast by **37.7%**. Residual silhouettes can remain; this is not a
claim that every cloud makes the whole ship invisible. Manual review found larger
clear regions and irregular groups, but further reference-based lighting and crown
calibration remains useful. Real cumulus fields can contain organized streets;
[NASA's Timor Sea example](https://earthobservatory.nasa.gov/images/88389/cloud-streets-over-the-timor-sea?src=ve)
is background, not a measured weather match for this mission.

The old occlusion metric sampled a seven-pixel strip around a roughly one-pixel
hull at 640 px, mostly measuring unrelated background. The revised metric selects
resolved features using only the clear image, measures the same pixels in both,
and rejects changed A/B backgrounds. The original attenuation threshold remains.
Four synthetic tests cover attenuation, unchanged hull, changed background and
unresolved hull rejection. This measurement correction is separate from visual
acceptance. The preserved run was re-evaluated with `--verify-only`, not rerendered
until it happened to pass.

```sh
bash tools/visual_playtest.sh --cloud-traverse --resolution 640x360 --run-id cloud-irregular-v3 --skip-build
python3 -m unittest discover -s tools/tests -p test_cloud_occlusion.py
```

Captures use llvmpipe/Compatibility rendering. Their CPU-rendered frame times are
not physical-GPU performance evidence, and differing resolutions/concurrent loads
prevent a valid before/after benchmark. Shader budget contracts pass; real-GPU
performance and artistic realism still require assessment in normal gameplay.

## Evidence

Local review artifacts are under `exports/reported-regressions-review/` (ignored
binary captures), including HUD/settings, six final cloud views, 960 px cloud views,
entry traces and an entry comparison plot. Numerical replays are diagnostic
initial conditions; no full mission or historical landing has been claimed here.

## Verification result

`bash tools/ci_check.sh` passed: 926 xUnit tests, both builds with zero warnings/errors,
source contracts and Flight/Main Menu/Construction headless startup checks. Four
cloud-occlusion measurement tests passed separately. UI state checks and cloud
traversal used real framebuffer captures; they are presentation checks, not proof
of historical mission accuracy.
