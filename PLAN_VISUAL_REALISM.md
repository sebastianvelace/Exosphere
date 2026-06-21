# Exosphere — Plan de Fidelidad Visual Realista

Objetivo: hacer que el juego se lea visualmente como un simulador Starship/Super
Heavy real-scale, sin romper la fisica, el VAB ni el flujo de vuelo ya validados.
Este plan prioriza mejoras visibles y verificables sobre sistemas nuevos.

## Criterio Rector

- Mantener jugabilidad y telemetria existentes: no romper ascenso [G], hot-staging,
  orbit insertion, warp, map view ni EDL R13.
- Mejorar primero lo que aparece en todas las capturas: vehiculo, pluma,
  reentry, lighting/camera y pad.
- Cada mejora visual debe poder validarse con screenshots o smoke visual.
- No introducir assets pesados si una malla procedural/material Godot resuelve el
  problema con buena calidad.
- Las dimensiones visibles deben respetar escala real aproximada: Starship/Super
  Heavy 9 m de diametro, stack ~121 m, Ship ~50 m, booster ~71 m.

## V0 — Preparacion De Capturas

Antes de hacer cambios visuales grandes:

- Crear harness temporal no commiteado para capturas con framebuffer real.
- Capturar baseline:
  - pad lateral,
  - liftoff con pluma,
  - Max-Q/ascent,
  - hot-staging,
  - Starship sola en orbita,
  - map/orbit view,
  - belly-flop reentry,
  - flip-and-burn,
  - touchdown/crash,
  - cockpit.
- Guardar outputs fuera del repo o en carpeta ignorada.
- Confirmar que `tools/ci_check.sh` sigue limpio y que no hay `scripts/_*Shot.cs`
  ni autoload temporal en `project.godot`.

Aceptacion:
- Las capturas muestran nave, UI y efectos sin pantalla negra.
- Hay una comparacion visual antes/despues para cada cambio importante.

## V1 — Starship/Super Heavy Exterior

Archivos probables:
- `scripts/VesselRenderer.cs`
- `data/parts/starship_*.json`
- `data/parts/super_heavy_booster.json`

Mejoras:
- Acero inoxidable con paneles sutiles, variacion por bandas y anisotropia fake.
- Weld/ring seams a lo largo del stack.
- Tile layout mas reconocible en la cara windward: patron, borde, zonas negras y
  transicion hacia acero.
- Raceways/cable covers y detalles externos principales.
- Grid fins mas cercanas a forma real: espesor, pivote, lattice y sombreado.
- Flaps con base/hinge mas legibles y offset realista.
- Engine bay con 33 motores visuales mas creibles: outer ring, inner cluster,
  mounts, dark cavities.
- Super Heavy separado: hot-stage ring, vents, soot, scorched top and aft skirt.

Aceptacion:
- En pad lateral se identifica inmediatamente una Starship/Super Heavy.
- En staging se distinguen Ship, hot-stage ring y booster separado.
- Los detalles sobreviven a distancia media sin saturar la silueta.

## V2 — Plumas, Smoke Y Launch Pad

Archivos probables:
- `scripts/PlumeSystem.cs`
- `scripts/LaunchEffectsController.cs`
- `scripts/LaunchPadController.cs`
- `scripts/VesselRenderer.cs`

Mejoras:
- Pluma SL: mas opaca, turbulenta, expandida contra el pad, con core brillante.
- Pluma vacio: expansion mas amplia y limpia, menor humo.
- Startup/ramp: transicion visible desde ignicion a liftoff.
- Hot-staging: plume entre etapas con iluminacion corta y smoke/soot en el ring.
- Ground cloud: polvo/vapor horizontal, no solo columna vertical.
- Pad: OLM mas reconocible, flame trench/deflector mas legible, escala humana
  opcional si no distrae.

Aceptacion:
- Liftoff y hot-staging son legibles en una captura estatica.
- El pad no tapa la nave ni oculta el estado de vuelo.
- La pluma cambia visualmente entre SL, upper stage y vacio.

## V3 — Reentry Visual

Archivos probables:
- `scripts/ReentryPlasmaController.cs`
- `scripts/ReentryBreakupController.cs`
- `scripts/VesselRenderer.cs`
- `ExosphereSimulation/Physics/ThermalModel.cs` solo si hace falta exponer datos
  ya existentes; no retunear fisica sin tests.

Mejoras:
- Plasma ligado a heat flux, velocidad y densidad, no solo a un umbral simple.
- Shock layer concentrado en windward y nose/leading edges.
- Trail ionizado/estela tenue durante peak heating.
- Charring progresivo por zonas de tile.
- Breakup visual por fragmentos calientes cuando `ThermalBreakup` ocurre.
- Si la orientacion es incorrecta, los efectos deben dejar claro que el flujo pega
  donde no hay escudo.

Aceptacion:
- Belly-flop nominal se ve protegido y controlado.
- Entrada mal orientada se ve peligrosa antes de destruirse.
- El efecto no tapa por completo HUD/cockpit/mapa.

## V4 — Camara, Luz, Atmosfera Y Escala

Archivos probables:
- `scripts/CameraController.cs`
- `scripts/SunController.cs`
- `scripts/SkyController.cs`
- `scripts/PlanetMaterials.cs`
- `scripts/EarthGroundController.cs`

Mejoras:
- Exposicion y color por fase: pad, ascent, space, reentry, cockpit.
- Horizonte y atmosfera con gradiente mas realista.
- Camaras de seguimiento con encuadre estable para stack completo y Ship sola.
- Mejor percepcion de escala en pad y cerca de superficie.
- Evitar bloom/exposicion que lave el acero o la UI.

Aceptacion:
- Pad, orbita y reentry se distinguen por luz/color sin filtros exagerados.
- La nave no queda subexpuesta contra espacio ni quemada por pluma/reentry.
- Cockpit sigue legible.

## V5 — Automatizacion Visual

Archivos probables:
- `.github/workflows/ci.yml`
- `tools/ci_check.sh`
- harness temporal local no commiteado

Mejoras:
- Captura PNG bajo Xvfb para escenas clave.
- Guardar artifacts en CI.
- Chequeos simples: imagen no negra, dimensiones correctas, porcentaje minimo de
  pixeles no vacios, UI/nave visible por heuristica.
- Mantener prohibicion de harnesses trackeados.

Aceptacion:
- CI produce artifacts visuales descargables.
- Un render roto falla o queda claramente diagnosticado.

## No Hacer En Esta Tanda

- Engine-out real o motores fisicos individuales.
- Reescribir el VAB.
- Cambiar el guiado de ascenso o EDL sin telemetria nueva.
- Retunear heating/drag para que un VFX "se vea mejor" si rompe tests.
- Meter assets externos grandes sin una razon clara.

## Orden De Implementacion Recomendado

1. V0 capturas baseline.
2. V1 materiales/superficie Starship.
3. V2 pluma liftoff + hot-staging.
4. V3 reentry plasma/charring.
5. V4 camara/luz/atmosfera.
6. V5 capturas automatizadas en CI.
