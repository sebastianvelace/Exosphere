---
name: orbital-physics
description: Convenciones de la librería de simulación Exosphere (ExosphereSimulation/). Úsala al tocar física orbital, integradores RK4/Kepler, doble precisión, time-warp, SOI, masas/ISP/empuje, o cualquier código bajo el namespace Exosphere.Simulation. Imprescindible antes de modificar Universe, Vessel, CelestialBody, OrbitalElements o los integradores.
---

# Física orbital — librería `ExosphereSimulation`

Librería de física **pura C#, sin Godot**. Namespace `Exosphere.Simulation` (+ `.Math`,
`.Integrators`, `.Physics`, `.Systems`, `.Parts`). Debe compilar y razonarse de forma aislada.

## Reglas no negociables

- **CERO `using Godot;`** en este proyecto. Si necesitas un tipo vectorial, usa `Vector3d` /
  `Quaterniond` propios — NUNCA los de Godot (que son float y rompen la precisión).
- **Doble precisión en todo el estado físico.** Posición, velocidad, orientación, tiempo: `double`.
- **Unidades SI, siempre:** metros, m/s, kg, segundos. `GM` = μ en m³/s². Internamente los ángulos
  van en **radianes** (los JSON usan grados y se convierten al cargar).
- **Tiempo = segundos desde J2000** (`Universe.CurrentTime`).

## Modelo de simulación

- `Universe` es la raíz: posee `Bodies` y `Vessels`, lleva `CurrentTime`, y según `TimeScale`
  despacha al integrador correcto. `ActiveVessel` = el que pilota el jugador.
- Sub-pasos (en `Universe`): `MaxPhysicsStep = 0.02` (física plena 50 Hz),
  `MaxCoastStep = 2.0` (vessel en coast bajo warp), `MaxThrustStep = 0.1` (quemando bajo warp).
- **On-rails vs RK4:** el vessel activo integra con `RK4Integrator`; el resto va "on rails" con
  `KeplerPropagator` (Kepler exacto). `Vessel.IsOnRails` + `Vessel.OrbitalState` controlan el modo.
- **`Vessel.ReferenceBodyId`** define el cuerpo dominante / SOI actual.

## Vessel — puntos a respetar

- Estado cinemático en marco inercial: `Position`, `Velocity` (Vector3d), `Orientation`,
  `AngularVelocity` (Quaterniond / Vector3d).
- Controles: `Throttle [0,1]`, `PitchYawRoll [-1,1]/eje`, `SASEnabled`.
- Masa/CoM se derivan del `PartGraph` (`TotalMass`, `CenterOfMass`).
- Aerodinámica: `GetDynamicPressure(body)` = q = ½·ρ·v² respecto a la atmósfera en rotación
  (define el Max-Q). `GetSurfaceVelocity(body)` resta la rotación del cuerpo.
- Contrato de crash (lo escribe quien gestiona impactos, lo leen los efectos):
  `IsDestroyed`, `CrashImpactSpeed`, `CrashSimPosition`.

## GOTCHAS

- **Cada etapa = UNA parte-motor.** Super Heavy = 1 parte (~74 MN), Starship = 1 parte. Los "33" y
  "6" son visuales. `Parts.ActiveEngines.Count == 1` por etapa — no iteres motores físicos por bell.
- **Impacto soft-rest**: hoy `Universe.cs` reubica a `Radius + 1` y aplica velocidad de superficie
  cuando `altitude < 0`, en lugar de destruir. Por eso un aterrizaje "se posa" sin estrellarse.
  Si cambias crash/EDL, esto es lo primero que toca revisar.
- **No mezcles float/double**: convertir a `Vector3` de Godot solo en la capa de juego, nunca dentro
  del sim.
- **No hardcodees constantes de cuerpos/piezas** — viven en `data/*.json` y se cargan en runtime.

## Verificación

`dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet` debe dar 0/0.
Para validar conservación (energía/momento orbital), un coast largo en RK4 debe mantener la forma de
la órbita a ~1e-6 %. No romper el ascenso [G] a órbita ni la EDL.
