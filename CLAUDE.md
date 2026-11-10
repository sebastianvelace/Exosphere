# Exosphere Agent Notes

This file is the short operational guide for agents working in this repo. `ROADMAP.md` is the live product plan. `PLAN_REALISM.md` is the physics/telemetry audit log. `PLAN_VISUAL_REALISM.md` is the next visual-fidelity track. Do not look for `PLAN_MEJORAS.md`; it has been retired. The maintained physics source of truth is `docs/physics/PHYSICS_MODEL.md`; the coupled migration and parity gates are tracked in `docs/physics/coupled_6dof_migration.md`.

## Required Checks

Run these after C# changes:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj -c Release --nologo
```

Or run the local aggregate:

```bash
bash tools/ci_check.sh
```

Inner loop (a few minutes): contracts, builds and every test except the multi-minute
classes in `tools/slow_tests.txt` (full Flight 14 pad-to-entry chains), optionally narrowed:

```bash
bash tools/ci_quick.sh
bash tools/ci_quick.sh "FullyQualifiedName~Apollo11"
```

Tests run in Release: the physics is bit-identical to Debug (same Flight 14 fingerprint) and the
full suite takes ~7 min instead of ~29 min. Use `ci_quick.sh` while iterating and for each local commit; run the full `ci_check.sh`
once before pushing a batch of commits (GitHub Actions also runs the full suite). The full
check writes `.ci_results/ci.trx`; `python3 tools/test_durations.py .ci_results/ci.trx
--update-slow-list 60` lists the slowest tests and refreshes the slow list. Flight 14
segment work has its own cached runner, `bash tools/flight14_segments.sh`.

Run Godot smoke checks when scene/game-layer behavior changes:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3
```

For fast VAB iteration (cached builds + construction tests + VAB scene smoke):

```bash
bash tools/vab_quick_check.sh
```

Use `tools/ci_quick.sh` after edits and the full `tools/ci_check.sh` once before pushing.

For atmosphere work (optical/physical tests + flight-scene shader smoke):

```bash
bash tools/atmosphere_quick_check.sh
```

For VAB:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3 \
  res://scenes/construction/Construction.tscn
