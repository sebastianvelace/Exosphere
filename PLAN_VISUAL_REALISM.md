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

## V0 — Capturas Visuales Locales ✅ FUNCIONANDO / CI PARCIAL

Captura local con framebuffer real **validada** via `xvfb-run` (ya instalado):
se corre Godot SIN `--headless` bajo un display virtual y un autoload temporal
`scripts/_CaptureShot.cs` o harness equivalente gitignored, espera
`RenderingServer.FramePostDraw` y guarda PNGs a `/tmp` en fases clave.

```bash
xvfb-run -a -s "-screen 0 1920x1080x24" "$GODOT" --path . --rendering-driver opengl3
```

Estado:
- [x] `--headless` queda reservado para smoke/load; no sirve para validar PNGs
  visuales en este entorno porque usa dummy renderer.
- [x] Captura local con Xvfb validada y usada para pad, liftoff, ascenso y pluma
  orbital.
- [x] Guard anti-harness cubre `scripts/_*Shot.cs`, `scripts/*VerifyShot.cs`,
  `scenes/*VerifyShot.tscn` y autoloads temporales en `project.godot`.
- [x] `tools/visual_playtest.sh` — runner local + CI `--smoke` (VAL-01 partial).
- [ ] Pendiente CI: capturas PNG end-to-end, artifacts descargables y heuristicas
  simples de imagen no negra / nave visible / UI visible (CC-01 full matrix).

Baseline minimo a mantener fuera del repo:
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

## V0.5 — Auditoria Con Referencias Reales

Objetivo: evitar mejoras visuales por intuicion. Cada cambio visual relevante debe
partir de una referencia real, una captura actual comparable y un criterio de
aceptacion explicito.

Referencias iniciales a consultar antes de implementar:
- SpaceX Starship official: https://www.spacex.com/vehicles/starship/
- SpaceX flight webcasts / update pages de Starship Flight 4-6 para liftoff,
  Max-Q, hot-staging, boostback, reentry y splashdown.
- NASA / Artemis / HLS para variantes futuras y diferencias visuales: HLS no es
  la Starship atmosferica normal; no mezclar landing legs/solar arrays/HLS con el
  stack orbital actual salvo que se cree variante nueva.
- Fotos/video de Starbase: OLM, water-cooled steel plate, deluge, tank farm,
  chopsticks, SQD/BQD y lightning towers.

Matriz de busqueda y aceptacion:

| Fase | Referencia real | Captura actual | Diferencia observable | Archivo dueño | Criterio de aceptacion |
| --- | --- | --- | --- | --- | --- |
| Pad lateral | Starship/Super Heavy en Starbase, vista lateral diurna | `/tmp/exosphere_pad_*.png` | Silueta, proporcion nariz/flaps/grid fins, brillo acero, escala del OLM | `VesselRenderer.cs`, `LaunchPadController.cs`, `CameraController.cs` | Stack 9 m / ~121 m reconocible; detalles legibles sin ruido ni plastico blanco |
| Liftoff | IFT liftoff daylight, 33 Raptors + deluge | `/tmp/exosphere_liftoff_*.png` | Pluma merged, nube horizontal, exposicion, tower clear | `PlumeSystem.cs`, `LaunchEffectsController.cs`, `LaunchPadController.cs` | Pluma brillante/ancha, deluge horizontal, nave no oculta, HUD legible |
| Startup/ramp | Engine chill/startup T-3s a liftoff | `/tmp/exosphere_startup_*.png` | Preburn, flare progresivo, anillos encendiendo, vapor antes de release | `PlumeSystem.cs`, `LaunchEffectsController.cs`, `SimulationBridge.cs` solo si hace falta exponer estado | Secuencia no salta de apagado a full plume; hay progreso visual durante hold-down |
| Hot-staging | IFT hot-stage frames T+2:39/T+2:40 | `/tmp/exosphere_hotstage_*.png` | Flash/plume entre etapas, soot ring, separacion Ship/Booster | `HotStageFlashController.cs`, `VesselRenderer.cs`, `PlumeSystem.cs` | Un frame estatico permite entender que Starship encendio antes de separarse |
| Orbit burn | Upper-stage / Raptor vacuum plume references | `/tmp/exosphere_orbit_*.png` | Pluma larga, azul/blanca, opticamente delgada, sin humo denso | `PlumeSystem.cs`, `raptor_plume.gdshader` | Vac plume visible contra Tierra sin parecer pluma SL |
| Reentry nominal | Starship reentry / Shuttle reentry analogs | `/tmp/exosphere_reentry_nominal_*.png` | Shock windward, wake, leading edges, tiles protegidas | `ReentryPlasmaController.cs`, `VesselRenderer.cs` | Belly-flop nominal se ve protegido y controlado; plasma no tapa UI/cockpit |
| Reentry fallo | Starship Flight 4-6 flap/tile damage references | `/tmp/exosphere_reentry_bad_attitude_*.png` | Flujo pegando fuera del escudo, flap/nose heating localizado | `ReentryPlasmaController.cs`, `VesselRenderer.cs` | Mala orientacion se ve peligrosa antes de destruirse |
| Touchdown/flip | Starship flip/landing footage | `/tmp/exosphere_touchdown_*.png` | Flip burn, plume-ground interaction, encuadre vertical | `EDLController.cs`, `CameraController.cs`, `PlumeSystem.cs` | Ship completa en cuadro, pluma legible, touchdown readable |
| Orbit/map beauty | Tierra/terminador/vacuum lighting | `/tmp/exosphere_orbit_map_*.png` | Terminador, brillo de acero, sky/atmosfera, escala | `PlanetMaterials.cs`, `SkyController.cs`, `SunController.cs` | Nave, planeta y UI se leen sin clipping ni terminador inconsistente |

