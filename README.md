# Exosphere

**A Realistic Solar System Simulator**

> Inspired by Spaceflight Simulator (mobile) and SpaceEngine/Universe Sandbox.  
> Build rockets piece by piece. Navigate the real solar system. Keep your crew alive.

---

## Vision

Exosphere is a single-player sandbox space simulator that puts full orbital mechanics, structural physics, and human spaceflight in the hands of one player. The experience bridges the elegance of Spaceflight Simulator's minimalism with the depth and scale of SpaceEngine — all rendered in a stylized-realistic aesthetic that prioritizes clarity without sacrificing awe.

There is no campaign, no tech tree, no win condition. The sandbox is the game.

---

## Core Pillars

| Pillar | Description |
|---|---|
| **Realistic Physics** | N-body gravity, Keplerian orbits, atmospheric drag, dynamic pressure, and reentry heating |
| **Modular Construction** | Every bolt matters — build rockets part-by-part; structural choices have physical consequences |
| **Real Solar System** | Real masses, radii, and atmospheres — Earth, Moon, Mars, Venus, Jupiter and beyond |
| **Crew Management** | Astronauts with stats, EVA capability, training, and mortal risk |
| **Living Sandbox** | Time warp, scriptable autopilot, interplanetary infrastructure — play at your own depth |

---

## Features

### Physics Engine

- **N-body gravitational simulation** — every body in the system exerts real gravitational influence
- **Keplerian orbital mechanics** — elliptical, hyperbolic, and parabolic trajectories with proper conic sections
- **Atmospheric modeling** — density and pressure curves per planet; aerodynamic drag and lift
- **Reentry heating** — thermal flux based on velocity and atmospheric density; ablative heat shields required
- **Dynamic pressure** — structural limits on velocity × density; supersonic staging, max-q events
- **Stress simulation** — joints and parts have load limits; G-force and structural loading cause failures

### Rocket Construction

- **Part-by-part assembly** — stack tanks, engines, fairings, decouplers, SAS modules, solar panels, etc.
- **Physics-coupled design** — mass distribution, thrust vector, drag profile, and structural integrity derive directly from assembly
- **Fuel types** — liquid bipropellant, solid rocket boosters, monopropellant RCS, ion/electric
- **ISRU (In-Situ Resource Utilization)** — mine propellant from planetary bodies to refuel without Earth resupply
- **Procedural parts** — resize tanks and structural elements within physical constraints

### Solar System

- **Real bodies** — Earth, Moon, Mars, Venus, Mercury, Jupiter + major moons, Saturn + rings, Uranus, Neptune
- **Accurate parameters** — real mass, radius, axial tilt, rotational period, atmospheric composition
- **Surface landing** — terrain on all major bodies; highlands, craters, volcanic regions, polar ice
- **Day/night cycle** — orbital period and axial tilt drive lighting; solar irradiance affects power generation
- **Atmospheric variety** — Earth (aerodynamics + heating), Mars (thin, still relevant), Venus (crushing pressure), airless bodies (no drag)

### Resource Systems

| Resource | Mechanic |
|---|---|
| **Fuel / Oxidizer** | Tracked per tank; depletion ends thrust |
| **Electric charge** | Solar panels, RTGs, batteries; powers SAS, computers, life support |
| **Monopropellant** | RCS thrusters for fine attitude control and docking |

### Crew & EVA

- **Astronauts as persistent entities** — each has a name, training level, and experience history
- **Risk of death** — exposure, structural failure, reentry errors, EVA malfunctions
- **EVA mechanics** — spacewalks for repairs, sample collection, module assembly in orbit
- **Life support via electric charge** — suits and habitats draw power; failures have consequences
- **Crew training** — more experienced astronauts perform EVA more effectively and handle emergencies better

### Mission Types

- **Launch and orbit** — reach LEO, MEO, GEO, and escape trajectories from any launchpad
- **Lunar and planetary landing** — powered descent, terrain navigation, ascent and rendezvous
- **Orbital stations** — dock modules to build permanent infrastructure; refueling depots
- **Interplanetary transfers** — Hohmann transfers, gravity assists, bi-elliptic maneuvers with real porkchop plots
- **ISRU operations** — establish surface outposts that produce fuel for further exploration

### Surface Infrastructure