```

Expected standard: 0 warnings, 0 errors, tests passing.

## Visual Capture Rule

Use Godot `--headless` only for smoke/load checks. For screenshot validation, run
Godot under a real framebuffer:

```bash
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" --path . --rendering-driver opengl3
```

Screenshot harnesses must be temporary and untracked: `scripts/_*Shot.cs`,
`scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn`, and temporary autoload edits
in `project.godot`.

## Architecture Boundary

There are two assemblies:

- `ExosphereSimulation/`: pure C#, no Godot dependency.
- `scripts/` via `Exosphere.csproj`: Godot game layer.

Never import `Godot` in `ExosphereSimulation/`.

`SimulationBridge` is the boundary object:
- owns `Universe`
- exposes `ActiveVessel`
- manages time warp
- places constructed vessels on the pad
- lets Godot controllers read/write simulation state

## Simulation Rules

- Use SI units in sim code: meters, m/s, kg, seconds, radians internally.
- Use double precision sim types: `Vector3d`, `Quaterniond`.
- Do not use Godot float vectors in simulation code.
- Keep public simulation contracts in English.
- Add or update xUnit tests when touching shared physics, orbital logic, construction logic, or data contracts.

Important implemented systems:
- RK4 and Kepler/on-rails propagation
- SOI selection
- radial/suborbital impact guards
- pressure-corrected thrust/Isp/mass flow
- orientation-dependent aero and heating
- coupled rigid-body 6-DoF RK4 (default) with COM/inertia, candidate-state forces and contact
- heat-shield flag from part JSON
- hard impact destruction
- VAB catalog/assembly/export in `ExosphereSimulation/Construction`
- environment providers in `ExosphereSimulation/Environment` (`Universe.Environment.Ephemeris`,
  `CelestialBody.Orientation`/`Gravity`; defaults are the original Kepler/fixed-axis/J2 models) and
  one air-velocity point (`CelestialBody.GetAirVelocity`, `Vessel.GetAirRelativeVelocity`: aero,
  heating and air data read the air; contact, landing and navigation read the surface)
- environment datasets enter through `tools/envdata` and `data/environment/manifest.json`
  (offline SHA-256 + provenance contract in `contract_checks.sh`); the dated environment is
  opt-in per `Universe.Use*` switch (IERS orientation, DE440, spherical-harmonic gravity,
  measured wind + HWM14 via `CelestialBody.Wind`, seeded von Kármán turbulence, NRLMSIS via
  `CelestialBody.AtmosphereField`; aero/engines read the atmosphere through
  `AtmosphericDensityAt/TemperatureAt/PressureAt`). Historical missions declare `datedEnvironment`
  and fly it (`Campaign/MissionEnvironment`, applied by `SimulationBridge`); absolute epochs are
  then negative (1969 ≈ −9.6e8 s), so never treat t < 0 as invalid and compare epochs with
  `SimulationClock.SameEpoch`. Saves record `EphemerisId`. Plan: `docs/physics/environment_and_recalibration_plan.md`
- Hohmann planetary transfers, Earth–Moon Lambert/B-plane planning, encounter prediction, and patched-conic SOI transitions in `ExosphereSimulation/Navigation` and `Universe`

## Game Layer Rules

- Godot code lives under `scripts/` and namespace `Exosphere.Game`.
- Project main scene: `scenes/ui/MainMenu.tscn`. Playable flight: `scenes/flight/Flight.tscn`.
- VAB scene: `scenes/construction/Construction.tscn`.
- Flight opens VAB with `V`; VAB launches the current craft through `CraftLaunchRequest`.
- VAB direct picking lives in `scripts/VabPickingLayer.cs`; it creates collision bodies for parts and compatible attachment nodes and is driven by `ConstructionController`.
- The active vessel is rendered at origin through `FloatingOrigin`.
- Render scale: `1 Godot unit = 2.8 m`.
- Planets are scaled-space backdrops, not placed at true render distances.

`Exosphere.csproj` must exclude:

```xml
<Compile Remove="ExosphereSimulation/**/*.cs" />
<Compile Remove="ExosphereSimulation.Tests/**/*.cs" />
<Compile Remove=".godot/**/*.cs" />
```

## Data Rules

Data lives in JSON:
- `data/bodies/*.json` — an atmosphere must declare `molar_mass`, `heat_capacity_ratio`
  (non-air gases), `surface_gravity` and `geopotential_radius`; the defaults are Earth's, so omitting them silently holds an alien
  column up with Earth gravity and Earth air. Layer `alt_min`/`alt_max` are GEOPOTENTIAL
  metres (USSA-76 convention), not geometric.
- `data/parts/*.json`
- `data/launch_sites/*.json` — live: `SimulationBridge.LaunchSiteId` picks the pad, and the
  site's latitude sets the rotational boost the vehicle inherits (ω·R·cos φ). Never spawn a
  vessel on a hardcoded axis; derive the pad frame from `CelestialBody.GetSurfacePosition`.

Do not hardcode physical constants in game code when they belong in JSON. Copy the schema of nearby files when adding data.

Attachment-node rules used by VAB V1:
- stack nodes attach only to stack nodes of the same size
- radial nodes attach only to radial nodes
- `engine_bell` nodes are not attachable

The default Starship stack uses `decoupler_heavy`, not `decoupler_medium`, because the Starship stack uses size-3 nodes.

## Known Limits

- Per-engine lifecycle/gimbal/thermal/feed state is modeled per instance; fuel exhaustion cuts delivered thrust without creating a gameplay failure state. Per-mount torque (`PartGraph.GetTotalTorque`), differential TVC (R5b: `SolveDifferentialGimbal`), and per-mount thrust vector sum (R5d: `GetThrustVector`) are landed; `Vessel.Tick` applies real per-mount torque with and without pilot input.
- VAB 2.0 has filtered catalog, double-click auto-attach, Starter/Starship templates, undo/redo, launch validation, 3D picking, craft persistence, MainMenu entry, and VAB-to-launch flow. It still lacks drag/rotate gizmos. Catalog drag-ghost, symmetry, and part-rotation UX are not in the construction UI.
- Reentry has physics basis and tests plus windward plasma, tile charring, survivable belly-flop EDL, thermal break-up VFX, per-piece structural breakup, and control-loss authority. Richer shock/plasma rendering vs IFT references is still pending.
- Mission UX: usable save/load (C1), orbit→deorbit→ENTRY without teleport (C2), and EDL phase-track/cues (C3) are landed. LEO warp decay (B3), hot-stage thrust overlap (B2), structural breakup (B1) and `ControlAuthority` are landed. Visual oleada A is landed. Mechazilla Ship catch + booster return (R12 boostback → entry burn → catch via `BoosterReturnController`, HUD status line) are landed. R11 systems phase loads + ground command delay (`GroundCommandRelay`) are landed; crewed pad/LEO stick is onboard (`PilotCommandRouting`) so LOS does not dead-stick the player, `[G]` yields to WASD, and Minimal HUD is a broadcast band with catalog-derived stage/mount engine boards for all launchers, navball, observed-event timeline and telemetry; Clean retains only the navball and critical alerts. Apollo 11 flies TD&E on SM RCS (real capture, no teleport), docked LOI, crew transfer, undocking, DOI and powered descent (`LunarPoweredDescent`) to a soft lunar touchdown; LM ascent, rendezvous and the return are pending. Player guidance: `FlightPhaseObserver` recognises hand-flown milestones (MissionManager queues one phase per frame, forward only); `[K]` toggles the historical director mid-flight (`ToggleMissionControl`, flags realigned from the phase); mission JSON `procedure` steps drive the in-flight guide (`MissionGuide`); `MoonIndicator`/`LunarStatusReadout` and `TargetIndicator`/`DockingTargeting` (arrow keys = RCS translation, real capture) cover lunar and docking navigation. Pilot tools: SAS hold modes (`SasHold` + `SasModeController`, [Y]), node normal axis (Ctrl+wheel), warp to node ([N], `WarpSchedule`), lunar [B] presets (`CapturePlanner`, TEI via `TransearthInjectionPlanner`), hyperbolic `TimeToNode`, landing aids (`LandingReadout`/`LandingAidIndicator`). Practice: milestone checkpoints (`MissionCheckpoints`, [F6] — F8 stays retired with the engine-failure controls — debrief button) reload the flight scene from save — every scene singleton must clear `Instance` in `_ExitTree` for flight→flight reloads; director resume realigns its flags from the saved phase; `Vessel.InfinitePropellant` assist restores tank loads after each step. Night pads: `PadNightLighting` (child of `LaunchPadController`, built after the complex) places screen-size work lights/beacons by structure name, floodlight cones and CPU-particle venting (GPU particles did not draw on Compatibility here); it raises the glow floor and the exposure's practical-light floor while lit. Settings → pad light (`UserInterfaceSettings.PadLighting`). `ControlHintBar` shows the current phase's keys above the bottom band (hidden in Clean). Training drills live in `data/training/*.json` (`Training/TrainingDefinition`, `TrainingSeeder` sets the start once, `TrainingGoals`), launched with `LaunchIntent.Mode = "training"`; the docking drill's Agena holds prograde in its orbit frame, and docking readouts are relative to the target's orbit frame (co-orbiting craft read zero drift). `tools/visual_playtest.sh` has `--hotstage`, `--reentry-compare`, `--apollo8-mission`, `--gemini-mission` and `--apollo11-mission` (observe-only pad→lunar orbit; `--until-phase ORBIT|TLI|…` for segment checks, powered ascent fast-forwarded unless `--no-fast-forward`, periodic `TRACE_MISSION` evidence). Interrupting the harness (SIGINT/SIGTERM) now stops Godot and restores `project.godot` immediately. R5/R5b/R5c/R5d are landed.
- Patched-conic SOI transitions are implemented for on-rails vessels (warp-resolution-independent); inside on-rails propagation use `BodyStateAt(body, t)` for body state at the epoch/crossing time, not the end-of-tick global position.
- Interplanetary planning has tested Hohmann planetary transfers, an ephemeris-targeted
  geocentric Moon route, patched-conic SOI transitions, encounter/perilunium prediction
  and TLI/LOI readouts. Apollo 8/11 now fly finite TLI, mid-course correction, LOI and
  circularization burns (`Flight/TranslunarInjection` steers the TLI in closed loop to the
  planned arrival, so the midcourse is a ~3 m/s trim; `LambertGuidedBurn` pulls its aim into
  the vessel's plane within 10° of a half revolution; `PeriapsisCaptureBurn`); [L] holds on the pad for a lunar launch window
  (`Navigation/LunarLaunchWindow`, azimuth 72°–108°) and the S-II/S-IVB fly closed-loop,
  plane-steered insertion into the lunar plane (only a resumed ascent falls back to a
  scripted plane correction). TEI is a finite SPS burn (`Navigation/TransearthInjectionPlanner`),
  three `TransearthCorrection` burns hit the 38 km corridor perigee, and the CM flies a lifting
  entry on trimmed-capsule aerodynamics with `CapsuleEntryGuidance` bank control. Translunar
  corrections target the perilune (`TranslunarCorrection`, in-SOI trim), LOI 108 × 312 km.
- Gemini 8 flies rendezvous and docking on translation RCS (`Vessel.TranslationCommand`,
  `RendezvousPlanner`, `ProximityApproach`, real `TryDock`), the thruster 8 failure as a
  `DisturbanceTorqueBody` with re-entry RCS recovery, and a solid-motor retrofire. Its launch
  still starts from an Agena placed overhead (scenario geometry).
  Dated lunar ephemerides and timeline maneuver nodes are still needed.
- CI runs `tools/contract_checks.sh` (the same static contract list as local `tools/ci_check.sh`), builds/tests the sim, builds the Godot C# layer, downloads Godot 4.6.3 mono in GitHub Actions, and runs strict headless smoke checks. Local `tools/ci_check.sh` runs Godot smoke only when `GODOT_BIN` or the default local Godot path exists.
- Godot `--headless` in this environment uses a dummy renderer, so viewport PNG capture needs a real framebuffer.
- Current product priority after documentation cleanup: visual fidelity against real Starship/Super Heavy references. Prefer scoped improvements to `VesselRenderer`, `ReentryPlasmaController`, `PlumeSystem`, camera/lighting, and visual capture before broad new gameplay systems.
- Flight 14 exploration now continues beyond its retained 100 m witness to estimated low-speed water entry, then freezes after ten seconds of water-only coupled mass-centre translation/rotation. Inclined sealed-cylinder buoyancy and projected wet-section drag are opt-in and region-gated; the global ascent/EDL integration mode is unchanged. An independent Super Heavy engineering return now reaches Gulf water contact with selectable observation and an abstracted FTS boundary; see `docs/audits/flight14_booster_return_2026-10-03.md`. Estimated booster apogee/landing guidance now improves height and timing; see `docs/audits/flight14_booster_calibration_2026-10-03.md`. Exact targeting, flooding, measured capsize timing and full booster reference-timing/trajectory calibration remain pending. See `docs/audits/flight14_water_motion_2026-10-03.md`; the earlier water-entry and inclined-water audits describe prior increments.
- Off-rails flight runs on the coupled rigid-body 6-DoF RK4 by default, from every scheduler
  (`Universe.IntegrateVesselFullPhysics`); the split tick remains only as a comparison baseline
  (`Coupled6DofIntegrationEnabled = false`). The part frame's origin is the stack's lowest point
  (the skirt datum the renderer, gear, water and pad use); `Vessel.Position` is that datum,
  `Vessel.CenterOfMass`/`CenterOfMassVelocity` the physical state. Navigation (surface velocity,
  orbital burns, SAS holds) and on-rails conics (`ComputeCenterOfMassOrbit`) use the centre of
  mass; altitude queries use the datum. Slender stacks (L/D ≥ 3) fly distributed per-part
  aerodynamics (`DistributedAerodynamics`, `Universe.DistributedAerodynamicsEnabled`, F1) whose
  crossflow term is the measured η·C_dn of Jorgensen NASA TR R-474 (`CrossflowDrag`, crossflow
  Mach/Reynolds with per-atmosphere Sutherland viscosity); Starship's body flaps are physical
  plates on by default (`Universe.PhysicalBodyFlapsEnabled`, F2), allocated by Gauss–Newton +
  redistributed pseudo-inverse (descent feedback gain-scheduled by flap authority); mission controllers clear
  `Vessel.PilotRateEnvelopeEnabled` (the 20°/s envelope is a human-pilot handling choice);
  parts may declare `header_tanks` (point masses at their station that feed only after the main
  tanks are empty; Starship lands on its LOX/CH4 headers after the main-tank vent);
  blunt capsules keep the blunt/trimmed model. Flight control inverts the predicted static aero
  moment in the gimbal allocation (clipped to the real range), so finless launchers stay
  unstable airframes held by TVC. Parity gates compare integrators with distributed aero off.
  Attitude control is actuator-only (F3, closed; see `rigid_body_fidelity_plan.md`
  "F3 findings"): SAS and the 20°/s pilot envelope are commands, nothing
  writes ω, and there is no generic RCS floor. RCS comes from per-part `rcs_thrusters`
  (position, direction, thrust, Isp; fed from the owning part's `fuel_capacity_mono` store or
  the shared feed; owning demands are reserved before shared draw), allocated by `Physics/RcsAllocation` (couples first, inertia-weighted; about the docked stack's
  centre and inertia when docked, `Vessel.DockedStackCenterOfMassLocal`);
  `rcs_arms_with_stage` and `rcs_arms_when_isolated` arm systems at separation; flaps take
  their share before the jets, and under thrust a slewing gimbal (≥ 1°/s) owns its axes.
  F4 is reverified in production: tidal moments act in coupled off-rails/water stages and Kepler attitude
  (`Universe.GravityGradientEnabled`); variable-mass transport acts on the coupled off-rails
  path (`Universe.VariableMassTransportEnabled`, default on). Mass properties come from one
  decomposition (`Parts/PartMassGeometry`: `dry_mass_center_y_m`, `dry_gyration_m`,
  `propellant_tanks` settled columns on equivalent flat bottoms, header points); each step's
  funded flows and declared outlets (engine thrust point less the model's
  `NozzleExitAxialOffsetM`, RCS nozzles, `vent_outlet`, a stuck thruster) feed
  `Physics/OpenSystemTransport`; flow cuts conserve P and H. An undeclared outlet, the
  practice assist, the legacy split path and water motion fall back to frozen mass
  (`Vessel.LastVariableMassStatus`). Draining liquid vehicles spin up in roll: controllers
  that must hold roll pass `rollDampingGain`. Historical gate:
  `SaturnVJetDampingHistoricalTests` (AS-506). Optional immutable receipts
  (`CaptureRcsFunding`, `CapturePropellantFunding`, `CaptureResourceFunding`) remain
  observational. See `docs/physics/variable_mass_torques.md`. F5 funded slosh is closed;
  its production coupling and validity limits are in `docs/physics/propellant_slosh.md`.
  Historical recalibration F6 is open (`docs/physics/historical_recalibration.md`, plan "F6 progress"):
  the SPS capture window uses the docked stack (`TranslationalMassKg`) and the SM carries the
  measured SP-238 SPS load, and CM/SM dry centres and gyrations derive from SP-238 table B-I
  (`ApolloCsmMassPropertiesHistoricalTests`), and Apollo 11 flies its own SP-238 lunar targets
  (non-circular circularization via `Flight/ApsidesTargetBurn`); SPS tank geometry, cryogenic use, the SPS exit
  and the Atlas sustainer/PU reconstruction remain.
  Material aero/contact velocity excludes the moving-centroid `R*cdot` term. Known feed
  stops transfer internal momentum before clearing it; off-axis openings compensate datum
  velocity for the angular reaction. Per-store relative-flow shares retain their angular
  reference at the datum, follow their source parts through splits, and persist in optional
  Save V2 fields; unknown flow remains unknown. Flight 14 orbital ignition commits inside 2 degrees
  and 0.03 rad/s; a running burn retains 5 degrees / 0.15 rad/s and aborts violations without
  automatic relight. Water coverage can be a union of individually NOAA-screened rectangles
  (`AdditionalWaterRegions`); each needs complete native-grid evidence and island exclusions.

## Workflow

- Fidelity data must cite its source: every value of the fidelity track (`nose_length_m`,
  `body_flaps`, header tanks, model constants, calibrated profile knobs) has a record in
  `data/provenance/*.json` (status published/derived/estimated/calibrated, source, uncertainty,
  and `value` when it is one number). `PartCatalog` rejects a fidelity field without one,
  `FidelityProvenanceTests` cross-checks code constants against their records, and
  `docs/physics/estimates_register.md` (regenerate with `python3 tools/provenance_register.py`;
  `contract_checks.sh` fails if stale) lists everything that is not published data.
- Each fidelity phase closes on an analytic and a historical gate listed in
  `docs/physics/fidelity_gates.md` and tagged `[Trait("FidelityGate", "Fn")]` +
  `[Trait("GateKind", "analytic|historical")]`; `FidelityGateRegistryTests` keeps both in sync.
  Run one phase with `bash tools/fidelity_gate.sh F1`.
- Flight 14 downstream segments (descent, flip, terminal burn) resume from one shared
  pad-to-entry checkpoint (`Flight14LaunchDiagnostic.CaptureEntryCheckpoint`/`ResumeAtEntry`,
  test fixture `Flight14EntryCheckpointFixture`), cached on disk under a SHA-256 of the
  simulation assembly and `data/`. `Flight14EntryCheckpointTests` requires a resumed run to
  reproduce the continuous one bit for bit; `Flight14ReturnTests` keeps the full pad-to-entry
  chain and the exploration preview keeps its gameplay-universe chain. Iterate segments with
  `bash tools/flight14_segments.sh` (~1 min with a warm cache).

- Keep `ROADMAP.md` current when closing or adding roadmap items.
- Keep `README.md` and this file aligned with the repo.
- One coherent commit per task.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses such as `scripts/_*Shot.cs`,
  `scripts/*VerifyShot.cs`, or `scenes/*VerifyShot.tscn`.
- Do not break `[G]` ascent or EDL while working on unrelated features.
