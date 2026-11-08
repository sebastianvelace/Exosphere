# Developing Exosphere

This is the technical guide for changing Exosphere. The player-facing introduction, the play path, and the flight controls live in the [README](../README.md).

Current engine and runtime:

| Piece | Role |
|-------|------|
| Godot 4.6.3 .NET | Game runtime and editor |
| .NET 8 | Language and SDK |
| `Exosphere.csproj` | Godot game layer under `scripts/` |
| `ExosphereSimulation/` | Pure C# simulation library |
| `ExosphereSimulation.Tests/` | xUnit regression tests |

## Quick path

1. Install Godot 4.6.3 .NET and the .NET 8 SDK.
2. Build both projects and run the test suite.
3. Open this folder in that Godot editor and run `scenes/ui/MainMenu.tscn`.

## Layout

| Path | What it owns |
|------|----------------|
| `ExosphereSimulation/` | Physics, vessels, construction model. No Godot reference. |
| `scripts/` | Godot controllers, HUD, rendering. Namespace `Exosphere.Game`. |
| `ExosphereSimulation.Tests/` | Shared physics, orbital, and construction tests. |
| `data/` | Bodies, parts, launch sites, campaigns, and vehicle presets. |
| `scenes/ui/MainMenu.tscn` | Project main scene. |
| `scenes/flight/Flight.tscn` | Playable flight. |
| `scenes/construction/Construction.tscn` | Vehicle assembly building. |
| `docs/physics/PHYSICS_MODEL.md` | Equations, frames, approximations, and activation gates. |
| `ROADMAP.md` | Current product plan. |

`PLAN_REALISM.md` records the physics and telemetry audit. `PLAN_VISUAL_REALISM.md` is the visual-fidelity track. Historical audits under `docs/audits/` are evidence, not the active plan. The documentation index is [docs/README.md](README.md).

## Build and test

Expected result: 0 warnings, 0 errors, all tests passing.

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
```

Local all-in-one check:

```bash
bash tools/ci_check.sh
```

Godot smoke test, using the 4.6.3 .NET binary on `GODOT_BIN`:

```bash
"$GODOT_BIN" --headless --path . --quit-after 3 --rendering-driver opengl3
```

Vehicle assembly smoke test:

```bash
"$GODOT_BIN" --headless --path . --quit-after 3 --rendering-driver opengl3 \
  res://scenes/construction/Construction.tscn