Reglas:
- Guardar referencias como links/notas, no assets pesados, salvo permiso claro.
- Capturar antes/despues con misma resolucion, fase, camara y hora visual cuando
  sea posible.
- Separar tres estados: `implementado`, `verificado por screenshot`,
  `comparado contra referencia`.
- No marcar un item como cerrado solo por existir codigo.

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
- [x] Grid fins primera pasada: 4 fins, mount/pivote, lattice visual y sombreado basico.
- [x] Grid fins close-up V1: placa trapezoidal con grosor, hinge drum, marco,
  ribs densos y diagonales; menos rectangular que la primera pasada. Validado con
  captura Xvfb `/tmp/exosphere_gridfin_closeup.png`. Pendiente comparar proporciones
  finas contra referencia real.
- [x] Flaps con base/hinge mas legibles y offset realista.
- [x] Engine bay con 33 motores visuales mas creibles: outer ring, inner cluster,
  mounts, dark cavities.
- [x] Super Heavy separado: hot-stage ring, vents, soot, scorched top and aft skirt.
- [x] Primera pasada de payload-door cues, seams longitudinales y aft shield/skirt.
- [x] Close-up cues Starship V1: access panels discretos, vent/drain ports,
  serial-style bars no intrusivos, leading edges y tile seams en flaps. Validado
  con captura Xvfb `/tmp/exosphere_ship_closeup.png`.

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
- [x] Pluma vacio limpia: `PlumeSystem` atenúa/apaga partículas de humo y soot cuando
  `expansion` es alta, especialmente en Starship; el shader sube levemente
  `vacuumDim`/`vacuumAlpha` para mantener un core azul/blanco legible contra Tierra
  sin volver a una nube de pad. Validado con captura sintética
  `/tmp/exosphere_orbit_plume_clean.png`.
- [x] Startup/ramp: transicion visible desde ignicion a liftoff. `EngineStartupController`
  agrega glow/flicker, vapor de chill y flecks de ignicion mientras la nave sigue
  ground-held; verificado con trigger local Xvfb en `/tmp/exosphere_startup_00..33.png`.
  Pendiente: comparar intensidad/timing contra referencias reales y ajustar solo si se ve teatral.
- [x] Hot-staging VFX implementado en codigo: al staging `SimulationBridge.TriggerStaging`
  reconstruye la Ship, spawnea el debris de Super Heavy y emite `VesselStaged`;
  `HotStageFlashController` escucha esa senal y dispara flash/luz/anillo de choque/plume corto/hollin.
  Validacion local: trigger forzado con harness temporal bajo Xvfb produjo multiframe
  `/tmp/exosphere_hotstage_after_00..11.png`; se ve flash inicial y fade a humo/hollin. Pendiente:
  captura en ascenso real y ajuste fino de encuadre/posicion contra referencia.
- [x] Hot-staging comparado contra referencia: captura multiframe en ascenso real `[G]`
  bajo xvfb (`/tmp/exosphere_hotstage_ascent_00..13.png`, jul 2026). Criterio cumplido:
  flash/pluma entre etapas, ring chamuscado en booster, Ship encendida antes de separarse,
  HUD legible. Ver `.atl/agent-hotstaging-log.md`. Pendiente: comparacion lado-a-lado con
  frame IFT T+2:39 para afinar intensidad del flash.
- [x] Ground cloud: vapor/polvo horizontal con blast radial y 5 capas N5.
- [ ] Validar en capturas si el deluge cloud no tapa en exceso la silueta durante
  liftoff lateral y no queda flotando al alejarse el pad.