- **Planetary bases** — deploy habitats, fuel plants, power systems, launch pads on any solid surface
- **ISRU chains** — extract regolith → process into propellant → load into waiting vehicles
- **Persistent world** — bases and vehicles remain deployed between sessions; the solar system state is saved

### Navigation & Flight

- **Interactive 3D map** — orbital view of the entire solar system; zoom from planetary to system scale
- **Maneuver node editor** — drag burn vectors on the map to plan Δv, see resulting trajectory, projected periapsis/apoapsis
- **Encounter prediction** — patched conics show planetary SOI intercepts and flyby trajectories
- **Navball** — prograde/retrograde/normal/radial indicators; heading, pitch, roll in all reference frames
- **Cockpit view** — diegetic instrument panels inside the vehicle; no overlay required if desired
- **HUD overlay** — classic 2D information overlay: altitude, velocity (surface/orbital), Ap/Pe, time to burn

### Autopilot & Scripting

- **SAS modes** — stability assist, prograde lock, retrograde, normal, radial, maneuver node tracking
- **Basic autopilot** — hold heading, circularize at current altitude, execute maneuver node
- **Flight script engine** — scriptable automation (kOS-style) for launch profiles, automated rendezvous, landing sequences
- **Example scripts included** — gravity turn launch, Hohmann transfer, suicide burn landing

### Time

- **Real-time simulation** with selectable warp rates: ×5, ×10, ×100, ×1000, ×10000, ×100000
- **Physics warp** at low rates (≤×4); **rail warp** at high rates (vessels follow conic trajectories)
- **Warp restrictions** — auto-cancel near atmosphere, during burns, and on collision course

---

## Art Direction

**Stylized Realistic** — the goal is visual clarity at scale.

- Planets and moons rendered with high-detail surface textures; readable from orbit
- PBR materials on spacecraft parts with metal, paint, and thermal damage states
- Volumetric atmosphere glow on planetary limbs; accurate terminator and shadow
- Spacecraft scale relative to Earth is true (a real rocket next to Earth looks tiny and right)
- No bloom overload — light is controlled and purposeful
- UI uses monospace / engineering aesthetic: clean readouts, no skeuomorphic chrome

---

## Platform

| Target | Method |
|---|---|
| **Web (primary)** | Runs in modern browsers at acceptable frame rates |
| **Desktop (secondary)** | Packaged from the same codebase; higher resolution and performance |

---

## Stack