```

Godot `--headless` uses a dummy renderer here, so a viewport PNG needs a real framebuffer such as Xvfb.

## Run in the editor

Open the project folder in Godot 4.6.3 .NET and run the project. Flight is `scenes/flight/Flight.tscn`. Construction is `scenes/construction/Construction.tscn`. Flight opens construction with `V`.

Debug helpers, omitted from the player guide:

| Key | Action |
|-----|--------|
| `O` | Jump to orbit |
| `J` | Jump to the body selected on the map |

## Visual capture

Use Godot `--headless` only for smoke and load checks. For screenshot validation, run Godot under a real framebuffer:

```bash
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT_BIN" --path . --rendering-driver opengl3
```

Screenshot harnesses stay temporary and untracked: `scripts/_*Shot.cs`, `scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn`, and temporary autoload edits in `project.godot`.

## Architecture

The project has two C# assemblies on purpose.

### `ExosphereSimulation/`

Pure C# simulation library. It must not reference Godot.

Rules:

- Use SI units: meters, m/s, kg, seconds, and radians internally.
- Use double precision: `Vector3d`, `Quaterniond`.
- Keep public simulation names in English.
- Add tests for shared physics or construction behavior.

| Folder | Contents |
|--------|----------|
| `Math/` | Double-precision math types |
| `Integrators/` | RK4 and Kepler propagation |
| `Parts/` | Part definitions, runtime parts, graph, joints |
| `Physics/` | Aero, thermal, stress |
| `Systems/` | Life support, power, comms, thermal systems |
| `Construction/` | Vehicle assembly catalog and assembly model |
| `Navigation/` | Hohmann transfers and Earth–Moon Lambert planning |

### `scripts/`

Godot C# game layer. It may reference Godot and `ExosphereSimulation`.

`SimulationBridge` is the boundary:

- owns `Universe`
- exposes `ActiveVessel`
- controls time warp
- spawns and places vessels
- bridges UI and controllers to the simulation

The active vessel renders at the origin through `FloatingOrigin`. Render scale is 1 Godot unit = 2.8 m. Planets are scaled-space backdrops, not placed at true render distances.

### `Exosphere.csproj`

This project must exclude the simulation, the tests, and the Godot cache from its compile glob:

```xml
<Compile Remove="ExosphereSimulation/**/*.cs" />
<Compile Remove="ExosphereSimulation.Tests/**/*.cs" />
<Compile Remove=".godot/**/*.cs" />
```

Without that exclude, Godot compiles the simulation a second time and can pull xUnit tests into the game assembly.

## Physics at a glance

The simulation is a double-precision, SI-unit model with seconds from J2000 as its clock. It combines data-driven body and part definitions, point-mass gravity plus optional `J2`, rotating-body atmospheres, pressure-corrected propulsion, orientation-dependent aero, thermal protection, multi-point contact, and patched-conic Kepler rail propagation.

Off-rails flight runs on a coupled rigid-body 6-DoF RK4 by default (`Universe.IntegrateVesselFullPhysics`). It integrates the centre of mass, attitude, body-frame angular velocity and the full inertia tensor through four candidate-state force stages, with contact, distributed per-part aerodynamics on slender stacks, physical Starship body flaps, actuator-only attitude control from declared RCS thrusters, variable-mass transport with tidal moments, and funded first-mode propellant slosh. The legacy split tick remains only as a comparison baseline. Fidelity phases F0–F5 are closed and F6 (historical recalibration) is open; the phase table and its analytic/historical gates are in [fidelity_gates.md](physics/fidelity_gates.md), the plan in [rigid_body_fidelity_plan.md](physics/rigid_body_fidelity_plan.md). See the [physics model](physics/PHYSICS_MODEL.md) and the [6-DoF migration record](physics/coupled_6dof_migration.md).

## Data

JSON is the source of physical data. Do not hardcode a constant in game code when it belongs in a file. Copy the schema of a nearby file.

| Path | Contract |
|------|----------|
| `data/bodies/*.json` | An atmosphere must declare `molar_mass`, `surface_gravity`, and `geopotential_radius`. The defaults are Earth's, so omitting them holds an alien column up with Earth gravity and Earth air. Layer `alt_min` and `alt_max` are geopotential metres, the USSA-76 convention. |
| `data/parts/*.json` | Part catalog, including the heat-shield flag. |
| `data/launch_sites/*.json` | `SimulationBridge.LaunchSiteId` picks the pad. Latitude sets the rotational boost the vehicle inherits. Derive the pad frame from `CelestialBody.GetSurfacePosition`. |

Attachment nodes in the vehicle assembly:

- Stack nodes attach only to stack nodes of the same size.
- Radial nodes attach only to radial nodes.
- `engine_bell` nodes are not attachable.

The default Starship stack uses `decoupler_heavy`, because that stack uses size-3 nodes: `starship_command`, `starship_tank`, `starship_engines`, `decoupler_heavy`, `super_heavy_booster`.

`SimulationBridge.DataDirectory` defaults to `res://data`. The bridge turns that into an operating-system path with `ProjectSettings.GlobalizePath` and passes it to `Universe.LoadFromDataDirectory`. An exported PCK does not leave that directory readable by normal file IO.

## Current state

### Flight and vehicle

- HUD, navball, map view, transfer planning, cockpit, systems HUD, and launch, crash, and reentry visual effects.
- Quicksave and quickload on `F5` and `F9`. Mechazilla Ship catch plus booster return. A 16-mission historical campaign, including Flight 7 and Flight 12 scenarios.
- Survivable Starship entry profile: belly-flop, low-altitude flip-and-burn, and soft touchdown.
- Starship and Super Heavy procedural mesh with hot-stage ring, grid-fin lattice, windward tiles, flaps, Raptor clusters, and a stainless steel shader.
- Vehicle assembly: filtered catalog, double-click auto-attach, 3D picking, Starter and Starship templates, undo and redo, launch validation, craft save and load, entry from the main menu and from flight with `V`, and launch to the pad. Drag and rotate gizmos are still missing.

### Simulation

- Eight body files in `data/bodies/` and 106 part files in `data/parts/`. Fidelity values carry source records in `data/provenance/*.json`; [estimates_register.md](physics/estimates_register.md) lists every value that is not published data.
- Double-precision types: `Vector3d`, `Quaterniond`, `Universe`, `Vessel`, `CelestialBody`, `OrbitalElements`.
- RK4 integration, Kepler on-rails propagation, sphere-of-influence selection, patched-conic transitions that do not depend on warp resolution, radial and suborbital guards, and hard-impact destruction.
- Pressure-corrected engines, mass flow, specific impulse, staging, and stage delta-v. The assembly building exposes one engine part per stage. The sim expands `engine_count` into per-instance lifecycle, gimbal, feed, fuel depletion, and torque.
- Orientation-dependent drag, reentry heating, progressive part thermal damage, heat-shield orientation, per-piece structural breakup (`Universe.TryStructuralBreakup`), and control-loss authority.
- Time warp levels: `1, 2, 3, 5, 10, 50, 100, 1000, 10000, 100000`.
- Hohmann planetary transfers and an ephemeris-targeted Earth–Moon Lambert and B-plane planner, with maximum-warp sphere-of-influence regressions.
- Patched-conic transitions and encounter prediction for on-rails interplanetary coast. Inside on-rails propagation, body state at the epoch or crossing uses `BodyStateAt(body, t)`, not the end-of-tick global position.
- Automated tests for gravity, RK4, Kepler, radial and suborbital paths, rails impact, engines, heat shield, aero, sphere of influence, the assembly catalog, the 6-DoF parity gates, and the analytic and historical fidelity gates.

## Limitations

- The assembly building still exposes one engine part per stage. Runtime expands `engine_count` into per-instance `EngineInstanceState`. Fuel exhaustion cuts delivered thrust and does not create a persistent engine failure state. Mesh clusters remain procedural visuals of those instances.
- Drag and rotate gizmos, catalog drag-ghost attach, symmetry, and part rotation are not in the construction UI.
- Reentry has windward plasma, tile charring, a survivable belly-flop profile, thermal break-up effects, per-piece structural breakup, and control-loss authority. Powered descent coasts below the target descent profile and rejects sub-m/s lateral noise before lighting minimum-throttle engines. Richer shock and plasma rendering against flight-test references is still pending.
- The orbital-return path seeds a rotating Starbase intercept plane and predicts the moving catch corridor with surface-relative velocity. The deterministic end-to-end run reaches atmospheric entry and reduces the footprint error. A complete physical tower catch is still pending. The catch gates stay strict.
- The hull is modelled at the real 9 m diameter, with procedural steel, weld seams, windward tiles, flaps, raceways, Raptor clusters, and a geographically aligned terrain stack around Starbase. Remaining visual work is fine reference matching, target-GPU tuning, and broader collision and detail coverage. See `docs/audits/LOCAL_TERRAIN_REALISM_2026-09-14.md`.
- Interplanetary planning has tested Hohmann transfers, a geocentric Lambert and B-plane Moon route, patched-conic transitions and encounter prediction. Apollo 8/11 fly finite TLI, mid-course corrections, LOI, circularization and TEI burns. Dated lunar ephemerides and timeline maneuver nodes are still open, and Apollo 11's lunar targets still use the Apollo 8 proxy.
- Historical recalibration (F6) is open: native SM dry mass properties, the SPS nozzle exit, the native Atlas sustainer and propellant-utilization reconstruction, S-IC fins, grid fins and a Newtonian blunt-capsule model remain. See [historical_recalibration.md](physics/historical_recalibration.md).
- CI builds the sim and the Godot layer, downloads Godot 4.6.3 .NET, runs the contract checks, the full test suite and headless smoke checks, and uploads a pad visual-smoke PNG when one is produced.
- `tools/export_game.sh` packages a build from `export_presets.cfg`. Simulation loaders read `data/` through `System.IO`, so the export copies the `data/` tree beside the executable and `GameDataPath` prefers it outside the editor.
- `data/licenses/assets_manifest.json` sets `commercialReleaseBlockedUntilReviewed`. Some textures have an unknown license, and the NASA and SpaceX names used for historical identification are marked `legal_review_required`. A public binary waits on that review. The MIT license covers the source code.

## Working rules

- Update `ROADMAP.md` when a roadmap item changes state.
- Keep the [README](../README.md), this guide, and `CLAUDE.md` aligned with the repo.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses.
- Build and test before committing code.
- Do not break ascent on `G` or entry while working on an unrelated feature.