- [x] Pad: OLM mas reconocible, flame trench/deflector mas legible, escala humana
  opcional si no distrae.

Aceptacion:
- Liftoff y hot-staging son legibles en una captura estatica.
- El pad no tapa la nave ni oculta el estado de vuelo.
- La pluma cambia visualmente entre SL, upper stage y vacio.

## V3 — Reentry Visual ✅ PARCIAL

Archivos probables:
- `scripts/ReentryPlasmaController.cs`
- `scripts/ReentryBreakupController.cs`
- `scripts/VesselRenderer.cs`
- `ExosphereSimulation/Physics/ThermalModel.cs` solo si hace falta exponer datos
  ya existentes; no retunear fisica sin tests.

Hecho:
- [x] Plasma ligado al heat flux real (`ThermalModel.ComputeHeatFlux`), densidad
  y velocidad.
- [x] Shock cap orientado a la cara windward usando `ThermalModel.WindwardFactor`.
- [x] Wake ionizado tenue durante heating.
- [x] Glow localizado primera pasada: nariz, belly center y leading edges de flaps
  usan el mismo heat flux/windward y siguen `vessel.Orientation` aunque el plasma
  sea sibling del renderer. Validado con captura sintética Xvfb
  `/tmp/exosphere_reentry_edges.png`; pendiente captura de EDL nominal/fallo real.
- [x] Charring progresivo de tiles por `Part.ThermalDamage`/temperatura.
- [x] Breakup VFX cuando ocurre destruccion termica.
- [x] Entrada mal orientada se ve mas roja/extendida que belly-first nominal.

Pendiente:
- [ ] Afinar shock/plasma localizado con capturas reales de EDL: tamano, alpha,
  color y timing en nose, leading edges y flap edges.
- [ ] Charring por zonas: nose/flaps/belly no deben degradarse todos al mismo ritmo.
- [ ] Capturas comparativas belly-flop nominal vs mala orientacion.
- [ ] Verificar que plasma/wake no ocultan HUD, cockpit ni map view.
- [ ] Afinar color/alpha por fase: inicio de plasma, peak heating, salida de heating.

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

> HALLAZGO (sesion jul 2026, ver `PLAN_PLAYTEST.md` B1): probar tonemap/glow GLOBAL
> NO funciona. ACES + `tonemap_white` oscurece la nave en orbita (subexpuesta), y el
> glow-only no rinde en pad/orbita/liftoff porque no hay hotspots HDR. La causa es que
> la luz es global: el ambient azulado de cielo (0.55,0.70,1.0 @0.45) esta bien en el
> pad pero MAL en el espacio. La solucion es un `PhaseLightingController` por fase,
> verificado con el harness de playtest. NO commitear cambios globales de env a ciegas.
>
> HECHO (V1): `scripts/PhaseLightingController.cs` mezcla la iluminacion por ALTITUD
> (suave, 70->130 km): baja el ambient (0.45->0.12), sube la energia solar (1.5->1.95)
> y rampa el glow HDR (0->0.6) al entrar al espacio; mantiene Filmic. `SunController`
> sigue duenio de la ORIENTACION de la luz (no toca energia) => sin conflicto. Validado
> con captura Xvfb: pad IDENTICO al baseline, orbita con contraste/pop metalico sin
> subexponer la nave ni lavar la Tierra. PENDIENTE: fase de reentry (exposicion calida
> del plasma) y cockpit; hoy reentry usa el look atmosferico (s=0), que es correcto.

Mejoras:
- Exposicion y color por fase: pad, ascent, space (HECHO por altitud), reentry, cockpit.
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
2. V0.5 auditoria con referencias reales para cada fase antes de tocar mas VFX.
3. V1 materiales/superficie Starship. Parcialmente cerrado; grid fins close-up V1
   y close-up cues Starship V1 implementados. Falta comparacion fina de
   nariz/flaps/tiles/markings contra referencia.
4. V2 plumas. ✅ Pluma SL/ascenso (brillo+ancho), ✅ pluma de vacio (legibilidad),
   ✅ hot-staging VFX implementado y verificado con trigger local multiframe,
   ✅ hot-staging capturado en ascenso real `[G]` (jul 2026, ver `.atl/agent-hotstaging-log.md`),
   ✅ smoke/soot de pluma vacio atenuado. Falta: comparacion fina hot-staging vs frame IFT,
   comparacion fina de startup/ramp y captura/reference de pluma de vacio limpia.
5. V3 reentry plasma/charring localizado. ✅ primera pasada de glows localizados
   en nose/belly/flaps; falta comparativa nominal/fallo y charring por zonas.
