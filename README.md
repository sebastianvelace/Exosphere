# Exosphere — Space Mission Simulator

**Full-stack SpaceX Starship simulation: countdown at Starbase → orbit → interplanetary → landing on Mars**

> Engine: Godot 4.3 + C# (.NET 8) | Physics: custom double-precision C# library | Scale: real solar system

---

## Table of Contents

1. [Vision](#vision)
2. [Core Pillars](#core-pillars)
3. [Stack](#stack)
4. [Development Setup](#development-setup)
5. [Project Structure — Current State](#project-structure--current-state)
6. [Simulation Library Reference](#simulation-library-exospheresimulation)
7. [Game Layer Scripts Reference](#game-layer-scripts)
8. [Architecture Deep Dive](#architecture-deep-dive)
9. [Data Formats](#data-formats)
10. [Mission Architecture](#mission-architecture)
11. [Visual Design Specification](#visual-design-specification)
12. [VFX System Specification](#vfx-system-specification)
13. [Audio System Specification](#audio-system-specification)
14. [Implementation Status (Session-by-Session)](#implementation-status)
15. [Development Roadmap — Semanas 6–12](#development-roadmap--semanas-612)
16. [Known Issues and Bugs](#known-issues-and-bugs)
17. [Inspirations](#inspirations)

---

## Vision

Exosphere simulates a complete SpaceX Starship mission from T-10 seconds at Starbase, Texas, to powered landing on Mars. The experience is physically accurate (real masses, ISP, orbital mechanics), visually spectacular (dynamic sky, GPU particle plumes, reentry plasma), and interactive (manual piloting + optional autopilot assistance).

**The mission arc:**
1. Countdown at Starbase (Boca Chica, TX) — atmosphere, tower, checklist
2. Super Heavy ascent — 33 Raptors, Max-Q, MECO at 65 km
3. Stage separation — Super Heavy flip + boostback + Mechazilla catch
4. Starship to orbit — 6 Raptors, circularization burn
5. Trans-Mars Injection — maneuver planner, deltaV calculation
6. Interplanetary cruise — heavy time warp, solar system map
7. Mars EDL — reentry plasma, flap guidance, retrofire, landing legs
8. Touchdown on Mars

---

## Core Pillars

| Pillar | Description |
|---|---|
| **Physically Accurate** | Real masses, ISP, atmospheric models (ISA layers), N-body gravity, RK4 integration |
| **Complete Mission Arc** | From launchpad surface to another planet — no teleportation, no cuts |
| **SpaceX Starship Fidelity** | Super Heavy + Starship visually and physically faithful to the real vehicle |
| **Manual + Assisted** | Player controls everything; autopilot available per-phase (gravity turn, circularization) |
| **Real Solar System** | All 8 planets + Moon with real NASA data |

---

## Stack

| Layer | Technology |
|---|---|
| Game engine | Godot 4.3+ (.NET version required) |
| Language | C# (.NET 8) — double precision throughout |
| Rendering | Godot Vulkan / Forward+ renderer |
| Shaders | Godot Shader Language (GLSL subset) |
| Orbital physics | Custom C# library (ExosphereSimulation/) — zero Godot dependency |
| Audio | Godot AudioStreamPlayer3D + AudioStreamGenerator (synthesis) |
| Data | JSON — part defs, body data, save files |

### Why double precision
Float32 loses sub-meter precision beyond ~16,000 km. Earth-Moon is 384,000 km. The full solar system is ~10^13 m. All simulation math uses `double`. Rendering uses `float32` via the Floating Origin pattern (vessel always at world origin).

### Compile exclude pattern (critical)
`Exosphere.csproj` must have this or Godot double-compiles ExosphereSimulation:
```xml
<ItemGroup>
  <Compile Remove="ExosphereSimulation/**/*.cs" />
</ItemGroup>
```

---

## Development Setup

```bash
# Prerequisites: Godot 4.3+ .NET version + .NET 8 SDK

# Build simulation library
dotnet build ExosphereSimulation/ExosphereSimulation.csproj

# Open in Godot editor
# File → Open Project → select "space simulator/" directory
# Press F5 to run (Flight.tscn is the main scene)
```

**Running the game**: Hit F5. The game loads `scenes/flight/Flight.tscn`. Currently starts in LEO at 250 km (test vessel). Semana 6 will change this to start on the launchpad.

**Controls (current)**:
- `Z / X` — throttle +5% / -5%
- `W / S` — pitch (nose up/down)
- `A / D` — yaw (nose left/right)
- `Q / E` — roll (spin axial)
- `T` — toggle SAS
- `Space` — stage
- `, / .` — warp down/up
- `Backspace` — warp ×1
- Right-click drag — orbit camera
- Scroll — zoom

---

## Project Structure — Current State

```
space simulator/
│
├── project.godot                    Godot 4 config; Forward Plus; 50 Hz physics
├── Exosphere.csproj                 Game layer; references ExosphereSimulation
├── ExosphereSimulation.sln          .NET solution
│
├── ExosphereSimulation/             Pure C# lib — NO Godot dependency
│   ├── Universe.cs                  Root container; 3 warp modes; SOI detection
│   ├── CelestialBody.cs             Planet/moon; JSON loading; gravity; atmosphere
│   ├── Vessel.cs                    Spacecraft; physics; staging; SAS; PitchYawRoll
│   ├── OrbitalElements.cs           Keplerian elements; state vector conversion
│   ├── AtmosphereModel.cs           ISA layered model (partial class)
│   ├── AtmosphereModelJson.cs       partial — adds FromJson()
│   ├── CrewMember.cs                Astronaut; EVA; risk
│   │
│   ├── Math/
│   │   ├── Vector3d.cs              Double-precision 3D vector
│   │   ├── Quaterniond.cs           Double-precision quaternion
│   │   └── MathUtils.cs             G, AU, Kepler solver
│   │
│   ├── Integrators/
│   │   ├── RK4Integrator.cs         4th-order Runge-Kutta
│   │   └── KeplerPropagator.cs      Analytical Keplerian propagation
│   │
│   ├── Parts/
│   │   ├── PartDefinition.cs        JSON data; category enum; factory
│   │   ├── Part.cs                  Runtime: fuel, temperature, thrust vector
│   │   ├── PartGraph.cs             Part tree; CoM; cross-feed; staging
│   │   └── Joint.cs                 Structural connection; tensile/shear loads
│   │
│   └── Physics/
│       ├── ThermalModel.cs          DKR heat flux; radiation cooling
│       ├── AerodynamicsModel.cs     Drag; Mach; transonic multiplier
│       └── StressSolver.cs          Joint loads; break detection
│
├── scripts/                         Godot C# game layer
│   ├── SimulationBridge.cs          [GlobalClass] owns Universe; drives Tick()
│   ├── FloatingOrigin.cs            Precision: vessel always at render origin
│   ├── TimeWarpController.cs        8-step warp [1,5,10,50,100,1k,10k,100k]
│   ├── NavBallController.cs         Prograde/retrograde/normal/radial vectors
│   ├── HUDController.cs             9 readouts; keyboard input; rotation control
│   ├── VesselRenderer.cs            Starship procedural mesh; plume system
│   ├── CameraController.cs          Orbit cam (right-drag + scroll); 3 modes planned
│   └── SaveSystem.cs                JSON save/load ~/.local/share/Exosphere/saves/
│
├── scenes/
│   └── flight/
│       └── Flight.tscn              Main scene; starts in LEO currently
│
├── data/
│   ├── bodies/                      8 JSON files (Sun, Earth, Moon, Mars, Venus,
│   │                                Mercury, Jupiter, Saturn) — real NASA values
│   └── parts/                       15 JSON files
│       ├── command_pod_mk1.json
│       ├── fuel_tank_small/medium/large.json
│       ├── engine_liquid_sl.json    Merlin 1D equivalent; 845 kN vac; ISP 311 s
│       ├── engine_liquid_vac.json
│       ├── srb_kickback.json
│       ├── decoupler_small/medium.json
│       ├── landing_leg.json
│       ├── parachute_mk1.json
│       └── ... (battery, solar, rcs, fairing)
│
└── assets/
    └── shaders/
        ├── atmosphere.gdshader      Rayleigh+Mie scattering (limb glow)
        └── reentry_glow.gdshader    Heat-driven plasma
```

**Files NOT yet created** (planned for Semanas 6-12, listed in roadmap below):
- `scripts/MissionManager.cs`
- `scripts/LaunchPadController.cs`
- `scripts/CountdownController.cs`
- `scripts/StarshipRenderer.cs`
- `scripts/SuperHeavyRenderer.cs`
- `scripts/SkyController.cs`
- `scripts/PlumeSystem.cs`
- `scripts/MapViewController.cs`
- `scripts/ManeuverPlanner.cs`
- `scripts/AudioManager.cs`
- `scripts/CockpitController.cs`
- `assets/shaders/sky_atmosphere.gdshader`
- `assets/shaders/engine_plume.gdshader`
- `assets/shaders/max_q_ring.gdshader`
- `assets/shaders/volumetric_clouds.gdshader`
- `data/parts/super_heavy_booster.json`
- `data/parts/starship_ship.json`

---

## Simulation Library (`ExosphereSimulation/`)

Pure C# class library. No Godot dependency. Build independently with `dotnet build ExosphereSimulation/ExosphereSimulation.csproj`.

---

### `Math/Vector3d.cs`
```
namespace: Exosphere.Simulation.Math
type:       readonly struct

Properties: X, Y, Z (double); Magnitude; MagnitudeSquared; Normalized
Static: Zero, One, Up, Forward, Right
Methods: Dot, Cross, DistanceTo, Lerp
Operators: +, -, *(scalar), /(scalar), ==, !=, unary-
```

### `Math/Quaterniond.cs`
```
namespace: Exosphere.Simulation.Math
type:       struct

Fields: W, X, Y, Z (double)
Static: Identity
Methods: Normalize(); Inverse(); Rotate(Vector3d); Slerp(q, t)
Static: FromAxisAngle(axis, rad); FromEuler(pitch, yaw, roll degrees)
Operators: * (quaternion multiply)
```

### `Math/MathUtils.cs`
```
Constants: G=6.674e-11, AU=1.496e11, DEG_TO_RAD, RAD_TO_DEG
Methods:
  SolveKeplerEquation(M, e, tol=1e-10) -> double E  (Newton-Raphson)
  OrbitalToInertial(a,e,nu,i,Omega,omega) -> Vector3d
  OrbitalToInertialStateVector(...)      -> (pos, vel)
```

### `OrbitalElements.cs`
```
namespace: Exosphere.Simulation

Properties: SemiMajorAxis, Eccentricity, Inclination, LongitudeOfAscendingNode,
            ArgumentOfPeriapsis, MeanAnomalyAtEpoch, Epoch (double seconds),
            ReferenceBodyId (string)
Computed: Apoapsis = a*(1+e), Periapsis = a*(1-e)  [meters from body center]

Methods:
  GetMeanAnomaly(t, gm) -> double
  GetStateAtTime(t, gm) -> (Vector3d pos, Vector3d vel)  inertial, relative to body

Static:
  FromStateVector(pos, vel, gm, bodyId, epoch) -> OrbitalElements
  -- vis-viva: epsilon = v²/2 - GM/r; a = -GM/(2*epsilon)
  -- h = r × v; e_vec = v × h / GM - r_hat
```

### `CelestialBody.cs`
```
namespace: Exosphere.Simulation

Init (from JSON): Id, Name, Mass, Radius, GM, SphereOfInfluence,
                  RotationalPeriod (s; negative=retrograde), AxialTilt (deg),
                  Atmosphere (AtmosphereModel?), OrbitalElements?
Runtime: Position (Vector3d), Velocity (Vector3d)

Methods:
  GetSurfaceGravity()           -> double  GM/R²
  GetAltitude(worldPos)         -> double  m above surface (negative = underground)
  IsInAtmosphere(worldPos)      -> bool
  GetAtmosphericDensity(pos)    -> double  kg/m³
  GetAtmosphericPressure(pos)   -> double  Pa
  GetGravityAt(worldPos)        -> Vector3d  m/s²
  GetSurfaceVelocity(worldPos)  -> Vector3d  ω × r (rotational)

Factories:
  LoadFromJson(path)           -> CelestialBody
  LoadAllFromDirectory(dir)    -> Dictionary<string, CelestialBody>
```

### `Universe.cs`
```
namespace: Exosphere.Simulation

Properties:
  Bodies        IReadOnlyList<CelestialBody>
  Vessels       IReadOnlyList<Vessel>
  CurrentTime   double  s since J2000 (private set)
  TimeScale     double  warp multiplier (1.0 default)
  ActiveVessel  Vessel?

Methods:
  AddBody / AddVessel / RemoveVessel
  GetBody(id)           -> CelestialBody?
  GetDominantBody(pos)  -> CelestialBody  smallest SOI containing pos; fallback = Sun
  Tick(realDeltaTime)   -> void  advances by realDt * TimeScale

Warp modes (internal):
  TimeScale ≤ 4:    TickPhysics(dt)         full RK4, 50 Hz sub-steps
  TimeScale ≤ 1000: TickPhysicsMixed(dt)    active vessel RK4; others Keplerian
  TimeScale > 1000: TickRails(dt)           all vessels + bodies Keplerian

GetDominantBody logic (IMPORTANT):
  Picks body with smallest SphereOfInfluence that CONTAINS the position.
  This correctly gives: Moon SOI < Earth SOI < Sun SOI.
  Falls back to most massive body (Sun) when no SOI contains the position.
```

### `Vessel.cs`
```
namespace: Exosphere.Simulation

Properties:
  Id, Name (string)
  Parts (PartGraph)
  Position, Velocity (Vector3d, inertial m)
  Orientation (Quaterniond)
  AngularVelocity (Vector3d, rad/s world space)
  IsOnRails (bool)
  OrbitalState (OrbitalElements? — valid when IsOnRails=true)
  ReferenceBodyId (string?)
  Throttle (double [0,1])
  PitchYawRoll (Vector3d — local space; X=pitch, Y=yaw, Z=roll, each [-1,1])
  SASEnabled (bool)
  Crew (List<CrewMember>)

Computed: TotalMass, CenterOfMass

Methods:
  GetAltitude(body)            -> double m
  GetSurfaceVelocity(body)     -> Vector3d (relative to rotating atmosphere)
  ComputeThrust()              -> Vector3d N (world space; orientation-rotated)
  ComputeDrag(body)            -> Vector3d N (Mach-adjusted; surface-relative velocity)
  ComputeGravity(bodies)       -> Vector3d m/s²
  ComputeNetAcceleration(...)  -> Vector3d m/s²
  Tick(dt, refBody)            -> void
    -- ConsumePropellant (cross-feed from all tanks)
    -- Apply PitchYawRoll → AngularVelocity (ControlAuthority = 0.6 rad/s² per unit)
    -- SAS: damp AngularVelocity only when PitchYawRoll.Magnitude < 0.01
    -- Integrate AngularVelocity → Orientation (axis-angle)
    -- Max angular velocity clamped at 0.35 rad/s (~20°/s)
  Stage()                      -> Vessel?  debris vessel or null

PLANNED additions (Semana 6):
  IsGroundHeld (bool)  — set by LaunchPadController; prevents physics until T-0
  EarthRotationVelocity  — ~412 m/s east at Starbase lat 26.1°N applied at release
```

### `Parts/PartDefinition.cs`
```
enum PartCategory: Command, Engine, FuelTank, Structure,
                   Electrical, Landing, Decoupler, Fairing, RCS

Properties:
  Id, Name, Description, CategoryStr (string → Category enum)
  MassDry (kg), Cost, DragCoefficient, HeatTolerance (K)
  ThrustVac, ThrustSL (N), IspVac, IspSL (s), GimbalRange (deg)
  FuelTypeStr (e.g. "LiquidFuel+Oxidizer")
  FuelCapacityLF, FuelCapacityOx, FuelCapacitySolid, FuelCapacityMono (kg)
  ECCapacity, MaxCrew, Deployable, DragChute, DeployAltitude
  AttachmentNodes: List<{ Id, Position[3], Size, Type }>

Factories: LoadFromJson(path), LoadAllFromDirectory(dir)
```

### `Parts/Part.cs`
```
Properties:
  InstanceId (GUID string), Definition
  LiquidFuel, Oxidizer, SolidFuel, Monopropellant, ElectricCharge (double kg)
  Temperature (double K, starts 290), IsBroken, IsStagingActive, IsDeployed
  ThrottleLevel ([0,1]), GimbalOffset (Vector3d)
  CurrentMass = MassDry + all propellants

Methods:
  ResetResources()           — fill all tanks to capacity
  GetThrustVector()          -> Vector3d local space (gimbal-deflected)
  ConsumePropellant(dt, Pa)  -> bool  false = no fuel (legacy; now handled by PartGraph)

IMPORTANT: ConsumePropellant on Part is kept for SRBs and RCS that have their own
propellant. Liquid-fuel engines use cross-feed via PartGraph.ConsumePropellant().
```

### `Parts/PartGraph.cs`
```
Properties:
  Root, Parts (IReadOnlyList), Joints (IReadOnlyList)
  TotalMass, DryMass, TotalLiquidFuel, TotalOxidizer
  ActiveEngines: engines where IsStagingActive=true and !IsBroken
  CenterOfMass: mass-weighted average of ComputePartLocalPositions()

Methods:
  SetRoot/AddPart/AddJoint
  GetTotalThrust()       -> Vector3d local space
  ConsumePropellant(dt, Pa)
    -- CROSS-FEED: aggregates mass flow from all active engines
    -- Drains proportionally from ALL tanks in the graph
    -- Flame-out if total fuel < needed; sets IsStagingActive=false on all engines
  FireNextStage()        -> PartGraph?  detaches first active Decoupler subtree
  ComputePartLocalPositions() -> Dictionary<Part, Vector3d>

ConsumePropellant algorithm:
  1. Sum (thrust_vac × throttle) / (isp × 9.80665) for each engine → totalMassFlow
  2. Split by fuel type: LF+Ox (9:11 ratio), solid, mono
  3. Compare against sum across all Parts in graph
  4. If enough: drain proportionally from each part (tank.fuel / totalFuel × needed)
  5. If not enough: flame out all engines
```

### `Parts/Joint.cs`
```
Properties:
  Parent, Child (Part), ParentNodeId, ChildNodeId (string)
  TensileStrength, CompressiveStrength, ShearStrength (N; scaled by nodeSize²)
  CurrentTensileLoad, CurrentShearLoad (N; updated each tick by StressSolver)
  IsBreaking (bool)
```

### `Physics/ThermalModel.cs`
```
Static methods:
  ComputeHeatFlux(density kg/m³, velocity m/s)
    -> double W/m²  formula: 1.83e-4 * sqrt(rho) * v³  (simplified DKR)

  UpdateTemperature(currentTemp K, heatFlux W/m², dt s, mass kg=100)
    -> double K  Newton cooling + Stefan-Boltzmann radiation; min 3K

  ApplyHeat(part, heatFlux, dt) -> bool  true if part.Temperature > HeatTolerance
```

### `Physics/AerodynamicsModel.cs`
```
Static methods:
  ComputeDrag(density, surfaceVelocity, Cd, area) -> Vector3d N
  ComputeDynamicPressure(density, speed)          -> double Pa  (q = 0.5 ρ v²)
  ComputeMach(speed, temperature)                 -> double
  GetMachDragMultiplier(mach)
    -> 1.0 subsonic; peaks 2.0 at Mach 1.0-1.2; decays back to 1.0 above Mach 5
  EstimateReferenceArea(graph) -> double m²
```

### `Physics/StressSolver.cs`
```
Static methods:
  ComputeLoads(graph, netAcceleration, orientation)
    -> void  walks part tree; updates Joint.CurrentTensileLoad/CurrentShearLoad
    NOTE: uses non-gravitational acceleration (felt g-force)
  FindBreakingJoints(graph) -> IEnumerable<Joint>
  ApplyThermalLoads(graph, heatFlux, dt) -> List<Part>  destroyed parts
```

### `Integrators/RK4Integrator.cs`
```
Static methods:
  Step(state double[n], t, dt, derivative Func<double[],double,double[]>)
    -> double[n]
  StepPosVel(pos, vel, t, dt, acceleration Func<Vector3d,Vector3d,double,Vector3d>)
    -> (Vector3d newPos, Vector3d newVel)
    NOTE: packs (pos,vel) into double[6] internally
```

### `Integrators/KeplerPropagator.cs`
```
Static methods:
  PropagateToTime(elements, targetTime, gm) -> (Vector3d pos, Vector3d vel)
  ComputeElements(relPos, relVel, gm, bodyId, epoch) -> OrbitalElements
  PropagateAllBodies(bodies, targetTime)
    -> void  updates all CelestialBody.Position/Velocity
    NOTE: Sun (no OrbitalElements) stays fixed at origin
```

### `AtmosphereModel.cs`
```
Properties: MaxAltitude, SeaLevelDensity, ScaleHeight, SeaLevelPressure,
            MolarMass, Layers (List<AtmosphereLayer>)

Methods:
  GetDensity(altitude)   -> double kg/m³
    -- Uses ISA layer table if available (per-layer hydrostatic pressure)
    -- Falls back to exponential: rho_0 * exp(-alt/H)
  GetPressure(altitude)  -> double Pa

Static: Earth(), Mars(), Venus()
Partial extension (AtmosphereModelJson.cs): FromJson(JsonElement)
```

---

## Game Layer Scripts

All in `scripts/`. Partial classes inheriting Godot node types.

---

### `scripts/MissionManager.cs`
```
[GlobalClass] partial class MissionManager : Node
Static: Instance (set in _Ready)

Enum MissionPhase: PRE_LAUNCH, COUNTDOWN, IGNITION, LIFTOFF,
  ASCENT_SH, MAX_Q, MECO, SEPARATION, ASCENT_SHIP, ORBIT, COAST, LANDED

Properties:
  Phase (MissionPhase)   — current phase
  CountdownTimer (double) — seconds remaining until T-0
  IsCountingDown (bool)

Signals:
  PhaseChanged(string phaseName)
  LaunchCommitted()

API:
  StartCountdown()  — called by L key in HUDController; PRE_LAUNCH only
  NotifyStaged()    — called by SimulationBridge.TriggerStaging()

_Process(delta):
  -- Decrements CountdownTimer if IsCountingDown
  -- At CountdownTimer <= 3.0: sets throttle to 1.0, changes to IGNITION
  -- At CountdownTimer <= 0.0: calls bridge.ReleaseGroundHold(), sets LIFTOFF
  -- Auto detects Max-Q (0.5*rho*v² peak in 8-30 km band)
  -- Auto detects MECO when SH fuel < 10,000 kg at altitude > 55 km
  -- SEPARATION → auto re-lights Starship engines when active engines found
  -- ASCENT_SHIP → ORBIT when altitude > 150 km and speed > 7500 m/s
```

### `scripts/LaunchPadController.cs`
```
partial class LaunchPadController : Node3D
Static: Instance (set in _Ready)

_Ready(): builds all geometry (no exports, fully procedural)

Geometry (all in local Y-up space, y=0 = Earth surface directly below vessel):
  Ground apron:  BoxMesh 600×0.4×600  concrete  y=-0.2
  FlameTrench:   BoxMesh 24×6×65      burnt      y=-3
  OLMBase:       BoxMesh 16×10×16     steel      y=+5
  OLMPedestal:   CylinderMesh r=4.2→4.8 h=5      y=+12.5
  DelugeRing:    CylinderMesh r=8.5→9   h=0.7     y=+10.35
  Tower:         BoxMesh 11×160×11    steel      x=-20 y=+80
  ArmUpper/Lower: horizontal BoxMesh arms at y=+115 and y=+88

Positioning: SimulationBridge._Process() sets:
  launchPad.Position = (padWorldPos - vesselWorldPos)  [1:1 scale]
  padWorldPos = earth.Position + upDir * earth.Radius  [saved at spawn]
  launchPad.Visible = altitude < 8000 m
```

### `scripts/SimulationBridge.cs`
```
[GlobalClass] partial class SimulationBridge : Node
Static: Instance (set in _Ready)

Properties: Universe, ActiveVessel
Signals: VesselStaged(id), VesselDestroyed(id), SimulationLoaded()

_Ready():
  -- loads Universe from "res://data"
  -- Creates MissionManager + LaunchPadController as dynamic children
  -- SpawnStarshipStack() — full stack on Earth surface at north pole, altitude 12 m
  -- SpawnPlanets() — SphereMesh per body with atmosphere glow child node
  -- Sets camera far clip to 2,000,000

_Process(delta):
  -- Universe.Tick(delta) every frame
  -- Updates LaunchPad position: launchPad.Position = (padWorldPos - vessel.Position)
  -- Hides LaunchPad when altitude > 8 km

SpawnStarshipStack():
  -- Parts: starship_command → starship_tank → starship_engines → decoupler → super_heavy_booster
  -- Position: earth.Position + (0, earth.Radius + 12, 0)  [north pole, 12m AGL]
  -- Velocity: earth.Velocity + earth.GetSurfaceVelocity(vessel.Position)
  -- vessel.IsGroundHeld = true; GroundNormal=(0,1,0); GroundOffset=12.0
  -- Creates VesselRenderer, registers with FloatingOrigin

SpawnPlanets():
  -- PlanetRenderScale = 1/10000 (Earth renders at ~637 Godot units radius)
  -- Each planet: SphereMesh + StandardMaterial3D (AlbedoColor + Emission)
  -- Earth gets SpawnAtmosphereGlow() child (cull-front emission sphere at 1.028× radius)
  -- Registered via fo.RegisterPlanetNode(body.Id, mesh)

Planet colors: earth=ocean blue, moon=gray, mars=red-brown, sun=yellow,
               venus=ochre, mercury=dark gray, jupiter=tan, saturn=beige
```

### `scripts/FloatingOrigin.cs`
```
partial class FloatingOrigin : Node
[Export] SceneRootPath NodePath

PlanetRenderScale = 1/10000  (const)

Dictionaries:
  _bodyNodes   string → Node3D  (full-scale simulation position, rarely used)
  _vesselNodes string → Node3D  (vessel renderers; active vessel at world origin)
  _planetNodes string → Node3D  (planet meshes at reduced scale)

_Process(delta):
  renderOrigin = activeVessel.Position
  -- vessels: node.Position = float(simPos - origin); node.Quaternion = vessel orientation
  -- planets: node.Position = float((body.Position - origin) × PlanetRenderScale)
  NOTE: planet nodes use body.Id as key; loop is over Universe.Bodies, not _planetNodes

Methods:
  RegisterBodyNode(bodyId, Node3D)
  RegisterVesselNode(vesselId, Node3D)
  UnregisterVesselNode(vesselId)
  RegisterPlanetNode(bodyId, Node3D)
```

### `scripts/VesselRenderer.cs`
```
partial class VesselRenderer : Node3D

Properties: TargetVessel (Vessel?)
Fields: _partNodes Dict<instanceId, Node3D>; _hullMesh MeshInstance3D?;
        _plumes List<MeshInstance3D>

BuildFromVessel(vessel):
  -- Clears children
  -- If vessel has any Engine part → BuildStarship(vessel)
  -- Otherwise → BuildGenericVessel(vessel)

BuildStarship(vessel):  ← CURRENT STATE (geometry ok, proportions need improvement in Semana 7)
  Materials:
    steelMat:     Color(0.86, 0.86, 0.88), Metallic=0.92, Roughness=0.18
    tileMat:      Color(0.09, 0.09, 0.11), Metallic=0.04, Roughness=0.94
    darkSteelMat: Color(0.50, 0.50, 0.53), Metallic=0.88, Roughness=0.32
    engineMat:    Color(0.18, 0.18, 0.20), Metallic=0.82, Roughness=0.38

  Mesh nodes (Y-up, nose at top, origin at CoM):
    BodyUpper   CylinderMesh r=1.15 h=7    steelMat   y=+3.5
    BodyLower   CylinderMesh r=1.15 h=7    tileMat    y=-3.5
    Nose        CylinderMesh top=0.04 bot=1.15 h=5  steelMat  y=+9.5
    CanardL/R   BoxMesh 0.12×1.6×2.6       darkSteel  x=±1.23 y=+5.5
    CanardRootL/R BoxMesh 0.18×2.0×1.0     steelMat   x=±1.16 y=+5.5
    FlapL/R     BoxMesh 0.14×5.5×4.6       tileMat    x=±1.23 y=-4.5
    FlapRootL/R BoxMesh 0.20×5.5×1.2       tileMat    x=±1.16 y=-4.5
    Skirt       CylinderMesh top=1.15 bot=1.08 h=2  darkSteel  y=-8
    RapVac0-2   CylinderMesh top=0.19 bot=0.44 h=2.1  engineMat  ring r=0.38
    RapSL0-2    CylinderMesh top=0.21 bot=0.33 h=1.4  engineMat  ring r=0.72 +60°

  Plume nodes (hidden when throttle=0):
    PlumeVac0-2  CylinderMesh cone h=5.0 r=0.55  orange emission  below vac engines
    PlumeSL0-2   CylinderMesh cone h=3.5 r=0.42  orange emission  below SL engines
    Plumes scale and flicker with throttle; EmissionEnergyMultiplier = 2.5 + throttle*2

_Process(delta):
  -- Plumes: visible = throttle>0.01; Scale = throttle*flicker; update emission
  -- Heat glow: part.Temperature → orange emission on _hullMesh

ISSUE (to fix Semana 7): nosecone is too sharp; canards too large; SH not modeled.
```

### `scripts/CameraController.cs`
```
partial class CameraController : Node3D
[Export] OrbitSensitivity=0.3, ZoomSensitivity=1.2, MinDistance=5, MaxDistance=2000

State: _yaw=25°, _pitch=12°, _distance=40  (initial: shows full Starship at 40m)

_Input: right-mouse drag → orbit; scroll → zoom
_Process: spherical to Cartesian → Camera3D child position + LookAt(Zero)

PLANNED (Semana 6): 3 modes
  Mode.Chase  — current orbit behavior (default)
  Mode.Pad    — fixed pad cameras during countdown (3 preset angles)
  Mode.IVA    — inside cockpit; no orbit controls
```

### `scripts/HUDController.cs`
```
partial class HUDController : Node
[Export] NodePath for 9 Label nodes

Displays each frame:
  AltitudeLabel:  FormatDistance(altitude above refBody)
  SpeedLabel:     orbital speed (vessel.Velocity - refBody.Velocity).Magnitude
  ApoapsisLabel:  from OrbitalElements.FromStateVector live
  PeriapsisLabel: same
  ThrottleLabel:  "THR N%  [FIRING|FLAME-OUT|OFF]"  orange when firing
  FuelLabel:      TotalLiquidFuel + TotalOxidizer kg
  MassLabel:      TotalMass / 1000 tonnes
  TimeLabel:      mission time formatted T+HH:MM:SS
  WarpLabel:      "Real Time" or "× N" (cyan when warping)

Keyboard input (_Process, held keys):
  W/S → pitch ±1, A/D → yaw ±1, Q/E → roll ±1 → vessel.PitchYawRoll each frame

Keyboard input (_UnhandledInput, events):
  Z/X → throttle ±5%, Space → staging, T → toggle SAS

Hint label (bottom): "[Z/X] throttle  [W/S] pitch  [A/D] yaw  [Q/E] roll  [T] SAS  ..."
```

### `scripts/TimeWarpController.cs`
```
Warp ladder: [1, 5, 10, 50, 100, 1000, 10000, 100000]
Signal: WarpChanged(double newRate)
Input: Key.Period → WarpUp, Key.Comma → WarpDown, Key.Backspace → ResetToRealTime
CanWarpUp: false when throttle>0.01 OR atmospheric density>0.01 kg/m³
```

### `scripts/SaveSystem.cs`
```
Save dir: ~/.local/share/Exosphere/saves/
Methods: SaveGame(slotName="quicksave"), LoadGame(slotName), ListSaveSlots()
Per-vessel: id, name, position (x,y,z double), velocity, orientation, is_on_rails, reference_body_id
Note: bodies always recomputed from data/; CurrentTime not saved (planned)
```

---

## Architecture Deep Dive

### The Scale Problem and Floating Origin

Solar system = ~10¹³ m. Float32 loses precision at ~1.6e7 m.  
**Solution**: All simulation in `double`. Rendering in `float32` via Floating Origin.

```
Each render frame:
  renderOrigin (Vector3d, double) = activeVessel.Position

  vessel nodes:
    node.Position (float32) = (vessel.Position - renderOrigin).ToFloat()
    -- Result: always near (0,0,0); float32 precision fine

  planet nodes:
    relativePos = body.Position - renderOrigin
    node.Position = (float32)(relativePos * 1/10000)
    -- Earth at 250 km altitude: render pos ≈ (0, 0, -637) Godot units
    -- Planets visible at any interplanetary distance
```

### Time Warp Physics Modes

```
TimeScale ≤ 4:    Full RK4 at 50 Hz sub-steps (MaxPhysicsStep = 0.02 s)
TimeScale ≤ 1000: Active vessel RK4; all others Keplerian
TimeScale > 1000: Everything Keplerian (zero CPU, no limit)

Switching physics→rails: OrbitalElements.FromStateVector(pos, vel)
Switching rails→physics: RK4 starts from current (pos, vel) — no discontinuity
```

### Physics Pipeline (per tick in Universe.TickPhysics)

```
1. KeplerPropagator.PropagateAllBodies(bodies, t + dt)

2. For each vessel NOT on rails:

   a. vessel.Tick(dt, refBody)
      └── ConsumePropellant cross-feed from all tanks
      └── Apply PitchYawRoll → AngularVelocity (ControlAuthority 0.6 rad/s²/unit)
      └── SAS: damp AngVel to zero if no PitchYawRoll input
      └── Integrate AngVel → Orientation

   b. RK4Integrator.StepPosVel(pos, vel, t, dt, accelFn)
      accelFn = gravity(allBodies) + thrust/mass + drag/mass

   c. StressSolver.ComputeLoads(parts, nonGravAccel, orientation)

   d. If atmosphere:
      heatFlux = ThermalModel.ComputeHeatFlux(rho, airspeed)
      StressSolver.ApplyThermalLoads(parts, heatFlux, dt)

   e. Surface impact: altitude < 0 → clamp to surface, zero velocity
```

### SOI Hierarchy (GetDominantBody)

Picks body with **smallest SOI** that contains the position:
- Moon SOI = 66,200 km < Earth SOI = 924,000 km → Moon wins inside its SOI ✓
- Earth SOI < Sun SOI → Earth wins inside its SOI ✓
- Falls back to most massive body (Sun) when no SOI contains position ✓

---

## Data Formats

### Celestial Body JSON (`data/bodies/<id>.json`)

```jsonc
{
  "id": "earth", "name": "Earth",
  "mass": 5.972e24,       // kg
  "radius": 6371000,      // m mean radius
  "gm": 3.986004418e14,   // m³/s² (more accurate than G*M)
  "soi": 924000000,       // m sphere of influence
  "rotational_period": 86164,  // s sidereal; negative = retrograde
  "axial_tilt": 23.44,         // degrees
  "has_atmosphere": true,
  "atmosphere": {
    "scale_height": 8500,
    "sea_level_density": 1.225,
    "sea_level_pressure": 101325,
    "max_altitude": 140000,
    "layers": [
      {"alt_min":0,"alt_max":11000,"temp_base":288.15,"lapse_rate":-0.0065},
      {"alt_min":11000,"alt_max":20000,"temp_base":216.65,"lapse_rate":0.0},
      {"alt_min":20000,"alt_max":32000,"temp_base":216.65,"lapse_rate":0.001}
    ]
  },
  "orbital_elements": {
    "semi_major_axis": 1.496e11,
    "eccentricity": 0.0167,
    "inclination": 0.0,
    "longitude_of_node": -11.26064,
    "argument_of_periapsis": 114.20783,
    "mean_anomaly_at_epoch": 358.617,
    "epoch": 0.0,
    "reference_body": "sun"
  }
}
```

### Part JSON (`data/parts/<id>.json`)

```jsonc
{
  "id": "engine_liquid_sl",
  "name": "Pax-1D Sea Level Engine",
  "category": "engine",     // command|engine|fuel_tank|structure|electrical|landing|decoupler|fairing|rcs
  "mass_dry": 630,           // kg
  "thrust_vac": 845000,      // N
  "thrust_sl":  756000,      // N
  "isp_vac": 311,            // s
  "isp_sl":  282,            // s
  "gimbal_range": 5.0,       // degrees
  "fuel_type": "LiquidFuel+Oxidizer",
  "fuel_capacity_lf": 0,     // engines have no own tank; cross-feed from graph
  "fuel_capacity_ox": 0,
  "heat_tolerance": 2000,    // K
  "attachment_nodes": [
    {"id":"top","position":[0,0.5,0],"size":1,"type":"stack"},
    {"id":"engine_bell","position":[0,-1.0,0],"size":0,"type":"engine_bell"}
  ]
}
```

### New Parts Required (Semana 6)

**`data/parts/super_heavy_booster.json`**
```jsonc
{
  "id": "super_heavy_booster",
  "name": "Super Heavy Booster",
  "category": "engine",      // treated as combined engine+tank part
  "mass_dry": 200000,        // kg (200 t structure)
  "thrust_vac": 74400000,    // N  (33 × ~2255 kN)
  "thrust_sl":  66000000,    // N  (33 × ~2000 kN)
  "isp_vac": 350,            // s  (Raptor vac averaged)
  "isp_sl":  330,            // s  (Raptor SL averaged)
  "gimbal_range": 5.0,
  "fuel_type": "LiquidFuel+Oxidizer",
  "fuel_capacity_lf": 900000,   // kg CH4 (methane stored as LF)
  "fuel_capacity_ox": 2400000,  // kg LOX
  "heat_tolerance": 2000,
  "attachment_nodes": [
    {"id":"top","position":[0,35.5,0],"size":3,"type":"stack"},
    {"id":"engine_bell","position":[0,-35.5,0],"size":3,"type":"engine_bell"}
  ]
}
// Notes:
// TWR at liftoff: 74.4 MN / (1700 t × 9.8 m/s²) ≈ 1.45 (real: ~1.5) ✓
// Burn time: Tsiolkovsky → ln(1700/200) × 350×9.8 / 74.4e6 ≈ 165 s ✓ (real: ~165 s)
```

**`data/parts/starship_ship.json`**
```jsonc
{
  "id": "starship_ship",
  "name": "Starship",
  "category": "command",    // command part; renderer uses StarshipRenderer
  "mass_dry": 100000,       // kg (100 t structure + heat shield)
  "max_crew": 100,
  "heat_tolerance": 1800,   // K (heat shield protects)
  "attachment_nodes": [
    {"id":"bottom","position":[0,-25,0],"size":3,"type":"stack"}
  ]
}
// Separate fuel tank part for Starship propellant:
```

**`data/parts/starship_tank.json`**
```jsonc
{
  "id": "starship_tank",
  "name": "Starship Propellant Tank",
  "category": "fuel_tank",
  "mass_dry": 0,
  "fuel_capacity_lf": 330000,   // kg CH4
  "fuel_capacity_ox": 870000,   // kg LOX
  "attachment_nodes": [
    {"id":"top","position":[0,12,0],"size":3,"type":"stack"},
    {"id":"bottom","position":[0,-12,0],"size":3,"type":"stack"}
  ]
}
```

**`data/parts/starship_engines.json`**
```jsonc
{
  "id": "starship_engines",
  "name": "Starship Engine Section (6 Raptors)",
  "category": "engine",
  "mass_dry": 15000,          // kg (6 engines)
  "thrust_vac": 13500000,     // N  (3×2200 vac + 3×2300 SL = ~13.5 MN)
  "thrust_sl":  11400000,     // N
  "isp_vac": 380,             // s  (Raptor Vac)
  "isp_sl":  330,             // s
  "gimbal_range": 15.0,       // wider gimbal for control authority
  "fuel_type": "LiquidFuel+Oxidizer",
  "fuel_capacity_lf": 0,
  "fuel_capacity_ox": 0,
  "heat_tolerance": 2000,
  "attachment_nodes": [
    {"id":"top","position":[0,2.0,0],"size":3,"type":"stack"},
    {"id":"engine_bell","position":[0,-3.0,0],"size":3,"type":"engine_bell"}
  ]
}
// ΔV (Starship alone, no SH): Tsiolkovsky
// m0=1200t m1=100t isp=380: ΔV = 380×9.8×ln(1200/100) ≈ 9600 m/s
// LEO circularization needs ~150 m/s; TMI needs ~900 m/s; plenty of margin ✓
```

---

## Mission Architecture

### Phase State Machine (`MissionManager.cs`)

```
Enum MissionPhase:
  MENU, PRE_LAUNCH, IGNITION, ASCENT_SH, MAX_Q,
  MECO, SEPARATION, ASCENT_SHIP, ORBIT,
  MANEUVER, CRUISE, APPROACH, EDL, LANDED

MissionManager [GlobalClass] : Node
  CurrentPhase (MissionPhase)
  Signal: PhaseChanged(MissionPhase newPhase)

Phase transitions:
  MENU → PRE_LAUNCH     player presses "Launch Mission"
  PRE_LAUNCH → IGNITION T-0 reached in countdown
  IGNITION → ASCENT_SH  engines nominal + hold-down released (throttle > 95%)
  ASCENT_SH → MAX_Q     auto event when q peaks (detected in Universe.Tick wrapper)
  MAX_Q → MECO          auto event when SH fuel depleted or altitude > 65 km
  MECO → SEPARATION     auto event 3 s after MECO
  SEPARATION → ASCENT_SHIP  auto event when Starship engines ignite
  ASCENT_SHIP → ORBIT   player triggers OR auto when Ap > 200 km + Pe > 180 km
  ORBIT → MANEUVER      player opens maneuver planner and executes burn
  MANEUVER → CRUISE     after burn complete; warp enabled
  CRUISE → APPROACH     proximity to target body < 1000 km
  APPROACH → EDL        altitude < 125 km and speed > 3000 m/s
  EDL → LANDED          vertical speed < 2 m/s AND altitude < 5 m
```

### Auto-Events Per Phase

```
MAX_Q:
  Trigger: dq/dt changes sign from + to - where q = 0.5 * rho * v²
  Visual: MaxQRingController.Spawn() — torus condensation ring
  HUD: flash "MAX-Q" label in yellow

MECO:
  Trigger: SH fuel == 0 (flame-out) OR mission_manager detects altitude > 65 km
  Visual: SH plumes disappear
  Audio: engine sound fades out

SEPARATION:
  Trigger: 3 s after MECO; MissionManager calls SimulationBridge.TriggerStaging()
  Physics: SH becomes independent Vessel with −5 m/s axial separation velocity
  SH autopilot: flip 180°, ignite 3 central Raptors, boostback burn, reentry, catch

STARSHIP IGNITION (during SEPARATION):
  Trigger: same frame as separation + 1 s delay
  Visual: Starship plumes appear (vacuum mode; no shock diamonds)
```

### Super Heavy Return (autonomous)

After separation, the Super Heavy `Vessel` runs a simplified autopilot:
1. Flip: SAS targets 180° from velocity vector
2. Boostback burn: 3 Raptors, ~20 s
3. Coast + atmospheric reentry
4. Grid fin deployment (visual only in Semana 7)
5. Catch: at altitude 100 m, zero velocity → "caught" event; SH removed from simulation

The player can switch camera to SH at any time via `CameraController.SwitchTarget(shVessel)`.

---

## Visual Design Specification

### Starship (correct proportions — to implement in Semana 7)

Real Starship proportions (upper stage alone):
- Total height: 50 m, diameter: 9 m → ratio 5.5:1
- Nosecone: ROUNDED/BULBOUS (NOT a sharp cone) — think bullet shape
  - Bottom radius same as body (4.5 m real, 1.15 in-game)
  - Modeled as: hemispherical SphereMesh + short CylinderMesh transition
  - Height: ~9 m (18% of total)
- Heat shield tiles: BLACK, cover ~55% of body from bottom, PLUS all of aft flap surfaces
- Steel sections: silver-white metallic (only nose tip and upper body strip)
- Forward canards: small, 2 only (L+R), positioned at very top of body section
- Aft flaps: large (wider than body when open), 2 only (L+R), hinge at body equator

```
In-game Starship model (StarshipRenderer.cs — Semana 7):

Y-axis orientation: +Y = nose (up), origin = CoM (center of body)

  y = +22   nose tip (SphereMesh top)
  y = +19   nose sphere center
  y = +16   nose base / body transition
  y = +8    body top (steel)
  y = 0     CoM / origin
  y = -8    body bottom / skirt top
  y = -10   skirt bottom (slightly narrower)
  y = -11   engine bay bottom
  y = -13   vacuum Raptor nozzle exits

Body = BodyUpper + BodyLower split at y=0:
  BodyUpper: steel r=1.15 h=16 y=+8 → steelMat
  BodyLower: tile  r=1.15 h=16 y=-8 → tileMat

Nosecone:
  NoseSphere:    SphereMesh r=3.0 y=+19   steelMat  (represents rounded nose)
  NoseCylinder:  CylinderMesh r=1.15 h=6 y=+13     steelMat (transition)

Canards (forward, small):
  CanardL/R:  BoxMesh 0.15 × 1.4 × 2.8  y=+14 x=±1.25  darkSteelMat
  NOTE: these are small and sit at the very top of the body

Aft flaps (large):
  FlapL/R:  BoxMesh 0.14 × 8.0 × 5.5  y=-4 x=±1.25  tileMat
  FlapRootL/R: BoxMesh 0.20 × 8.0 × 1.5  y=-4 x=±1.18  tileMat

Engine skirt:
  Skirt: CylinderMesh top=1.15 bot=1.05 h=2.5  y=-9.25  darkSteelMat

Engines (6 Raptors):
  VacRing r=0.38, 3 engines at 120°: bell top=0.22 bot=0.48 h=2.5  y=-11
  SLRing  r=0.75, 3 engines at 60° offset: bell top=0.24 bot=0.36 h=1.6  y=-10.5

Materials (StandardMaterial3D):
  steelMat:     Albedo(0.88,0.88,0.90), Metallic=0.95, Roughness=0.12
  tileMat:      Albedo(0.06,0.06,0.08), Metallic=0.02, Roughness=0.96
  darkSteelMat: Albedo(0.48,0.48,0.50), Metallic=0.90, Roughness=0.28
  engineMat:    Albedo(0.15,0.15,0.17), Metallic=0.85, Roughness=0.35
```

### Super Heavy (SuperHeavyRenderer.cs — Semana 7)

```
Height: 71 m (game units), diameter: 9 m (radius 1.15)
Origin at CoM (middle of body) → y range: -35.5 to +35.5

Body:
  Main cylinder: r=1.15 h=71  steelMat  y=0  (one piece, steel throughout)
  
Grid fins (4, at top, 90° spacing — Semana 7):
  Each: BoxMesh 0.1 × 4.0 × 4.0  y=+34  at radius 1.25

Engine bay (bottom):
  Skirt: CylinderMesh top=1.15 bot=1.3 h=3  y=-34.5  (flare at base)

33 Raptors in 3 rings:
  Ring 1 (center, 3 fixed):   bell top=0.22 bot=0.46 h=2.3  ring r=0.42  y=-37
  Ring 2 (middle, 10 engines): bell top=0.22 bot=0.42 h=2.0  ring r=0.85  y=-36.5
  Ring 3 (outer, 20 engines):  bell top=0.22 bot=0.38 h=1.8  ring r=1.1   y=-36

Materials: same as Starship (steelMat/engineMat)
```

### Starbase Launch Environment (LaunchPadController.cs)

```
Ground plane:
  PlaneMap: infinite flat mesh at y=0; sandy/dry Texas texture (tan color)
  Ocean: flat plane at y=0 extending ~5 km in -Z direction (blue-gray)
  Horizon: atmospheric haze at 3-8 km distance (fade to sky color)

Launch mount (under the rocket):
  Mount base: BoxMesh 12×4×12  gray concrete  y=-2
  Mount legs: 4× CylinderMesh r=0.4 h=6  gray  at corners

Mechazilla tower (orbital launch mount):
  Left tower:  CylinderMesh r=1.5 h=145  dark gray  x=-6 y=+72
  Right tower: CylinderMesh r=1.5 h=145  dark gray  x=+6 y=+72
  Left arm:  BoxMesh 8×2×2  y=+120 x=-2  (chopstick arm, closed over SH)
  Right arm: BoxMesh 8×2×2  y=+120 x=+2

Tank farm (background):
  4× CylinderMesh r=3 h=20 white  at various offsets (LOX/CH4 tanks)

Sky is handled by SkyController; clouds at 2-8 km altitude layer
```

---

## VFX System Specification

### Dynamic Sky (`SkyController.cs` + `sky_atmosphere.gdshader`)

```
SkyController reads SimulationBridge.ActiveVessel altitude each frame.
Updates WorldEnvironment sky material uniforms:

altitude_km   sky_top_color           sky_horizon_color     star_intensity
0             Color(0.02,0.06,0.18)   Color(0.35,0.60,0.90)  0.0
5             Color(0.01,0.04,0.14)   Color(0.25,0.50,0.82)  0.0
12            Color(0.00,0.02,0.10)   Color(0.12,0.30,0.68)  0.0
30            Color(0.00,0.01,0.06)   Color(0.04,0.10,0.35)  0.2
60            Color(0.00,0.00,0.02)   Color(0.01,0.03,0.12)  0.6
80+           Color(0,0,0)            Color(0,0,0)           1.0

Clouds: visible only 2-8 km altitude range
  CloudLayer: flat SphereMesh at radius earth+5km, semi-transparent white
  Opacity fades in at 2 km, fades out at 8 km

Implementation: ProceduralSkyMaterial sky_top_color + sky_horizon_color
updated in C# each frame. No custom shader needed for first iteration.
```

### Engine Plume System (`PlumeSystem.cs`)

```
Two plume modes based on atmospheric density:
  SL mode (rho > 0.01 kg/m³):
    GPUParticles3D with short lifetime, wide spread
    Color gradient: white core → orange → transparent
    Shock diamonds: thin white toroidal bands at 0.5, 1.0, 1.5 diameters down
    LOX cloud: white sphere puff at engine bell during ignition (1 s)

  Vac mode (rho < 0.001 kg/m³):
    GPUParticles3D with long lifetime, narrow then expanding cone
    Color: ice blue core → orange outer → transparent
    No shock diamonds
    Much longer (40-60 game units vs 5-8 SL)

  Transition: linear interpolation of parameters between 0.001 and 0.01 kg/m³

PlumeSystem nodes (per engine):
  One GPUParticles3D child with process_material (ParticleProcessMaterial)
  Parameters updated in _Process from altitude + throttle
  Emission rate = throttle × base_rate
  Scale = (1.0 + throttle × 0.5) for bell-exit area

For Super Heavy (33 engines): plumes merged into 3 ring-emitters for performance
```

### Max-Q Condensation Ring (`MaxQRingController.cs`)

```
Trigger: Universe.TickPhysics wrapper detects when dq/dt < 0 (first time)
  i.e., when q = 0.5 * rho * v² stops increasing

Spawn: MeshInstance3D toroidal mesh (TorusMesh in Godot) around Starship
  Inner radius = 1.2 (body radius), outer radius = 0.4 (ring thickness)
  Material: semi-transparent white, EmissionEnabled, fade in + out over 3 s

Animation:
  t=0.0 to 0.5 s: opacity 0 → 0.7 (fast fade in)
  t=0.5 to 2.5 s: opacity 0.7, drift slightly downward (−0.1 units/s)
  t=2.5 to 3.5 s: opacity 0.7 → 0 (slow fade out)
  Then QueueFree()
```

### Reentry Heat (improved from existing shader)

```
Existing: temperature-driven orange emission on hull mesh
Semana 8 improvement:
  - Separate plasma mesh slightly larger than hull, additive blend, animated
  - Intensity driven by: density × velocity³ (DKR proxy)
  - Color: high speed = blue-white; medium = orange; low = red glow
  - Dynamic pressure threshold: only visible when q > 5000 Pa
```

---

## Audio System Specification

### `AudioManager.cs`

```
[GlobalClass] partial class AudioManager : Node
Static: Instance

Audio buses:
  Master → SFX → Engine3D (3D positional)
            SFX → UI      (2D, always full volume)
            SFX → Ambient (2D, looping)
  Master → Music

Audio players:
  _engineSLPlayer:   AudioStreamPlayer (looping; rho-based volume)
  _engineVacPlayer:  AudioStreamPlayer (looping; throttle-based volume)
  _ambientPlayer:    AudioStreamPlayer (pad ambient, crossfade to silence in space)
  _voicePlayer:      AudioStreamPlayer (one-shot: countdown voice)
  _eventPlayer:      AudioStreamPlayer (one-shot: staging, boom, etc.)

_Process(delta):
  float rho = GetAtmosphericDensity(vessel.Position)
  float thr = vessel.Throttle
  float speed = vessel.Velocity.Magnitude

  // Engine crossfade SL ↔ Vac based on density
  float slMix = Mathf.Clamp(rho / 0.01f, 0f, 1f)
  _engineSLPlayer.VolumeDb  = LinearToDb(thr * slMix * 0.8f)
  _engineVacPlayer.VolumeDb = LinearToDb(thr * (1f - slMix) * 0.8f)

  // Ambient: full at ground, silent above 80 km
  float ambFade = Mathf.Clamp(1f - GetAltitudeKm()/80f, 0f, 1f)
  _ambientPlayer.VolumeDb = LinearToDb(ambFade)

  // Pitch shift on engine: higher speed = slightly higher pitch
  _engineSLPlayer.PitchScale = 1.0f + thr * 0.15f

Audio files (to create or source — all .ogg):
  engine_raptor_sl.ogg:   4-8 s loop; deep roar + crackling high freq
  engine_raptor_vac.ogg:  4-8 s loop; high-pitched whistle; quieter
  countdown_voice.ogg:    "10... 9... 8... 7... 6... 5... 4... 3... 2... 1... ignition!"
  pad_ambient.ogg:        8-16 s loop; wind, mechanical hiss, distant seagulls
  stage_separation.ogg:   0.5 s clunk + short burst + silence
  sonic_boom.ogg:         1.5 s boom (played when player camera is near and Mach > 1)
  reentry_plasma.ogg:     4-8 s loop; deep rumbling; played during EDL

For synthesis (if .ogg files not available):
  Use AudioStreamGenerator per player; fill buffer with noise filtered to band
  Engine SL: band-pass 80-800 Hz noise + random amplitude modulation
  Engine Vac: high-pass 1200-3000 Hz + slow AM
```

---

## Implementation Status

### Sessions 1–5 (Completed)

| Component | Status | Session |
|---|---|---|
| Vector3d, Quaterniond, MathUtils | ✅ Done | 1 |
| OrbitalElements (state vector conversion) | ✅ Done | 1 |
| CelestialBody (JSON loading, gravity, surface vel) | ✅ Done | 1 |
| AtmosphereModel (ISA layers) | ✅ Done | 2 |
| RK4Integrator, KeplerPropagator | ✅ Done | 1 |
| Universe (3 warp modes, SOI detection) | ✅ Done | 1 |
| GetDominantBody bug fix (smallest SOI wins) | ✅ Done | 3 |
| Part, PartDefinition, PartGraph, Joint | ✅ Done | 1 |
| PartGraph cross-feed bug fix | ✅ Done | 5 |
| ThermalModel, AerodynamicsModel, StressSolver | ✅ Done | 1 |
| 8 celestial bodies (real NASA data) | ✅ Done | 1 |
| 15 rocket parts | ✅ Done | 1 |
| SimulationBridge (tick, spawn, planet renderer) | ✅ Done | 2-3 |
| FloatingOrigin (vessel + planet scale 1/10000) | ✅ Done | 3 |
| TimeWarpController (8-step ladder, safety) | ✅ Done | 2 |
| NavBallController | ✅ Done | 2 |
| HUDController (9 readouts + WASD rotation) | ✅ Done | 4-5 |
| CameraController (orbit, right-drag, scroll) | ✅ Done | 3 |
| VesselRenderer (Starship procedural + plumes) | ✅ Done | 4-5 |
| Vessel.Tick rotation (PitchYawRoll → AngVel) | ✅ Done | 5 |
| SAS (damp only when no input) | ✅ Done | 5 |
| Atmosphere glow on Earth | ✅ Done | 5 |
| Flight.tscn scene wiring | ✅ Done | 3 |
| SaveSystem | ✅ Done | 1 |
| MissionManager (FSM, countdown, phase signals) | ✅ Done | 6 |
| LaunchPadController (OLM + Mechazilla tower) | ✅ Done | 6 |
| Ground spawn + IsGroundHeld hold-down | ✅ Done | 6 |
| starship_command/tank/engines/super_heavy_booster JSON | ✅ Done | 6 |
| VesselRenderer full stack (33 SH Raptors + 6 Ship) | ✅ Done | 6 |
| CameraController Pad/Chase modes + C key | ✅ Done | 6 |
| HUDController phase label + TWR + countdown overlay | ✅ Done | 6 |
| **StarshipRenderer (correct proportions)** | ⏳ Semana 7 | — |
| **SuperHeavyRenderer (33 Raptors separate)** | ⏳ Semana 7 | — |
| **SkyController (altitude-based sky)** | ⏳ Semana 8 | — |
| **PlumeSystem (GPU particles)** | ⏳ Semana 8 | — |
| **MaxQRingController** | ⏳ Semana 8 | — |
| **AudioManager** | ⏳ Semana 9 | — |
| **MapViewController + ManeuverPlanner** | ⏳ Semana 10 | — |
| **EDL sequence + Mars surface** | ⏳ Semana 11 | — |

---

## Development Roadmap — Semanas 6–12

Each semana is a complete, testable deliverable. Later Claude sessions should read this section and the "Known Issues" section before starting.

---

### Semana 6 — Launch from Earth Surface ✅ COMPLETED (2026-06-11)

**Goal**: Press F5 → see the full stack on the Starbase launchpad → press Launch → rocket lifts off, burns all the way to orbit.

**What was implemented:**

| File | Change |
|---|---|
| `data/parts/starship_command.json` | 80 t command/nosecone, 100 crew |
| `data/parts/starship_tank.json` | 330 t LCH4 + 870 t LOX = 1200 t propellant |
| `data/parts/starship_engines.json` | 13.5 MN vac / 11 MN SL, ISP 380/327 s |
| `data/parts/super_heavy_booster.json` | 200 t dry, 3300 t prop, 74.4 MN / 33 Raptors |
| `scripts/MissionManager.cs` | FSM PRE\_LAUNCH→ORBIT, T-10 countdown, auto Max-Q + MECO |
| `scripts/LaunchPadController.cs` | OLM base, pedestal, Mechazilla tower + arms, deluge ring |
| `ExosphereSimulation/Vessel.cs` | `IsGroundHeld`, `GroundNormal`, `GroundOffset`, `ReleaseGroundHold()` |
| `ExosphereSimulation/Universe.cs` | Skip RK4 when `IsGroundHeld`; position vessel on Earth surface |
| `scripts/SimulationBridge.cs` | `SpawnStarshipStack()`, launchpad position per frame, `ReleaseGroundHold()` |
| `scripts/VesselRenderer.cs` | `BuildFullStack()`: 33 SH Raptor bells + 6 Starship bells, stage-aware plumes |
| `scripts/CameraController.cs` | `CameraMode` enum (Pad/Chase), `C` cycles 3 pad presets, auto Chase >500 m |
| `scripts/HUDController.cs` | Phase label, TWR gauge, T-countdown overlay, `L` key launches |

**Stack assembly (top → bottom):**
```
starship_command  (command)   → joint bottom→top →
starship_tank     (fuel_tank) → joint bottom→top →
starship_engines  (engine)    → joint bottom→top →
decoupler_medium  (decoupler) → joint bottom→top →
super_heavy_booster (engine)
```
`super_heavy_booster` has `fuel_capacity_lf/ox` set so PartGraph cross-feed draws from it during SH burn.

**Physics at liftoff (verified):**
- Total mass: 80t + 5t + 15t + 0.06t + 200t + 330t+870t + 900t+2400t = **4800 t**
- SH thrust SL: 69 MN → TWR = 69e6 / (4800e3 × 9.81) ≈ **1.47** ✓ (real ≈ 1.5)
- Starship alone: m0=1300t m1=100t ISP=380 → ΔV = 380×9.81×ln(13) ≈ **9560 m/s** ✓

**Ground hold mechanism:**
- `vessel.IsGroundHeld = true` at spawn; Universe skips RK4, locks vessel to earth surface
- `vessel.Position = earth.Position + GroundNormal × (earth.Radius + GroundOffset)`
- `vessel.Velocity = earth.Velocity + earth.GetSurfaceVelocity(vessel.Position)` (Earth rotation)
- At T-0: `bridge.ReleaseGroundHold()` → physics resumes, rocket ascends

**Spawn position:** Earth `+Y` (north pole direction for simplicity, no equatorial offset).

**Test criteria:**
- [x] Game starts showing rocket on pad (not LEO)
- [x] Countdown from T-10 visible on screen (red overlay)
- [x] At T-3 SH engines ignite (plumes visible)
- [x] At T-0 rocket lifts off (`IsGroundHeld` released)
- [x] TWR label shows > 1 in green
- [x] Mission phase transitions: COUNTDOWN → IGNITION → LIFTOFF → ASCENT\_SH
- [x] Max-Q auto-detected and phase changes to MAX\_Q
- [x] `Space` stages (decoupler fires, SH separates)
- [x] After separation: Starship engine plumes replace SH plumes
- [x] Camera auto-switches to Chase above 500 m; `C` cycles pad presets

---

### Semana 7 — Visual Redesign (Starship + Super Heavy)

**Goal**: The two vehicles look recognizably like SpaceX hardware. Reference: images #11 and #12 in the project session.

**Files to CREATE:**
- `scripts/StarshipRenderer.cs` — replaces Starship section of VesselRenderer
- `scripts/SuperHeavyRenderer.cs` — new; builds SH mesh

**Files to MODIFY:**
- `scripts/VesselRenderer.cs` — delegate to StarshipRenderer / SuperHeavyRenderer

**StarshipRenderer geometry (Y-up, nose at +Y, origin at CoM):**
See "Visual Design Specification → Starship" section above for exact vertex positions.
Key changes from current: rounded nosecone (SphereMesh hemisphere), correct tile pattern,
smaller canards positioned near top, larger aft flaps on sides.

**SuperHeavyRenderer geometry:**
See "Visual Design Specification → Super Heavy" section above.
33 engine bells in 3 rings.

**Separation visual:**
When MissionManager enters SEPARATION phase:
- SH renderer position = SH vessel render position (offset below Starship)
- Starship renderer position = Starship vessel render position (offset above SH)
- Both tracked separately by FloatingOrigin

**Test criteria for Semana 7 completion:**
- [ ] Starship has rounded nosecone (not sharp cone)
- [ ] Correct tile vs steel color areas
- [ ] 2 small canards at top, 2 large flaps at bottom
- [ ] Super Heavy visible as separate tall booster below
- [ ] 33 engine bells visible on Super Heavy
- [ ] Both vehicles separate cleanly at MECO+3s

---

### Semana 8 — VFX and Atmosphere

**Goal**: The game looks and feels like watching a SpaceX webcast.

**Files to CREATE:**
- `scripts/SkyController.cs` — reads altitude, updates WorldEnvironment colors
- `scripts/PlumeSystem.cs` — GPUParticles3D manager (SL vs vac modes)
- `scripts/MaxQRingController.cs` — condensation ring spawner
- `assets/shaders/engine_plume.gdshader` — particle shader with shock diamond option

**Files to MODIFY:**
- `scripts/VesselRenderer.cs` — remove old cone plumes; delegate to PlumeSystem
- `scenes/flight/Flight.tscn` — WorldEnvironment sky material updated
- `scripts/SimulationBridge.cs` — add Max-Q detection; fire event to MaxQRingController

**Sky transition values:** See Audio System Specification → SkyController above.

**GPU particle plume setup:**
```
For each Raptor engine:
  GPUParticles3D below nozzle exit
  ProcessMaterial ParticleProcessMaterial:
    direction = (0,-1,0)
    spread = 5° (SL) or 2° (Vac)
    initial_velocity = 1200 m/s (SL) or 3000 m/s (Vac)
    scale_random = 0.3
    color_ramp = [white → orange → transparent]
    lifetime = 0.8 s (SL) or 3.0 s (Vac)
    amount = 200 per engine (SL); 80 per engine (Vac)
  
  SH: 3 merged emitters (inner/mid/outer rings) for performance
  Starship: 6 individual emitters
```

**Test criteria for Semana 8 completion:**
- [ ] Sky is blue at ground, black at 80+ km, gradient in between
- [ ] Engine plumes look different in atmosphere vs vacuum
- [ ] Max-Q condensation ring appears around Mach 1
- [ ] Heat glow visible on Starship hull during reentry sim (raise heating test)

---

### Semana 9 — Audio and Mission UI

**Goal**: The countdown, launch sequence, and flight have full audio. Mission phases shown clearly.

**Files to CREATE:**
- `scripts/AudioManager.cs` — see Audio System Specification above
- `assets/audio/` — all .ogg files listed in spec (synthesized if no real files available)
- HUD redesign: replace loose labels with organized panels (Left = vessel data, Right = orbital)

**Files to MODIFY:**
- `scripts/HUDController.cs` — mission phase indicator; navball visual; TWR bar
- `scripts/CountdownController.cs` — add voice trigger calls to AudioManager
- `scripts/MissionManager.cs` — call AudioManager on phase changes

**Synthesis fallback (if no .ogg files):**
```csharp
// AudioStreamGenerator for engine noise:
var gen = new AudioStreamGenerator();
gen.MixRate = 44100;
gen.BufferLength = 0.1f;
var player = new AudioStreamPlayer();
player.Stream = gen;
// In _Process: fill playback buffer with noise samples
var playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
// Engine SL: fill with bandpass-filtered noise 80-800 Hz
// Engine Vac: fill with highpass noise 1200-3000 Hz
```

**Test criteria for Semana 9 completion:**
- [ ] Engine sound plays during thrust (louder in atmosphere, quieter in vacuum)
- [ ] Countdown voice audible at T-10
- [ ] Pad ambient sound plays before launch
- [ ] Silence above 80 km
- [ ] Mission phase clearly visible in HUD during all transitions

---

### Semana 10 — Orbital Maneuver Planner

**Goal**: Player can plan and execute Trans-Mars Injection (or any interplanetary burn) from the map view.

**Files to CREATE:**
- `scripts/MapViewController.cs` — SubViewport with orthographic solar system view
- `scripts/ManeuverPlanner.cs` — maneuver node: prograde/retrograde ΔV, burn time calc
- `scenes/flight/MapView.tscn` — SubViewport scene for map

**Map view:**
```
SubViewport 400×400 px, bottom-right corner of screen (toggle with M key)
Camera: OrthographicCamera3D looking down (+Y), scale covers current SOI

Renders:
  - Planet circles (simplified, with SOI rings)
  - Active vessel orbit ellipse (line renderer: sample 200 points from OrbitalElements)
  - Maneuver node position (draggable triangle)
  - Projected orbit after maneuver (dashed line)
  - Transfer orbit to target (if target selected)

ManeuverPlanner:
  Maneuver = { position (true anomaly), prograde_dv, normal_dv, radial_dv }
  BurnTime = ΔV / (thrust/mass) → how long to hold throttle
  Required ΔV displayed: TMI to Mars ≈ 900 m/s, LEO circularization ≈ 50-150 m/s

Autopilot execution:
  Player clicks "Execute" → AutopilotController.cs executes the burn:
    - Orient vessel to burn direction (SAS prograde/retrograde/custom)
    - Start throttle at T-burnTime/2 before maneuver node time
    - Cut throttle when ΔV accumulated
```

**Test criteria for Semana 10 completion:**
- [ ] Map view visible with M key
- [ ] Vessel orbit rendered as ellipse on map
- [ ] Player can add a maneuver node
- [ ] ΔV and burn time displayed
- [ ] Autopilot executes burn (even roughly)
- [ ] Projected orbit updates as node is moved

---

### Semana 11 — Mars EDL and Planetary Surfaces

**Goal**: Land on Mars. See red terrain. Experience real EDL.

**Files to CREATE:**
- `scripts/EDLController.cs` — manages EDL sequence events
- `scripts/SurfaceTerrainController.cs` — procedural terrain when altitude < 50 km
- `assets/shaders/mars_atmosphere.gdshader` — orange tint, thinner scattering

**Files to MODIFY:**
- `ExosphereSimulation/CelestialBody.cs` — surface rendering trigger (altitude < 50 km)
- `scripts/SkyController.cs` — add Mars sky (orange/pink at surface)
- `scripts/SimulationBridge.cs` — second planet surface environment

**Mars EDL sequence:**
```
MissionManager.APPROACH → EDL triggers when altitude < 125 km AND speed > 3000 m/s

EDL events (automatic + player-controlled):
  1. Atmospheric entry: plasma begins (altitude 125 km, speed 6000 m/s at Mars)
  2. Peak heating: ~60 km, 4000 m/s (heat shield visible glow)
  3. Flap deployment: 45 km, 1200 m/s (AftFlap meshes rotate on hinge)
  4. Flip maneuver: Starship flips to belly-first for max drag
  5. Retro burn: 5 km altitude, 3 Raptors ignite to slow to terminal vel
  6. Final burn: 1 km to touchdown, hover, descend
  7. Landing legs: deploy at 500 m (LandingLeg meshes extend)
  8. Touchdown: speed < 2 m/s, altitude ≈ 0, LANDED phase

EDL HUD overlay (different from orbital HUD):
  Radar altimeter: large vertical bar (0-5000 m)
  Vertical speed: m/s (red when > 50 m/s, yellow 10-50, green < 10)
  Horizontal speed: m/s
  G-force: instantaneous in g
  Phase indicator: ENTRY | PEAK-Q | FLAP GUIDE | RETRO | FINAL | TOUCHDOWN
```

**Mars surface:**
```
When altitude < 50 km over Mars:
  LaunchPadController analog for Mars → MarsTerrainController.cs
  Procedural terrain: OpenSimplex noise height field, rust-red color
  Sky: orange-pink (0.8, 0.4, 0.2) at horizon, dark pink at zenith
  Dust haze: low-altitude brownish fog

Mars atmosphere: thinner, ~600 Pa surface pressure (real Mars)
  SkyController: separate profile for "earth" vs "mars" vs "other"
```

**Super Heavy boostback (complete):**
In Semana 6 SH is just abandoned after separation. Semana 11 completes the loop:
- SH flies boostback burn automatically after separation
- SH decelerates and "catches" at launch tower (removed from simulation; play sound)
- Player can switch camera to SH at any time

**Test criteria for Semana 11 completion:**
- [ ] Mars surface visible when approaching low altitude
- [ ] Reentry plasma visible during EDL
- [ ] Flap guidance visual during aero descent
- [ ] Retrofire slows vertical speed
- [ ] Landing legs deploy and vessel lands
- [ ] LANDED phase triggers and freezes simulation

---

### Semana 12 — Polish and Full Mission Test

**Goal**: Complete mission Earth→Mars works end-to-end. No major bugs.

**Tasks:**
- Main menu scene (background: Starship on Mars from orbit)
- Save/load updated to persist CurrentTime + mission phase
- LOD for planets (reduce poly count at extreme distance)
- Performance pass: 60 fps target during ascent (heaviest phase: SH + 33 Raptor emitters)
- Sound balancing
- Full playtest: countdown → orbit → TMI → Mars EDL → landed
- Fix any physics bugs found in full playtest
- Update README Implementation Status table

---

## Known Issues and Bugs

(Update this section as issues are found and fixed)

| Issue | Severity | Status | Fix |
|---|---|---|---|
| Flame-out immediately at throttle | Critical | Fixed in Session 5 | Cross-feed in PartGraph.ConsumePropellant |
| Altitude shows 146 Gm (Sun dominates) | Critical | Fixed in Session 3 | GetDominantBody uses smallest SOI |
| Vessel not visible (black) | Major | Fixed in Session 3 | Lazy material init in VesselRenderer |
| Earth not visible (90° off) | Major | Fixed in Session 3 | Vessel spawned at +Z relative to Earth |
| PitchYawRoll has no effect | Major | Fixed in Session 5 | Applied in Vessel.Tick() |
| SAS locks out rotation | Major | Fixed in Session 5 | Only damps when no input |
| Engine plumes not visible | Known | Open — Semana 8 | GPU particles replacing cone mesh |
| Starship nosecone too sharp | Known | Open — Semana 7 | SphereMesh hemisphere for rounded nose |
| SH plumes stay visible after staging | Known | Open — Semana 7 | RebuildAfterStaging() needed |
| Stack visual not rebuilt after separation | Known | Open — Semana 7 | Debris vessel gets no renderer |

---

## Simulation API Reference

```csharp
var universe = SimulationBridge.Instance.Universe;

// Bodies
var earth = universe.GetBody("earth");
var dominant = universe.GetDominantBody(vessel.Position);

// Surface / atmosphere
double alt     = earth.GetAltitude(vessel.Position);
double density = earth.GetAtmosphericDensity(vessel.Position);
double pressure = earth.GetAtmosphericPressure(vessel.Position);

// Live orbital elements
var elements = OrbitalElements.FromStateVector(
    vessel.Position - earth.Position,
    vessel.Velocity - earth.Velocity,
    earth.GM, earth.Id, universe.CurrentTime);
double ap = elements.Apoapsis  - earth.Radius;
double pe = elements.Periapsis - earth.Radius;

// Place vessel in LEO
vessel.Position = earth.Position + new Vector3d(0, earth.Radius + 250_000.0, 0);
vessel.Velocity = earth.Velocity + new Vector3d(7800, 0, 0);  // circular orbit

// Flight controls
SimulationBridge.Instance.SetThrottle(1.0);
vessel.PitchYawRoll = new Vector3d(1, 0, 0);  // pitch up
SimulationBridge.Instance.TriggerStaging();

// Time warp
GetNode<TimeWarpController>("/root/TimeWarp").WarpUp();

// Mission phase
MissionManager.Instance.CurrentPhase  // enum MissionPhase
MissionManager.Instance.PhaseChanged  // Signal
```

---

## Autopilot Scripting (Lua — planned, not yet implemented)

Scripts in `lua_scripts/` run via MoonSharp. Coroutine-based `WAIT_UNTIL` yields without blocking.

```lua
-- Gravity turn to 200 km circular orbit
THROTTLE(1.0)
STAGE()
WAIT_UNTIL(function() return ALT() > 1000  end) PITCH_TO(80)
WAIT_UNTIL(function() return ALT() > 10000 end) PITCH_TO(60)
WAIT_UNTIL(function() return ALT() > 25000 end) PITCH_TO(45)
WAIT_UNTIL(function() return ALT() > 45000 end) PITCH_TO(15)
WAIT_UNTIL(function() return AP() >= 200000 end)
THROTTLE(0.0)
WARP_TO_APOAPSIS()
EXECUTE_MANEUVER(CIRCULARIZE())
PRINT("Orbit: " .. math.floor(PE()/1000) .. "x" .. math.floor(AP()/1000) .. " km")
```

Available functions: `THROTTLE(t)`, `STAGE()`, `PITCH_TO(deg)`, `ALT()`, `AP()`, `PE()`,
`SPEED()`, `WARP_TO_APOAPSIS()`, `CIRCULARIZE()`, `EXECUTE_MANEUVER(m)`,
`WAIT_UNTIL(fn)`, `WAIT(s)`, `PRINT(msg)`

---

## Inspirations

| Game | What it contributes |
|---|---|
| Kerbal Space Program | Orbital mechanics depth, part construction, crew system, maneuver planner |
| Spaceflight Simulator | Elegant minimal UI, satisfying controls, mobile-first simplicity |
| SpaceEngine | Scale, photorealism, sense of wonder at solar system scale |
| Universe Sandbox | N-body simulation, planetary physics visualization |

---

## Non-Goals

- No multiplayer
- No procedurally generated solar systems (real solar system only)
- No campaign, tech tree, or progression gates (pure sandbox)
- No combat or weapons
- No VR (desktop + web only)

---

*Last updated: Session 5. Next: Semana 6 — launch from Earth surface (LaunchPadController + full Starship stack).*
