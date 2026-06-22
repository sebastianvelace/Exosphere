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
- Launchpad flight scene: `scenes/flight/Flight.tscn`
- Data-driven solar system with 8 body JSON files in `data/bodies/`
- Data-driven parts catalog with 20 JSON files in `data/parts/`
- Double-precision simulation types: `Vector3d`, `Quaterniond`, `Universe`, `Vessel`, `CelestialBody`, `OrbitalElements`
- RK4 integration, Kepler/on-rails propagation, SOI selection, patched-conic SOI transitions (warp-resolution-independent), radial/suborbital guards, hard-impact destruction
- Pressure-corrected engines, mass flow, Isp, staging and stage delta-v
- Orientation-dependent drag, reentry heating, progressive part thermal damage, heat-shield orientation handling and destruction causes
- Time warp levels: `1,2,3,5,10,50,100,1000,10000,100000`
- HUD, navball, map view, transfer planning helpers, cockpit, systems HUD, launch/crash/reentry visual effects
- Pure Hohmann transfer calculator in `ExosphereSimulation/Navigation` with Earth-Mars and Earth-Venus regression tests
- Patched-conic SOI transitions and encounter prediction for on-rails interplanetary coast
- Starship/Super Heavy procedural mesh with hot-stage ring, grid-fin lattice, windward tiles, flaps, Raptor clusters and stainless steel shader
- Survivable Starship EDL profile: belly-flop reentry, low-altitude flip-and-burn and soft touchdown
- VAB V1.5:
  - `ExosphereSimulation/Construction`
  - `scripts/ConstructionController.cs`
  - `scripts/VabPickingLayer.cs`
  - `scenes/construction/Construction.tscn`
  - catalog loading, compatible-node validation, mass/propellant/TWR/delta-v metrics, subtree delete, export to `Vessel`/`PartGraph`
  - 3D preview, direct click-to-attach node picking, craft JSON save/load, saved-craft browser panel, `V` from flight to VAB, `Launch` from VAB to pad
- Automated tests:
  - gravity
  - RK4 energy/radius conservation
  - Kepler round-trip
  - radial/suborbital detection
  - rails impact destruction
  - engine thrust/mass-flow/Isp equation
  - heat-shield orientation
  - aero drag scaling
  - SOI selection
  - VAB catalog/assembly/export behavior

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

Open the project folder in Godot 4.6.3 mono and run the project. The main scene is:

```text
scenes/flight/Flight.tscn
```

The construction scene is:

```text
scenes/construction/Construction.tscn
```

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
- `Backspace`: warp x1
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

- One physical engine part per stage; 33/6 engines are visual, not individual physical engines.
- VAB V1.5 has 3D preview, click-to-attach node picking, craft-file persistence, VAB-to-launch flow, and a saved-craft browser panel. It still lacks drag/rotate gizmos and a dedicated main-menu flow.
- Reentry has windward plasma glow, progressive heat-shield tile charring, survivable belly-flop EDL, and a thermal break-up VFX when a vessel burns up. Still limited: per-piece structural break-up, control-loss consequences, and richer plasma/shock visuals.
- Starship hull is modelled at the real 9 m diameter with procedural steel, weld seams, windward tiles, heat-shield borders, flaps, raceways, payload-door cues, Raptor clusters and denser liftoff plume/smoke. Engine startup now has pre-release glow/vapor/flicker, and after staging the separated Super Heavy shows an exposed, scorched hot-stage ring with hot-staging flash/plume VFX. Reentry plasma uses heat-flux-driven cap/wake plus first-pass localized nose/belly/flap glow. Remaining visual work is close-up fidelity, vacuum plume behavior, EDL reference captures, lighting/camera polish, and verified screenshots.
- CI provisions Godot in the workflow and runs the headless smoke checks strictly, with an anti-harness guard; full PNG capture in CI is still a follow-up.
- Interplanetary planning has a tested Hohmann core, patched-conic SOI transitions, encounter prediction, and better node readouts. It still needs long-cruise validation, a more accurate Moon-transfer model, and draggable maneuver nodes.
- Automated visual screenshots need a real framebuffer; current headless smoke tests only validate load/runtime.

## Working Rules

- Keep `ROADMAP.md` updated when a roadmap item changes state.
- Keep `README.md` and `CLAUDE.md` aligned with the actual repo.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses such as `scripts/_*Shot.cs`, `scripts/*VerifyShot.cs`, or `scenes/*VerifyShot.tscn`.
- Build and test before committing code.