6. V4 camara/luz/atmosfera.
7. V5 capturas automatizadas en CI (cablear el xvfb capture de V0).

## Bitacora Para El Proximo Agente (que se hizo y como continuar)

Sesion de fidelidad visual (jun 2026). Contexto para retomar sin re-derivar:

**Hecho y verificado por screenshots reales (xvfb):**
- **Captura real funcionando** (`xvfb-run` + autoload temporal `_CaptureShot.cs`). Ver bloque "V0"
  arriba y la memoria `visual-capture-xvfb`. Esto es LA herramienta para todo cambio visual:
  cambio -> build -> capturar -> mirar el PNG con la tool Read -> comparar. Igual que el tuning de
  la EDL en R13. El `--headless` NO sirve (dummy renderer).
- **Pluma SL/ascenso** mas brillante y ancha (`scripts/PlumeSystem.cs`, commit `390a7ce`): mouths del
  core/anillos mas anchos + `energy` del shader SH 3.0->4.6 / Ship 3.4->4.0. Antes era humo gris fino.
- **Pluma de vacio** legible (`assets/shaders/raptor_plume.gdshader`, commits `4d971ad` + actual):
  `vacuumDim`/`vacuumAlpha` un poco mas altos. El shader es un asset deliberado y bien hecho — NO
  reescribir; tunear con cuidado.
- **Pluma de vacio limpia** (`scripts/PlumeSystem.cs`): el smoke/soot se apaga con
  `expansion` alta (`smokePresence`), dejando el shader-core azul/blanco como capa dominante.
  Si la pluma orbital se ve sucia, ajustar ese factor; no tocar la nube N5 del pad.
- El **ground cloud** (`LaunchEffectsController.cs`) ya es una nube de deluge de 5 capas muy iterada
  ("N5"); las capturas confirman que es fuerte. **No tocar a ciegas.**

**Implementado, pendiente de comparar contra referencia:**
- **Hot-staging VFX**: `SimulationBridge.TriggerStaging` separa Ship/Booster y emite
  `VesselStaged`; `HotStageFlashController` agrega flash/luz/anillo/plume/hollin, y
  `VesselRenderer` muestra Super Heavy separado con hot-stage ring expuesto, vents y
  scorch/labio quemado. Validado con trigger local multiframe (`/tmp/exosphere_hotstage_after_*.png`).
  Falta capturar el evento dentro del ascenso real y comparar contra frames IFT T+2:39/T+2:40.
- **Startup/ramp VFX**: `EngineStartupController` agrega pre-release engine glow, vapor y
  flicker en el mount mientras `IsGroundHeld` y throttle sube. Validado con trigger local
  multiframe (`/tmp/exosphere_startup_*.png`); falta comparar contra startup real y ajustar timing.
- **Reentry localized glow V1**: `ReentryPlasmaController` ya no asume nave vertical para
  cap/wake; aplica `vessel.Orientation` al centro de plasma y a glows de nariz, belly y flaps.
  Validado con captura sintética `/tmp/exosphere_reentry_edges.png`; falta barrido real de EDL.
- **Grid fins close-up V1**: `VesselRenderer.AddSHGridFins` usa placa trapezoidal,
  hinge drum, marco/ribs/diagonales y cant leve. Validado con
  `/tmp/exosphere_gridfin_closeup.png`; falta comparacion fina contra referencias Starbase/IFT.
- **Starship close-up cues V1**: `VesselRenderer` agrega access panels, vents,
  serial-style bars, leading edges y seams en flaps. Validado con
  `/tmp/exosphere_ship_closeup.png`; falta comparacion fina contra referencias reales.

**Como tunear plumas (mapa rapido):**
- Tamaño/colores/brillo por anillo: `PlumeSystem.SetupSH` / `SetupStarship` (mouthR, length, core).
- Brillo maestro + diamantes: uniforme `energy` y `diamond_count` en `PlumeSystem.BuildUnit`.
- Forma/opacidad/color por presion (SL vs vacio): el fragment de `raptor_plume.gdshader`
  (`effExpansion`, `reach`, `vacuumDim`, `vacuumAlpha`, `eScale`). SL = corto/brillante/diamantes;
  vacio = largo/tenue/sin diamantes (a proposito).
- Ground cloud (deluge): `LaunchEffectsController.cs`.

**Proximo paso mas valioso:** screenshot sweep de hot-staging en ascenso real con framebuffer
real y captura multiframe; despues comparar hot-staging/startup contra referencia IFT y ajustar
solo lo observable. Luego V3 (reentry plasma localizado ligado al heat flux real, que YA esta
en el sim como `WorstHeatRatio`/`Part.ThermalDamage`).
