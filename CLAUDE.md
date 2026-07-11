# Exosphere Agent Notes

This file is the short operational guide for agents working in this repo. `ROADMAP.md` is the live product plan. `PLAN_REALISM.md` is the physics/telemetry audit log. `PLAN_VISUAL_REALISM.md` is the next visual-fidelity track. Do not look for `PLAN_MEJORAS.md`; it has been retired.

## Required Checks

Run these after C# changes:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
```

Or run the local aggregate:

```bash
bash tools/ci_check.sh
```

Run Godot smoke checks when scene/game-layer behavior changes:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3
```

For fast VAB iteration (cached builds + construction tests + VAB scene smoke):

```bash
bash tools/vab_quick_check.sh
```

Use the full `tools/ci_check.sh` once before committing instead of after every UI edit.

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
- heat-shield flag from part JSON
- hard impact destruction
- VAB catalog/assembly/export in `ExosphereSimulation/Construction`
- Hohmann transfer math, encounter prediction, and patched-conic SOI transitions in `ExosphereSimulation/Navigation` and `Universe`

## Game Layer Rules

- Godot code lives under `scripts/` and namespace `Exosphere.Game`.
- Main scene: `scenes/flight/Flight.tscn`.
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
- `data/bodies/*.json` — an atmosphere must declare `molar_mass`, `surface_gravity` and
  `geopotential_radius`; the defaults are Earth's, so omitting them silently holds an alien
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

- One physical engine part per stage. The 33 Super Heavy and 6 Starship engines are visual, not individually simulated.
- VAB V1.6 has filtered catalog, double-click auto-attach, Starter/Starship templates, undo/redo, launch validation, 3D picking, craft persistence, and VAB-to-launch flow. It still lacks drag/rotate gizmos, radial symmetry, and a dedicated menu flow.
- Reentry has physics basis and tests plus windward plasma, tile charring, survivable belly-flop EDL, and a thermal break-up VFX. It still lacks per-piece structural break-up, control-loss consequences, and richer shock/plasma rendering.
- Patched-conic SOI transitions are implemented for on-rails vessels (warp-resolution-independent); inside on-rails propagation use `BodyStateAt(body, t)` for body state at the epoch/crossing time, not the end-of-tick global position.
- Interplanetary planning has a tested Hohmann core, patched-conic SOI transitions, encounter prediction, and maneuver readouts. It still needs long-cruise validation, a better Moon-transfer model, and draggable maneuver nodes.
- CI builds/tests the sim, builds the Godot C# layer, downloads Godot 4.6.3 mono in GitHub Actions, and runs strict headless smoke checks. Local `tools/ci_check.sh` runs Godot smoke only when `GODOT_BIN` or the default local Godot path exists.
- Godot `--headless` in this environment uses a dummy renderer, so viewport PNG capture needs a real framebuffer.
- Current product priority after documentation cleanup: visual fidelity against real Starship/Super Heavy references. Prefer scoped improvements to `VesselRenderer`, `ReentryPlasmaController`, `PlumeSystem`, camera/lighting, and visual capture before broad new gameplay systems.

## Workflow

- Keep `ROADMAP.md` current when closing or adding roadmap items.
- Keep `README.md` and this file aligned with the repo.
- One coherent commit per task.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses such as `scripts/_*Shot.cs`,
  `scripts/*VerifyShot.cs`, or `scenes/*VerifyShot.tscn`.
- Do not break `[G]` ascent or EDL while working on unrelated features.
