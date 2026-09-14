# Exosphere

Exosphere is a Godot/C# space mission simulator focused on a Starship/Super Heavy style vehicle, real-scale orbital mechanics, atmospheric flight, reentry, time warp, cockpit/HUD gameplay, and a data-driven VAB.

Current engine/runtime:
- Godot 4.6.3 mono
- .NET 8
- C# game layer in `Exosphere.csproj`
- Pure C# simulation library in `ExosphereSimulation/`
- xUnit regression tests in `ExosphereSimulation.Tests/`

## Current State

Implemented and working:
- Project main scene: `scenes/ui/MainMenu.tscn`. Playable flight: `scenes/flight/Flight.tscn`. VAB: `scenes/construction/Construction.tscn`
- Data-driven solar system with 8 body JSON files in `data/bodies/`
- Data-driven parts catalog with 104 JSON files in `data/parts/`
- Double-precision simulation types: `Vector3d`, `Quaterniond`, `Universe`, `Vessel`, `CelestialBody`, `OrbitalElements`
- RK4 integration, Kepler/on-rails propagation, SOI selection, patched-conic SOI transitions (warp-resolution-independent), radial/suborbital guards, hard-impact destruction
- Pressure-corrected engines, mass flow, Isp, staging and stage delta-v. VAB still exposes one engine part per stage; the sim expands `engine_count` into per-instance lifecycle, gimbal, feed, failure and torque
- Orientation-dependent drag, reentry heating, progressive part thermal damage, heat-shield orientation handling, per-piece structural breakup (`Universe.TryStructuralBreakup`) and control-loss authority
- Time warp levels: `1,2,3,5,10,50,100,1000,10000,100000`
- HUD, navball, map view, transfer planning helpers, cockpit, systems HUD, launch/crash/reentry visual effects
- Quicksave/load `F5`/`F9`, Mechazilla Ship catch plus booster return, and a 16-mission historical campaign (Flight 7/12 scenarios included)
- Pure Hohmann planetary transfers plus an ephemeris-targeted Earth–Moon Lambert/B-plane planner with maximum-warp SOI regressions
- Patched-conic SOI transitions and encounter prediction for on-rails interplanetary coast
- Starship/Super Heavy procedural mesh with hot-stage ring, grid-fin lattice, windward tiles, flaps, Raptor clusters and stainless steel shader
- Survivable Starship EDL profile: belly-flop reentry, low-altitude flip-and-burn and soft touchdown
- VAB 2.0: filtered catalog, double-click auto-attach, 3D picking, Starter/Starship templates, undo/redo, launch validation, craft save/load, MainMenu and `V` from flight, `Launch` to pad. Still missing drag/rotate gizmos
- Automated tests for gravity, RK4, Kepler, radial/suborbital, rails impact, engines, heat-shield, aero, SOI, and VAB catalog/assembly/export

See `ROADMAP.md` for the current plan. `PLAN_REALISM.md` records the physics/telemetry audit, and `PLAN_VISUAL_REALISM.md` is the next visual-fidelity track.

## Build And Test

Run these after C# changes:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
```

Expected result: 0 warnings, 0 errors, all tests passing.

Local all-in-one check:

```bash
bash tools/ci_check.sh
```

Godot smoke test:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3
```

VAB smoke test:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3 \
  res://scenes/construction/Construction.tscn
