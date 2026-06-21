# Exosphere Agent Notes

This file is the short operational guide for agents working in this repo. `ROADMAP.md` is the live product plan. Do not look for `PLAN_MEJORAS.md`; it has been retired.

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

For VAB:

```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --headless --path . --quit-after 3 --rendering-driver opengl3 \
  res://scenes/construction/Construction.tscn
```

Expected standard: 0 warnings, 0 errors, tests passing.

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
- Hohmann transfer math in `ExosphereSimulation/Navigation`

## Game Layer Rules

- Godot code lives under `scripts/` and namespace `Exosphere.Game`.
- Main scene: `scenes/flight/Flight.tscn`.
- VAB scene: `scenes/construction/Construction.tscn`.
- Flight opens VAB with `V`; VAB launches the current craft through `CraftLaunchRequest`.
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
- `data/bodies/*.json`
- `data/parts/*.json`
- `data/launch_sites/*.json`

Do not hardcode physical constants in game code when they belong in JSON. Copy the schema of nearby files when adding data.

Attachment-node rules used by VAB V1:
- stack nodes attach only to stack nodes of the same size
- radial nodes attach only to radial nodes
- `engine_bell` nodes are not attachable

The default Starship stack uses `decoupler_heavy`, not `decoupler_medium`, because the Starship stack uses size-3 nodes.

## Known Limits

- One physical engine part per stage. The 33 Super Heavy and 6 Starship engines are visual, not individually simulated.
- VAB V1.5 has 3D preview, craft persistence, VAB-to-launch flow, and a saved-craft browser panel. It still lacks direct node manipulation in the 3D preview.
- Reentry has physics basis and tests plus windward plasma, tile charring, and a thermal break-up VFX. It still lacks per-piece structural break-up and control-loss consequences.
- Patched-conic SOI transitions are implemented for on-rails vessels (warp-resolution-independent); inside on-rails propagation use `BodyStateAt(body, t)` for body state at the epoch/crossing time, not the end-of-tick global position.
- Interplanetary planning has a tested Hohmann core, but needs stronger patched-conic validation and better UX.
- CI is configured for simulation build/tests. Godot build/smoke is strict in `tools/ci_check.sh` and optional in GitHub Actions unless `GODOT_BIN` is provided.
- Godot `--headless` in this environment uses a dummy renderer, so viewport PNG capture needs a real framebuffer.

## Workflow

- Keep `ROADMAP.md` current when closing or adding roadmap items.
- Keep `README.md` and this file aligned with the repo.
- One coherent commit per task.
- Do not commit generated files: `.godot/`, `bin/`, `obj/`, `*.uid`.
- Do not commit temporary visual harnesses such as `scripts/_*Shot.cs`.
- Do not break `[G]` ascent or EDL while working on unrelated features.
