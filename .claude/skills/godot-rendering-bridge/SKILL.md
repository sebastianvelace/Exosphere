---
name: godot-rendering-bridge
description: Convenciones de la capa de juego Godot de Exosphere (scripts/, namespace Exosphere.Game). Úsala al tocar render, cámaras (FPV, mapa, órbita), FloatingOrigin, scaled-space, HUD/navball, SimulationBridge, señales, o cualquier conversión doble-precisión↔float. Imprescindible al cruzar la frontera juego↔simulación.
---

# Capa de juego Godot — `scripts/` (`Exosphere.Game`)

Capa que renderiza el estado del sim y captura input. Aquí SÍ se usa `Godot`. El `.csproj`
(`Exosphere.csproj`) excluye `ExosphereSimulation/**` y referencia la librería de física.

## La frontera: `SimulationBridge`

- Singleton: `SimulationBridge.Instance`. ÚNICO punto de contacto juego↔sim.
- Posee `Universe`, expone `ActiveVessel`, el API de time-warp y emite señales:
  `VesselStaged`, `VesselDestroyed`, `SimulationLoaded`.
- Los controladores LEEN desde `SimulationBridge.Instance.Universe` / `.ActiveVessel`. **Evita editar
  la capa sim desde aquí**; si necesitas un dato nuevo, exponlo en el sim y léelo por el bridge.
- Time-warp: `SetWarpIndex(i)`, `WarpIndex`, `WarpLevels`, `MaxAllowedWarpIndex`.

## FloatingOrigin y escala (CLAVE)

- **`1 unidad Godot = 2.8 m`** (`MetresPerUnit`).
- El **vessel activo se renderiza en el ORIGEN**. `FloatingOrigin` mueve el mundo a su alrededor
  cada frame para mantener coordenadas pequeñas (precisión float, sin z-fighting).
- **Planetas = scaled-space backdrop**: esferas unitarias a distancia FIJA `BackdropDistance =
  50 000 u`, escaladas para subtender su tamaño angular correcto según la altitud. NO están a su
  distancia real. El Earth lejano y el parche de suelo local se funden por altitud de cámara
  (`CameraAltOverEarth`) para no solaparse en una costura.
- Orientación de la Tierra: `PlanetTilt` coloca Cabo Cañaveral (27.5°N/80.7°O) en el sitio de
  lanzamiento. Convención equirectangular del shader: `lon = atan2(z,x)`, `lat = asin(y)`.

## Conversión de precisión

- El sim trabaja en `Vector3d`/`Quaterniond` (double). El render en `Vector3`/`Quaternion` (float).
- Convierte **solo en la capa de juego**, lo más tarde posible, aplicando el origen flotante y la
  escala (`/ MetresPerUnit`). Nunca metas float en el estado del sim.

## Cámaras y HUD

- Vistas: FPV (cabina Crew-Dragon), mapa del sistema ([Tab]), órbita/seguimiento. `[C]` cicla FPV.
- HUD estilo SpaceX: `HUDController`, `NavBallController`/`AttitudeNavball`, `EngineGridHUD`,
  `SystemsHUD`. Layout: SYSTEMS a la izquierda, PROPULSION abajo-izquierda (evitar solapes).

## GOTCHAS

- Nodos hermanos creados en `_Ready()` van con `CallDeferred("add_child", ...)` (el padre `Flight`
  aún está ocupado en su propio `_Ready`).
- El "33 Raptors / 6 motores" es **solo visual** — no asumas 1 nodo de motor = 1 motor físico
  (el sim tiene 1 parte-motor por etapa).
- No edites `.godot/`, `*.cs.uid`, `bin/`, `obj/` (generados).

## Verificación

`dotnet build Exosphere.csproj --nologo -v quiet` → 0/0. Para cambios visuales, usa la skill
`visual-testing` (captura headless), no asumas que "compila = se ve bien".
