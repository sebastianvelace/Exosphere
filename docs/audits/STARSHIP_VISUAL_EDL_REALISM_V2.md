# Starship visual, propulsion and EDL realism — V2

Date: 2026-07-13

## Outcome

This pass fixes the full-stack control discontinuity, the reversed Starship ogive,
ascent-only red hull, invisible Raptor exhaust, cockpit clipping/overexposure, excessive
photographic stars, startup texture stalls, and the EDL state that previously ended in a
ballistic loss around 200 km.

The deterministic EDL scenario now completes:

`ENTRY → PEAK_HEATING → AERO_DESCENT → RETRO_BURN → FINAL_DESCENT → LANDED`

Measured final verification (`/tmp/exo_edl_v3.log`):

- entry seed: 70 km, 1,805 m/s;
- visible heating capture: 33.85 km, 1,667 m/s, 85.3 kW/m²;
- three-engine landing-burn ignition at 2.75 km;
- finite physical flip: 9.57 s, final alignment 0.99922;
- engine reduction from three to two during braking;
- touchdown: six contacts, 0.0 m/s vertical/horizontal, `LANDED`;
- no launch clamp reused after landing; the contact solver enters a wakeable rigid-body
  sleep only after a persistent low-kinetic, adequately-supported contact state.

## Root causes and corrections

### Guidance ownership

The `[L]` auto sequence performed countdown, ignition and clamp release but did not engage
the ascent controller. A nominal-looking launch could therefore remain a ballistic full
stack. Auto-launch now engages ascent guidance, ascent yields control throughout descent,
and EDL is the last writer of throttle and attitude.

EDL previously disarmed during a brief upward skip or atmospheric exit. The mission phase
then remained in entry while no controller owned the vehicle. EDL now remains armed across
skip-entry geometry and begins the landing flip with three centre Raptors already lit.

### Landing contact

The vehicle could reach six-foot contact at essentially zero speed yet never satisfy a
narrow `0.75–1.25 g` penalty-contact support window. Spring preload then accumulated until
the visual test timed out. Ultimate leg load remains the hard damage gate; persistent
low-speed, upright, multi-foot contact now establishes a standard rigid-body sleep state.
Throttle immediately wakes it, so this is not a hidden ground clamp.

### Starship geometry and heating

- The old nose radius ran from zero at the barrel to full radius at the tip. A tested
  tangent-ogive profile now runs monotonically from the 9 m barrel to a zero-radius tip.
- Nose TPS follows that ogive rather than a cylindrical shell.
- Steel and TPS emission is gated by descending radial velocity and convective heat flux.
  A warm ascent can no longer paint the complete vehicle red.
- Plasma, TPS emission, exposure, phase lighting and plasma audio share the same calibrated
  25–300 kW/m² visual range.

### Raptors and exhaust

- Starship has three individual sea-level and three individual vacuum nozzles/plumes at
  their actual cluster locations.
- Sea-level and vacuum bells have different dimensions, stiffeners, feed-line cues and
  exit geometry.
- Exhaust expands with ambient pressure, retains a visible optically-thin sheath in vacuum,
  and uses a pale blue-white methalox core with a confined warm ignition root.
- Landing guidance commands the visible 3 → 2 → 1-capable centre-engine sequence instead
  of leaving engines at zero through the flip.

The sequence is patterned after SpaceX's published flight descriptions: Flight 4 used
three centre Raptors for flip/landing burn, and Flight 12 reports landing-burn start,
flip, and reductions from three to two to one engines.

References:

- https://www.spacex.com/launches/starship-flight-4
- https://www.spacex.com/launches/starship-flight-12

### Launch deluge

Changing `GpuParticles3D.Amount` during spool rebuilt GPU buffers and repeatedly discarded
the young cloud. Particle counts are now fixed and driven with `AmountRatio`. Ignition also
creates one deterministic, instanced bank of irregular vapor billows, followed by turbulent
steam, boil-up, haze and radial dust. It is anchored to the rotating surface point rather
than following the vehicle.

### Camera, cockpit and exposure

- Cockpit up is local `-Z`, matching the authored interior; the former `+Z` made the cabin
  appear inverted.
- IVA has a 4 cm near plane, 60° FOV, restrained vibration, dark non-emissive structure,
  practical lights, panel/screen bezels, overhead controls and live double-sided displays.
- The separated-Ship chase camera reframes from 121 m stack to 50 m vehicle and initially
  chooses its illuminated quarter. Earthshine prevents physically implausible absolute-black
  steel in low orbit.
- The 8K photographic star map is attenuated during photopic adaptation. Sunlit Earth,
  atmosphere, plasma and illuminated vehicle surfaces suppress stars; dark adaptation can
  recover them.

### Startup and Earth transition

- 8K JPEGs now use Godot's imported resource cache rather than being synchronously decoded
  and mipmapped independently by several controllers.
- The local curved Earth patch dropped from 153,600 to 55,296 generated vertices while
  preserving its 900 km footprint and true-sphere curvature.
- Expensive procedural-sky uniform/cubemap updates are capped at 12 Hz and remain incremental.

The eastbound trajectory from Boca Chica naturally crosses the Gulf of Mexico. That is not
a coordinate bug. A return to Starbase needs explicit latitude/longitude targeting, crossrange
management and a landing-zone mission; moving the ocean or keeping local Starbase geometry
under the spacecraft would be less realistic.

## Verification

Commands:

```bash
dotnet build Exosphere.csproj --no-restore
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore
bash tools/visual_playtest.sh --edl
bash tools/visual_playtest.sh --ship
bash tools/visual_playtest.sh --cockpit
```

Deterministic visual modes emit PNGs and a telemetry log. `--edl` fails on destruction,
timeout, missing flip or missing touchdown. `--ship` stages and powers all six engines in
vacuum; `--cockpit` stages and captures IVA in orbit.

## Deliberate limitations

- The exterior is procedural geometry, not a survey-grade SpaceX CAD model.
- The landing gear is the simulator's explicit reusable-EDL variant, not hardware flown on
  the cited Starship flights.
- The renderer approximates reacting-flow radiation; it is not CFD or spectral combustion.
- Starbase return targeting and tower catch are separate navigation/control milestones.
