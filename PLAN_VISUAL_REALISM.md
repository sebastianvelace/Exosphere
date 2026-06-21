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

## V0 — Preparacion De Capturas  ✅ FUNCIONANDO

Captura con framebuffer real **validada** via `xvfb-run` (ya instalado): se corre Godot SIN
`--headless` bajo un display virtual y un autoload temporal `scripts/_CaptureShot.cs` (patron
`_*Shot`, gitignored) que engancha el autopiloto de ascenso, espera `RenderingServer.FramePostDraw`
y guarda PNGs a `/tmp` en fases clave (ignition/liftoff/low-ascent), reportando avgLum/nonEmpty para
descartar pantallas negras. Comando:

```bash
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" --path . --rendering-driver opengl3
```

Esto desbloquea verificar TODO cambio visual con screenshots reales (antes el dummy renderer de
`--headless` impedia capturar). El harness es temporal y se limpia (`rm` + `git checkout project.godot`).
Siguiente paso natural: cablearlo a CI bajo Xvfb (V5).

## V0 (original) — Preparacion De Capturas

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
- Confirmar que `tools/ci_check.sh` sigue limpio y que no hay `scripts/_*Shot.cs`,
  `scripts/*VerifyShot.cs`, `scenes/*VerifyShot.tscn` ni autoload temporal en
  `project.godot`.

Aceptacion:
- Las capturas muestran nave, UI y efectos sin pantalla negra.
- Hay una comparacion visual antes/despues para cada cambio importante.

## V1 — Starship/Super Heavy Exterior

Archivos probables:
- `scripts/VesselRenderer.cs`
- `data/parts/starship_*.json`
- `data/parts/super_heavy_booster.json`

Mejoras:
- [x] Acero inoxidable con paneles sutiles, variacion por bandas y anisotropia fake.
- [x] Weld/ring seams a lo largo del stack.
- [x] Tile layout mas reconocible en la cara windward: patron, borde, zonas negras y
  transicion hacia acero.
- [x] Raceways/cable covers y detalles externos principales.
- [ ] Grid fins mas cercanas a forma real: espesor, pivote, lattice y sombreado.
- [x] Flaps con base/hinge mas legibles y offset realista.
- [x] Engine bay con 33 motores visuales mas creibles: outer ring, inner cluster,
  mounts, dark cavities.
- [x] Super Heavy separado: hot-stage ring, vents, soot, scorched top and aft skirt.
- [x] Primera pasada de payload-door cues, seams longitudinales y aft shield/skirt.

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
- [x] Pluma SL: mas opaca, turbulenta, expandida contra el pad, con core brillante.
- [x] Pluma SL/ascenso mas brillante y ANCHA (verificado por captura): el column merged de 33
  Raptors se leia como humo fino contra el cielo; ahora mouths mas anchos + energy 3.0->4.6 (SH) /
  3.4->4.0 (Ship). El ground cloud de 5 capas (`LaunchEffectsController`) ya era fuerte, no se toco.
- [x] Pluma vacio: legibilidad subida (verificado por captura a 120/152 km). El shader
  `raptor_plume.gdshader` ya hace el vacio opticamente DELGADO a proposito (realista); contra la
  Tierra brillante se leia como un manchon. Subido `vacuumDim` 0.45->0.62 y `vacuumAlpha` 0.40->0.55
  (sigue mas tenue que SL, ahora legible). NO se reescribio el shader — es un asset deliberado.
- [ ] Startup/ramp: transicion visible desde ignicion a liftoff.
- [ ] Hot-staging: plume entre etapas — **GAP CONFIRMADO por captura**. Al separar (`exo_hotstage`,
  ~63 km) la Starship ya enciende normal pero NO hay flash/plume brillante ENTRE etapas ni soot en el
  hot-stage ring del booster. Approach para el proximo agente: enganchar la senal
  `SimulationBridge.VesselStaged` (se emite en `TriggerStaging`); en un controlador nuevo
  (`scripts/HotStageFlashController.cs`, patron self-install como `ReentryBreakupController`) spawnear
  un OmniLight3D + un GpuParticles3D corto (~1-1.5 s) de plume/soot en el plano de separacion (y/o
  scorch en el tope del booster en el `VesselRenderer` del debris). OJO: el momento dura ~1 frame —
  el harness de captura debe disparar VARIOS frames seguidos tras el drop de part-count para verlo.
- [ ] Ground cloud: polvo/vapor horizontal, no solo columna vertical.
- [x] Pad: OLM mas reconocible, flame trench/deflector mas legible, escala humana
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

1. V0 capturas baseline. ✅ Captura real con framebuffer via `xvfb-run` validada (ver tope del doc).
2. V1 materiales/superficie Starship. Parcialmente cerrado; falta close-up fino y grid fins.
3. V2 plumas. ✅ Pluma SL/ascenso (brillo+ancho), ✅ pluma de vacio (legibilidad). Falta:
   **hot-staging** (gap confirmado, approach especificado arriba), startup/ramp, pluma de vacio
   "limpia" con menos humo.
4. V3 reentry plasma/charring.
5. V4 camara/luz/atmosfera.
6. V5 capturas automatizadas en CI (cablear el xvfb capture de V0).

## Bitacora Para El Proximo Agente (que se hizo y como continuar)

Sesion de fidelidad visual (jun 2026). Contexto para retomar sin re-derivar:

**Hecho y verificado por screenshots reales (xvfb):**
- **Captura real funcionando** (`xvfb-run` + autoload temporal `_CaptureShot.cs`). Ver bloque "V0"
  arriba y la memoria `visual-capture-xvfb`. Esto es LA herramienta para todo cambio visual:
  cambio -> build -> capturar -> mirar el PNG con la tool Read -> comparar. Igual que el tuning de
  la EDL en R13. El `--headless` NO sirve (dummy renderer).
- **Pluma SL/ascenso** mas brillante y ancha (`scripts/PlumeSystem.cs`, commit `390a7ce`): mouths del
  core/anillos mas anchos + `energy` del shader SH 3.0->4.6 / Ship 3.4->4.0. Antes era humo gris fino.
- **Pluma de vacio** legible (`assets/shaders/raptor_plume.gdshader`, commit `4d971ad`):
  `vacuumDim`/`vacuumAlpha` un poco mas altos. El shader es un asset deliberado y bien hecho — NO
  reescribir; tunear con cuidado.
- El **ground cloud** (`LaunchEffectsController.cs`) ya es una nube de deluge de 5 capas muy iterada
  ("N5"); las capturas confirman que es fuerte. **No tocar a ciegas.**

**Como tunear plumas (mapa rapido):**
- Tamaño/colores/brillo por anillo: `PlumeSystem.SetupSH` / `SetupStarship` (mouthR, length, core).
- Brillo maestro + diamantes: uniforme `energy` y `diamond_count` en `PlumeSystem.BuildUnit`.
- Forma/opacidad/color por presion (SL vs vacio): el fragment de `raptor_plume.gdshader`
  (`effExpansion`, `reach`, `vacuumDim`, `vacuumAlpha`, `eScale`). SL = corto/brillante/diamantes;
  vacio = largo/tenue/sin diamantes (a proposito).
- Ground cloud (deluge): `LaunchEffectsController.cs`.

**Proximo paso mas valioso:** hot-staging (gap confirmado, approach detallado en V2 arriba). Luego
startup/ramp de ignicion, y despues V3 (reentry plasma ligado al heat flux real, que YA esta en el
sim como `WorstHeatRatio`/`Part.ThermalDamage`).