| Layer | Technology | Rationale |
|---|---|---|
| **Game engine** | Godot 4.3+ | Free, WASM web export, native desktop export, open source |
| **Primary language** | C# (.NET 8) | Static typing, generics, double precision — familiar to TypeScript devs; required for simulation math |
| **Rendering** | Godot 4 Vulkan / OpenGL ES 3 | Vulkan on desktop, OpenGL ES 3 fallback for web |
| **Custom shaders** | Godot Shader Language (GLSL subset) | Atmosphere scattering, reentry plasma, planet surface |
| **Physics (game)** | Godot Jolt (built-in) | Construction snap, landing gear, debris — local scale only |
| **Physics (orbital)** | Custom C# simulation library | N-body, Keplerian propagation, RK4 integrator — fully decoupled from engine |
| **Autopilot scripting** | MoonSharp (Lua interpreter for C#) | Lua is minimal, sandboxed, scriptable by players — same approach as kOS |
| **3D assets** | GLTF 2.0 | Engine-agnostic, supports PBR materials natively in Godot |
| **Data files** | JSON | Part definitions, planet data, save files — human-readable and debuggable |
| **Web export** | Godot WASM + WebGL 2.0 | Single export target; same codebase as desktop |
| **Desktop export** | Godot native | Linux, Windows, macOS from one project |
| **Build tooling** | .NET CLI + Godot export templates + GitHub Actions | Automated builds for web and desktop |

### Why Godot over Unity
Unity has a larger asset store but its licensing history is unstable for indie projects and its WebGL export is heavier and slower. Godot is zero-cost, its web export is leaner, and the C# integration via .NET 8 is mature as of Godot 4.2+.

### Why C# over GDScript
GDScript is Python-like (dynamically typed). C# is closer to TypeScript: static types, generics, LINQ, interfaces, async/await. The orbital simulation library specifically needs `double` precision floating point and type-safe data structures — GDScript doesn't offer this. All game code and simulation code stays in one language.

### Why custom orbital physics
Godot Jolt is a rigid-body physics engine operating in meters at human scale. Space simulation has two problems it cannot solve: (1) the solar system spans ~10¹³ meters, far beyond float32 precision; (2) N-body gravity and Keplerian propagation are mathematical operations, not collision detection. The custom library runs in `double` precision, is fully headless (no engine dependency), and can be unit-tested independently.

---

## Architecture

### The Scale Problem

A spacecraft in low Earth orbit is ~250 km above a planet with a 6,371 km radius. The distance from Earth to Neptune is 4.5 billion km. Float32 has 7 significant digits of precision — it loses accuracy beyond ~16 million meters, which is less than Earth–Moon distance. Everything in Exosphere uses `double` for simulation positions and velocities.

**The Floating Origin pattern** solves the rendering side: every frame, the 3D world origin is set to the active vessel's position. All other objects (planets, other vessels, surface bases) are rendered at `(simPosition - originPosition)` cast to `float32`. Since everything is rendered relative to the origin and the active object is always at `(0,0,0)`, float32 precision is sufficient for rendering even at solar-system scale.

---

### Layer Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER  (Godot 4 scene tree)                            │
│                                                                      │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  ┌─────────┐  │
│  │ 3D Viewport │  │ Orbital Map  │  │  HUD Overlay  │  │ Cockpit │  │
│  │ (flight)    │  │ (3D, scaled) │  │  (2D Control) │  │ (3D VP) │  │
│  └─────────────┘  └──────────────┘  └───────────────┘  └─────────┘  │
└──────────────────────────────────────────────────────────────────────┘
                                 ↕
┌──────────────────────────────────────────────────────────────────────┐
│  GAME LAYER  (C# + Godot nodes)                                      │
│                                                                      │
│  ConstructionSystem │ CrewManager │ AutopilotController │ TimeWarp   │
│  SaveSystem         │ UIBridge    │ FloatingOrigin       │ SoundMgr  │
└──────────────────────────────────────────────────────────────────────┘
                                 ↕
┌──────────────────────────────────────────────────────────────────────┐
│  SIMULATION LAYER  (pure C# library, no Godot dependency)            │
│                                                                      │
│  Universe           │ CelestialBody  │ Vessel         │ PartGraph   │
│  OrbitalIntegrator  │ AtmosphereModel│ ThermalModel   │ StressSolver│
│  KeplerPropagator   │ ManeuverPlanner│ LuaScriptEngine│ IsruSolver  │
└──────────────────────────────────────────────────────────────────────┘
                                 ↕
┌──────────────────────────────────────────────────────────────────────┐
│  DATA LAYER                                                          │
│                                                                      │
│  /data/parts/*.json │ /data/bodies/*.json │ /saves/*.json           │
│  /assets/models/    │ /assets/textures/   │ /scripts/*.lua          │
└──────────────────────────────────────────────────────────────────────┘
```

---

### Simulation Layer (Core)

#### Universe

The root simulation object. Holds all `CelestialBody` and `Vessel` instances. Advances time by calling the integrator.

```
Universe
  ├── bodies[]           CelestialBody — Sun, Earth, Moon, Mars, …
  ├── vessels[]          Vessel — all active rockets and probes
  ├── Tick(dt: double)   advance simulation by dt seconds
  └── TimeScale          current warp multiplier
```

**Time warp modes:**
| Warp rate | Integration mode |
|---|---|
| ×1 – ×4 | Full RK4 for all vessels, all bodies |
| ×5 – ×1000 | RK4 for active vessel; on-rails (Keplerian) for all others |
| ×10000+ | All vessels on Keplerian rails; no RK4; time jumps directly |

#### CelestialBody

```
CelestialBody
  ├── mass: double        (kg)
  ├── radius: double      (m)
  ├── position: Vector3d  (m, simulation space)
  ├── velocity: Vector3d  (m/s)
  ├── atmosphere: AtmosphereModel
  └── terrain: TerrainSampler
```

Real data sourced from NASA Horizons / IAU. Bodies are numerically integrated at ×1–×4 warp; at higher warp they follow precomputed ephemeris.

#### Vessel

```
Vessel
  ├── parts: PartGraph    tree of connected parts
  ├── position: Vector3d  root part position (m)
  ├── velocity: Vector3d  (m/s)
  ├── orientation: Quaternion
  ├── isOnRails: bool
  ├── crew: CrewMember[]
  └── ComputeTotalMass() / ComputeThrust() / ComputeDrag()
```

When `isOnRails = true`, the vessel follows a Keplerian conic (no forces computed). Burns switch it to active physics mode.

#### PartGraph

Parts form a tree (root = command pod). Each edge is a `Joint` with a structural load limit. The solver:

1. Computes aerodynamic forces on each part (drag, lift from shape + angle of attack)
2. Walks the tree bottom-up accumulating tensile/compressive stress at each joint
3. Any joint exceeding its limit detaches — the subtree becomes a new separate `Vessel`

#### OrbitalIntegrator (RK4)

Classic 4th-order Runge-Kutta integration over position and velocity with N-body acceleration:

```
a = Σ G·Mᵢ / |r - rᵢ|² · (rᵢ - r) / |r - rᵢ|   for each body i
```

Adaptive step size: if error estimate exceeds threshold, halve Δt and retry. During physics warp (×2–×4) the fixed step is simply divided by `TimeScale`.

#### KeplerPropagator

For on-rails vessels: given initial orbital elements (a, e, i, Ω, ω, ν), compute position and velocity at any future time analytically. Zero computation cost — supports ×100000 warp trivially.

#### AtmosphereModel

Per-body exponential density model with optional layered overrides:

```
ρ(h) = ρ₀ · exp(-h / H)     H = scale height (m)
P(h) = P₀ · exp(-h / H_P)
T(h) = piecewise linear       from real atmospheric profile
```

Used by drag: `F_drag = ½ · ρ · v² · Cd · A`
Used by heating: `q = k · ρ · v³` (simplified Detra-Kemp-Riddell)

---

### Rendering Architecture

#### Floating Origin (every frame)

```
renderOrigin = activeVessel.position   // Vector3d
foreach (renderable) {
    renderable.node.Position = (Vector3)(renderable.simPosition - renderOrigin)
}
```

This keeps all `float32` render positions within ±10 km of origin — well within float precision.

#### Planet Rendering

Each planet is a sphere mesh with a custom Godot shader:

- **Albedo**: high-res texture projected onto sphere (2K web, 8K desktop)
- **Normals**: normal map for surface relief
- **Atmosphere**: screen-space post-process shader doing Rayleigh + Mie single scattering
- **Clouds**: animated 2D texture scrolled over atmosphere layer
- **City lights**: blended in on the night side based on real distribution maps

LOD: 4 levels from orbit (low poly + single texture) to close approach (high poly + detail tiles).

#### Vessel Rendering

- Parts are GLTF meshes instantiated and parented according to `PartGraph`
- Each part has PBR material: metallic/roughness, albedo, emissive (engines glow)
- Thermal state drives an emissive heat gradient (blue → orange → white) on reentry
- Engine exhaust: Godot `GPUParticles3D` with custom shader

#### Orbital Map

A separate 3D scene rendered to a `SubViewport`. The solar system is scaled down by `1 / SCALE_FACTOR` (e.g. 1:10⁹). Vessels are rendered at fixed screen-size billboards so they remain visible regardless of scale. Maneuver nodes are 3D gizmo handles: drag to adjust burn Δv, rotation snaps to prograde/normal/radial axes.

---

### Construction System

The part editor is a separate Godot scene. Parts snap to attachment nodes (defined in part JSON). On assembly commit:

1. `PartGraph` is built from the placed parts
2. Mass, CoM, CoT, and drag profile are computed
3. `Vessel` is created in `Universe` and handed off to the flight scene

Parts are defined in JSON:

```jsonc
// data/parts/liquid_engine_merlin.json
{
  "id": "engine_merlin",
  "category": "engine",
  "mass_dry": 470,          // kg
  "thrust_vac": 934000,     // N
  "isp_vac": 348,           // s
  "isp_sl": 282,            // s
  "gimbal_range": 5.0,      // degrees
  "attachment_nodes": [
    { "id": "top",    "position": [0, 1.2, 0],  "type": "stack" },
    { "id": "bottom", "position": [0, -0.6, 0], "type": "engine_bell" }
  ],
  "drag_model": "cylinder",
  "heat_tolerance": 2000     // K before damage
}
```

---

### Autopilot Scripting (Lua / MoonSharp)

Player-written Lua scripts run in a sandboxed MoonSharp interpreter. The API exposes vessel state and control surfaces:

```lua
-- example: gravity turn launch to 200km orbit
THROTTLE(1.0)
STAGE()
WAIT_UNTIL(function() return ALT() > 1000 end)

-- pitch over to 45° at 20km
WAIT_UNTIL(function() return ALT() > 20000 end)
PITCH_TO(45)

-- circularize at apoapsis
WARP_TO_APOAPSIS()
EXECUTE_MANEUVER(CIRCULARIZE())
```

The engine is coroutine-based: `WAIT_UNTIL` yields control without blocking the game thread.

---

### Save System

Universe state is serialized to JSON on scene transitions and autosave intervals:

```
saves/
  quicksave.json
  autosave_001.json
  slot_mars_mission/
    meta.json          { name, date, screenshot_path }
    universe.json      { bodies[], vessels[], bases[] }
    crew.json          { astronauts[] }
```

`universe.json` stores vessels as orbital elements (for on-rails) or full state vectors (for active vessels). Part trees are stored as ordered arrays matching the assembly graph.

---

### Directory Structure (Godot Project)

```
exosphere/
  project.godot
  ExosphereSimulation/          C# simulation library (no Godot deps)
    Universe.cs
    CelestialBody.cs
    Vessel.cs
    PartGraph.cs
    OrbitalIntegrator.cs
    KeplerPropagator.cs
    AtmosphereModel.cs
    ThermalModel.cs
    StressSolver.cs
    ManeuverPlanner.cs
  scenes/
    flight/                     main flight scene
    construction/               part editor
    orbital_map/                navigation map
    ui/                         HUD, menus, dialogs
  scripts/                      C# game-layer scripts
    FloatingOrigin.cs
    ConstructionSystem.cs
    CrewManager.cs
    AutopilotController.cs
    TimeWarpController.cs
    SaveSystem.cs
  data/
    parts/                      *.json — part definitions
    bodies/                     *.json — planet/moon data
    launch_sites/               *.json — launchpad locations
  assets/
    models/                     *.glb — part and planet meshes
    textures/                   planet surfaces, part albedo
    shaders/                    atmosphere.gdshader, reentry.gdshader
  lua_scripts/                  example autopilot scripts
  exports/
    web/
    linux/
    windows/
```

---

## Scope Management (Solo Dev)

This is an ambitious feature set. The development philosophy is:

1. **Core loop first** — launch → orbit → land is playable before any other feature ships
2. **Vertical slices** — each feature is complete before the next begins; no half-implemented systems
3. **Defer complexity** — ISRU, colonies, and scripting are late-game features; physics and construction are day-one
4. **Modding later** — architecture will be mod-friendly but no public API until the game is content-complete

### Feature Priority Order

```
Phase 1 — Foundation
  ├── Physics engine (N-body + aerodynamics)
  ├── Part construction system
  ├── Earth + Moon (first playable system)
  └── Basic flight + navball + time warp

Phase 2 — Full Solar System
  ├── All planets and major moons
  ├── Atmospheric models per body
  ├── Reentry and heat simulation
  └── 3D orbital map + maneuver nodes

Phase 3 — People and Places
  ├── Crew system + EVA
  ├── Orbital docking + stations
  ├── Surface bases + ISRU
  └── Interplanetary missions (Mars, Venus, outer planets)

Phase 4 — Depth
  ├── Scriptable autopilot
  ├── Structural stress + damage
  ├── Cockpit + diegetic instruments
  └── Polish, performance, mod architecture
```

---

## Inspirations

| Game | What it contributes |
|---|---|
| **Spaceflight Simulator** | Elegant minimal UI, satisfying rocket construction, direct controls |
| **SpaceEngine** | Scale, photographic realism, sense of wonder across the solar system |
| **KSP / KSP2** | Orbital mechanics depth, part construction philosophy, crew system |
| **Universe Sandbox** | N-body simulation, planetary physics, scale visualization |

---

## Non-Goals

- No multiplayer (out of scope for solo dev)
- No procedurally generated solar systems (real solar system only)
- No campaign or tech tree (pure sandbox)
- No in-game economy or resource management beyond fuel and power
- No combat or weapons

---

*Concept and architecture document — implementation begins in the next phase.*
# Exosphere