```

Note: Godot `--headless` in this environment uses a dummy renderer, so viewport PNG capture is not reliable without a real framebuffer such as Xvfb or an equivalent display backend.

## Visual Capture Rule

Use Godot `--headless` only for smoke/load checks. For screenshot validation, run
Godot under a real framebuffer:

```bash
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" --path . --rendering-driver opengl3
```

Screenshot harnesses must be temporary and untracked: `scripts/_*Shot.cs`,
`scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn`, and temporary autoload edits
in `project.godot`.

## Run In Godot

Open the project folder in Godot 4.6.3 mono and run the project. The project main scene is:

```text
scenes/ui/MainMenu.tscn
```

Playable flight is `scenes/flight/Flight.tscn`. Construction is `scenes/construction/Construction.tscn`.

## Controls

Flight:
- `Z` hold: ignition / throttle up
- `X` hold: throttle down
- `W/S`: pitch
- `A/D`: yaw
- `Q/E`: roll
- `T`: SAS
- `Space`: stage
- `G`: ascent autopilot
- `H`: gravity-turn assist mode
- `L`: countdown / launch flow
- `O`: jump to orbit debug helper
- `.` / `,`: warp up / down
- `F5` / `F9`: quicksave / quickload
- The local Sun/terrain lighting follows simulation time continuously. At x1, one
  sidereal surface rotation is 86,164 seconds; the solar day also includes the Sun's
  ephemeris motion. Use time warp to observe dawn, twilight and night, and enable the
  Full HUD density to see the live `SUN` elevation/phase.
- `C`: cycle camera presets and cockpit
- `V`: open VAB / construction scene
- Mouse right-drag: orbit/free-look camera
- Mouse wheel: zoom

Map:
- `M`: toggle map
- `1..6`: target Mars, Moon, Venus, Jupiter, Mercury, Saturn
- `Enter`: create/apply selected maneuver flow
- `J`: jump/debug to selected body
- `Tab`: cycle map mode
- `[` / `]`: adjust maneuver time
- `Shift`: larger maneuver step
- `Alt`: radial adjustment mode
- `Delete` / `Backspace`: clear maneuver

## Architecture

The project intentionally has two C# assemblies.

### `ExosphereSimulation/`

Pure C# simulation library. It must not reference Godot.

Rules:
- Use SI units: meters, m/s, kg, seconds, radians internally.
- Use double precision.
- Public sim names stay in English.
- Add tests for shared physics or construction behavior.

Important folders:
- `Math/`: double-precision math types
- `Integrators/`: RK4 and Kepler propagation
- `Parts/`: part definitions, runtime parts, graph, joints
- `Physics/`: aero, thermal, stress
- `Systems/`: life support, power, comms, thermal systems
- `Construction/`: VAB catalog and assembly model

### `scripts/`

Godot C# game layer. It may reference Godot and `ExosphereSimulation`.

`SimulationBridge` is the main boundary between Godot and the sim:
- owns `Universe`
- exposes `ActiveVessel`
- controls time warp
- spawns/places vessels
- bridges UI/controllers to the simulation

### `Exosphere.csproj`

This project must exclude sim, tests and Godot cache sources from its compile glob:

```xml
<Compile Remove="ExosphereSimulation/**/*.cs" />
<Compile Remove="ExosphereSimulation.Tests/**/*.cs" />
<Compile Remove=".godot/**/*.cs" />
```

Without this, Godot double-compiles the simulation and can accidentally compile xUnit test files into the game assembly.

## Data

Data lives in JSON:
- `data/bodies/*.json`
- `data/parts/*.json`
- `data/launch_sites/*.json`

Part attachment nodes drive construction. Stack nodes should match by type and size. Radial nodes match radial nodes. `engine_bell` nodes are not attachable in VAB V1.

The Starship default stack currently uses:
- `starship_command`
- `starship_tank`
- `starship_engines`
- `decoupler_heavy`
- `super_heavy_booster`

## Current Limitations

- VAB still exposes one engine part per stage. Runtime expands `engine_count` into per-instance `EngineInstanceState` (lifecycle, gimbal, feed, failure, torque); mesh clusters remain procedural visuals of those instances.
- VAB 2.0 still lacks drag/rotate gizmos. Catalog drag-ghost attach, symmetry, and part-rotation tools are not in the construction UI.
- Reentry has windward plasma, tile charring, survivable belly-flop EDL, thermal break-up VFX, per-piece structural breakup and control-loss authority. Richer shock/plasma rendering vs IFT references is still pending.
- Starship hull is modelled at the real 9 m diameter with procedural steel, weld seams, windward tiles, heat-shield borders, flaps, raceways, payload-door cues, access panels, vent/drain ports, flap leading-edge/tile-seam cues, Raptor clusters, denser liftoff plume/smoke, and refined Super Heavy grid fins with hinge/lattice detail. The Starbase tower has added carriage rails, catch-arm rub rails/rollers/cables and Ship QD cues. Engine startup now has pre-release glow/vapor/flicker, hot-staging has flash/plume VFX, vacuum burns suppress pad-like smoke, and reentry plasma uses heat-flux-driven cap/wake plus first-pass localized nose/belly/flap glow. The initial-flight pass now includes height-relative camera framing, opaque PBR steel, sky-based phase lighting, renderer-aware screen-space effects, layered plume alpha, and material-level Starbase LOD fades. Remaining visual work is fine reference matching and replacing the procedural local ground with geographically aligned terrain materials; see `docs/audits/INITIAL_FLIGHT_VISUAL_REALISM_2026-09-12.md`.
- CI provisions Godot in the workflow and runs the headless smoke checks strictly, with an anti-harness guard; full PNG capture in CI is still a follow-up.
- Interplanetary planning has tested Hohmann planetary transfers, a geocentric
  Lambert/B-plane Moon route, patched-conic SOI transitions, encounter prediction,
  TLI/LOI readouts and future-window burn arming. Remaining work includes executable
  LOI sequencing, dated lunar ephemerides and timeline maneuver nodes.
- Automated visual screenshots need a real framebuffer; current headless smoke tests only validate load/runtime.

## Working Rules

- Keep `ROADMAP.md` updated when a roadmap item changes state.
- Keep `README.md` and `CLAUDE.md` aligned with the actual repo.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses such as `scripts/_*Shot.cs`, `scripts/*VerifyShot.cs`, or `scenes/*VerifyShot.tscn`.
- Build and test before committing code.
