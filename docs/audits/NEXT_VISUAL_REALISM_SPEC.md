# Next Visual Realism Specification

**Date:** 2026-08-20
**Status:** In progress after `03d65c3`; daylight framebuffer review remains open.

## Goal

Push Exosphere from "recognizable Starship simulator" toward "credible real-world
flight footage": Starbase daylight launch, hot-staging, orbital Earth, EDL plasma,
catch/landing, cockpit and VAB must each have reference-backed visual acceptance.

This is not a request for more random detail. Each item below needs:

- a real reference target,
- a deterministic capture,
- a code owner,
- a measurable gate,
- and one human screenshot review.

## Sources Consulted

- SpaceX Starbase launch page: `https://www.spacex.com/launches/starbase`
- SpaceX Starship Flight 12 page: `https://www.spacex.com/launches/starship-flight-12`
- SpaceX Starship Flight 11 page: `https://www.spacex.com/launches/starship-flight-11`
- FAA Boca Chica Starship/Super Heavy archive:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship/activity_archive`
- FAA Starship/Super Heavy vehicle summary:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship/starship_super_heavy`
- FAA LC-39A Starship/Super Heavy EIS:
  `https://www.faa.gov/space/stakeholder_engagement/spacex_starship_ksc`
- NASA Earth at Night:
  `https://science.nasa.gov/earth/earth-observatory/earth-at-night/`
- NASA airglow explainer:
  `https://www.nasa.gov/solar-system/why-nasa-watches-airglow-the-colors-of-the-upper-atmospheric-wind/`
- NASA Earth limb / atmosphere visual reference:
  `https://svs.gsfc.nasa.gov/11901`

## Current Baseline

Validated locally:

- `dotnet build Exosphere.csproj --nologo -v quiet` -> 0 warnings / 0 errors.
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`
  -> 703/703 pass.
- `bash tools/ci_check.sh` -> pass.
- `bash tools/visual_playtest.sh --launch --run-id pad-tower-v11-launch2 --skip-build`
  -> `LAUNCH_OK`, pad/liftoff PNGs verified.
- `bash tools/visual_playtest.sh --atmosphere-bodies --run-id mars-venus-framebuffer-v2
  --skip-build --max-runtime 420` -> `ATMOSPHERE_BODIES_OK`, 6/6 PNG reales a
  1920×1080 en llvmpipe.

The launch-night baseline also exposed a presentation issue: the complex geometry was
present, but all four work lights aimed at the OLM centre, making the tower readable while
the tank farm and service apron collapsed into a black silhouette. The bounded fix keeps
four shadow maps, lowers each pool from 42 to 30 energy units, widens the cone/range to
50°/170 m, and targets four work sectors. This is a presentation-only change; it does not
alter solar phase, launch-site coordinates, physics, or the daylight path. The harness now
records `VISUAL_LAUNCH` component counts for pad/liftoff so a future dark capture cannot be
mistaken for missing OLM, deluge, tanks, or chopsticks.

### Mars/Venus framebuffer review — 2026-08-20

La matriz real quedó en `/tmp/exo_play-mars-venus-framebuffer-v2/` y su log en
`/tmp/exo_play-mars-venus-framebuffer-v2.log`. Las seis capturas terminaron con identidad
de cuerpo correcta y `exposureSettled=True`. Mars a 10 km conserva el relieve procedural
local; a 400 km vuelve al mapa orbital. Venus conserva la cubierta nubosa de baja
frecuencia y la lectura diferenciada de día/noche. No se promovió ningún ajuste de shader:
el clipping de superficie fue 0 en los cuatro casos de baja altitud, no hubo fracción de
verde neón y los estados solares fueron coherentes (`solarVisibility=1` de día, `0` de
noche). El relieve de Mars sigue siendo una reconstrucción procedural y no topografía
medida.

La matriz inicialmente quedó parcial por un límite externo de 180 s debido al precálculo
de Venus en llvmpipe (~55 s por LUT); se repitió con un presupuesto de 420 s y terminó
correctamente. El harness temporal también dejó de emitir `CS0162`: las banderas de
override visual son `static readonly`, para que los modos sin override compilen la rama
compartida sin código inalcanzable.

The engine HUD audit reaches a similar conclusion for the original screenshot: the intended
visual vocabulary is `track` for off, amber for startup, white for delivered thrust, and red
only for a failure. The left engine grid already followed that rule, but the right attitude
strip snapshot still derived its count from raw rows. It now uses the shared delivered/failure
classifier and preserves declared aggregate counts (for example, a three-selected cluster
still reports `3/6`, not `1/1`). A command throttle of 100% during chill/ignition may still
show zero delivered engines for the short startup interval; that is intentional and is now
auditable rather than being represented as red failure dots.
Pad/liftoff captures now also emit `VISUAL_ENGINES` with command throttle, declared rows,
delivered, starting, and failed counts so a screenshot can be diagnosed without guessing
from color alone.

Important limitations:

- Launch/catch captures still happen in dark/twilight conditions too often for
  fine structural review.
- Starbase tower V1.1 loads and is functional, but not yet compared side-by-side
  against daylight Starbase footage.
- Reentry visuals have shock/plasma/charring, but the "real footage" contrast,
  flow direction and heating progression still need a tighter acceptance pass.
- Earth/atmosphere is better, but still reconstructed, not photogrammetric.

## P0 — Daylight Reference Capture Matrix

### Why

The project has good runtime gates, but too many visual claims still rely on dark
or obstructed screenshots. Realism work cannot keep advancing from low-contrast
evidence.

### Implementation

- Add deterministic sun/time controls to `tools/visual_playtest.sh`:
  - `--sun-elevation DEG` for pad/launch/catch/ship/orbit modes.
  - `--camera-preset pad_side|tower_side|tracking|orbit_beauty|edl_side`.
  - write `VISUAL_SUN elevationDeg=... phase=...` and `VISUAL_CAMERA preset=...`.
- Default visual acceptance captures should use daylight unless a night case is
  explicitly being tested.
- Store run output in `/tmp/exo_visual_<topic>_<id>/`.

### Files

- `tools/visual_playtest.sh`
- `scripts/SunController.cs`
- `scripts/CameraController.cs`
- `scripts/SkyController.cs`

### Acceptance

- `--launch --sun-elevation 35 --camera-preset tower_side` captures pad/liftoff
  with tower, stack and plume readable.
- `--hotstage --sun-elevation 35` captures hot-stage overlap and separation.
- `--edl --sun-elevation 25 --camera-preset edl_side` captures entry, retro burn
  and caught/landing with vehicle silhouette readable.
- Add contract: visual modes that claim reference acceptance must log sun and
  camera preset.

## P1 — Starbase Pad 2 / Tower Fidelity

### Reference Target

FAA/SpaceX public material confirms Boca Chica Starship/Super Heavy operations,
modern deluge/water system context and launch-site infrastructure. Recent public
Starship flights also moved into V3/Pad 2 era. The in-game Starbase should read as
post-deluge Starbase, not a generic launch tower.

### Implementation

- Extend `LaunchComplexSpec` with Pad 1 vs Pad 2 visual profiles.
- Add:
  - Pad 2 civil footprint option,
  - wider catch-arm carriage housing,
  - visible SQD/BQD umbilical heads with hoses,
  - deluge plate/nozzle field detail visible at pad distance,
  - catch-arm inner pads with distinct material from structural steel,
  - service platforms at Ship QD and carriage levels.
- Keep it procedural. Do not introduce heavy mesh assets until the procedural
  silhouette fails a side-by-side review.

### Files

- `scripts/LaunchPadController.cs`
- `docs/audits/STARBASE_RECONSTRUCTION_V1.md`
- `PLAN_VISUAL_REALISM.md`

### Acceptance

- Daylight side capture identifies OLM, OLIT, chopsticks, SQD/BQD, deluge deck
  and tank farm without zooming into source code.
- `launch_pad_performance_contract_test.sh` still passes.
- No pad geometry appears in orbital ship captures unless a catch approach is
  active.

## P1 — Hot-Staging Reference Pass

### Reference Target

SpaceX Flight 11 and Flight 12 timelines list hot-staging shortly after MECO,
with Ship ignition and stage separation occurring within seconds. In-game visuals
must make this event readable in one frame and correct across a short sequence.

### Implementation

- Use `--hotstage` as the acceptance harness, not synthetic staging only.
- Capture at least:
  - `hotstage_pre`,
  - `hotstage_overlap`,
  - `hotstage_separation`,
  - `booster_flip`.
- Tune:
  - interstage flash duration,
  - plume origin and scale,
  - soot/haze around hot-stage ring,
  - exposure so Ship and Booster do not vanish into black.

### Files

- `scripts/HotStageFlashController.cs`
- `scripts/PlumeSystem.cs`
- `scripts/VesselRenderer.cs`
- `tools/visual_playtest.sh`

### Acceptance

- A static `hotstage_overlap` screenshot clearly shows Ship thrust before full
  separation.
- Log includes finite vehicle states, engine counts and `IsHotStageOverlapping`.
- No pad smoke style appears on vacuum/upper-stage plume.

## P1 — Reentry Plasma And Thermal Damage V2

### Reference Target

SpaceX flight writeups emphasize heatshield performance, structural stress, flap
limits, dynamic banking and guided flap-controlled descent. NASA reentry/airglow
references help distinguish atmospheric glow/limb effects from vehicle plasma.

### Implementation

- Separate visual regimes:
  - upper atmosphere faint shock layer,
  - peak heating windward plasma,
  - post-peak wake thinning,
  - retro burn plume interaction,
  - final catch/landing dust/steam.
- Add per-zone thermal presentation:
  - nose,
  - windward belly,
  - forward flaps,
  - aft flaps,
  - leeward stainless body.
- Drive color and alpha from heat flux, density and local flow incidence.
- Keep structural failure physics separate from visual charring.

### Files

- `scripts/ReentryPlasmaController.cs`
- `scripts/ReentryBreakupController.cs`
- `scripts/VesselRenderer.cs`
- `scripts/EDLController.cs`
- `tools/visual_playtest.sh`

### Acceptance

- Nominal belly-first: windward plasma and tile glow, leeward side mostly readable.
- Bad attitude: nose/flap off-axis heating clearly stronger before failure.
- `--reentry-compare` produces nominal/bad-attitude PNGs with non-overlapping
  thermal signatures.
- HUD remains legible during peak heating.

## P1 — Orbital Earth / Night / Airglow Pass

### Reference Target

NASA Earth-at-night and airglow references show that orbital night is not pure
black: city lights, airglow and limb scattering remain visible, while the surface
should not become a flat bright texture.

### Implementation

- Add a thin airglow shell or sky/planet limb term distinct from Rayleigh daytime
  atmosphere.
- Add optional low-resolution night-light texture path using NASA Black Marble
  derived assets only if licensing and asset size are acceptable.
- Improve exposure adaptation so:
  - daylight Earth does not clip,
  - night Earth has city/airglow cues,
  - stars remain visible without overpowering the planet.

### Files

- `assets/shaders/planet_body.gdshader`
- `assets/shaders/earth_surface.gdshader`
- `assets/shaders/space_sky.gdshader`
- `scripts/PlanetMaterials.cs`
- `scripts/SkyController.cs`
- `scripts/SunController.cs`

### Acceptance

- `--orbit --sun-elevation -35` shows a readable night limb without broad white
  clipping.
- `--atmosphere-ground` keeps sunrise/sunset monotonic and no negative radiance.
- `space_sky_banding_contract_test.sh` and `planet_body_lighting_contract_test.sh`
  pass.

## P2 — Real Camera Language

### Implementation

- Add capture presets matching real footage:
  - long-lens pad side,
  - tracking ascent,
  - staging telephoto,
  - orbital chase,
  - EDL ground/telemetry view,
  - cockpit handheld/seat vibration.
- Add camera metadata logging:
  - FOV,
  - target,
  - distance,
  - mode,
  - sun elevation.

### Acceptance

- Captures are comparable across runs without manual camera guesswork.
- No UI panel hides the exact structure being reviewed.

## P2 — VAB And Mission Presentation

### Implementation

- Make VAB lighting/materials match the flight renderer: stainless steel, tile
  side, flaps, grid fins and engine bells should be recognizable in the preview.
- Add multi-select/gizmo screenshots to acceptance.
- Improve main-menu/mission briefing art so the first frame signals the actual
  playable simulator, not a generic menu.

### Acceptance

- `tools/capture_vab.gd` or an equivalent harness captures empty VAB, selected
  stack and invalid attachment state.
- `vab_preview_lighting_contract_test.sh` and `vab_picking_alignment_contract_test.sh`
  pass.

## Anti-Goals

- Do not replace physics with visual shortcuts.
- Do not add heavy external assets unless the asset has clear licensing, size
  budget and a visible quality win.
- Do not claim realism from static contracts alone; contracts protect regressions,
  screenshots prove presentation.
- Do not tune night exposure to make one screenshot pretty while breaking orbital
  darkness, stars or cockpit readability.

## Recommended Next Commit Sequence

1. `test(visual): add deterministic daylight capture presets`
2. `polish(pad): refine Starbase tower and deluge fidelity`
3. `polish(staging): match hot-staging reference sequence`
4. `polish(reentry): add thermal zone presentation v2`
5. `polish(orbit): add airglow and night Earth cues`
6. `docs(visual): record next realism evidence matrix`

## Verification Gate

Before closing the next session:

```bash
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
dotnet build Exosphere.csproj --nologo -v quiet
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo
bash tools/ci_check.sh
bash tools/visual_playtest.sh --launch --run-id next-launch --skip-build
bash tools/visual_playtest.sh --hotstage --run-id next-hotstage --skip-build
bash tools/visual_playtest.sh --reentry-compare --run-id next-reentry --skip-build
bash tools/visual_playtest.sh --orbit --run-id next-orbit --skip-build
```

Manual review must inspect the PNGs, not just command exit codes.

## Fase actual — hot-stage: anclaje y evidencia (2026-08-20)

### Decisión

El VFX de hot-stage queda anclado al plano de separación de Flight 7 y se activa
al comenzar el solape de empuje, no al recibir la señal tardía de `VesselStaged`.
La señal tardía se conserva sólo como respaldo para rutas de staging no estándar.
La física, el consumo de propelente y la separación no se modifican.

### Defecto reproducido

La corrida previa `/tmp/exo_hotstage_anchor_compile_v4.log` registró en el hito de
solape:

```text
VISUAL_HOTSTAGE slug=hotstage visible=False frameSynced=False interfaceY=25.36
```

El mismo proceso sólo mostraba el efecto en `hotstage_separation`. El origen antiguo
estaba en `y≈0`, junto a las campanas del booster, aunque la geometría del stack
sitúa la interfaz Ship/Super Heavy en `71 m / 2.8 = 25.36` unidades de render.

### Corrección y prueba

- `HotStageFlashController` usa `HotStageInterfaceRenderY = 25.36` para pluma,
  anillo, hollín y luz.
- El root replica posición y orientación de `ActiveVesselRenderer`, respetando
  FloatingOrigin durante pitch/yaw.
- La transición autoritativa `IsHotStageOverlapping: false → true` inicia el burst.
- `tools/visual_playtest.sh --hotstage` produce `hotstage` y
  `hotstage_separation`, con telemetría espacial y estado físico separado.
- La repetición `/tmp/exo_hotstage_anchor_compile_v5.log` confirmó para ambos hitos:

```text
VISUAL_HOTSTAGE slug=hotstage visible=True frameSynced=True interfaceY=25.36
VISUAL_HOTSTAGE slug=hotstage_separation visible=True frameSynced=True interfaceY=25.36
SUMMARY reason=HOTSTAGE_OK
```

Esta repetición fue `--headless`: demuestra compilación, orden temporal y anclaje
de nodos, pero no demuestra calidad de píxel.

### Estado de aceptación

| Gate | Resultado | Evidencia |
|---|---|---|
| Anclaje al plano de separación | PASS | `hotstage_visual_anchor_contract_test.sh` |
| Activación durante solape real | PASS | `compile_v5.log`, `visible=True`, `frameSynced=True` |
| Captura post-separación | PASS lógico | `hotstage_separation`, `SUMMARY=HOTSTAGE_OK` |
| PNG daylight overlap/separation | PENDIENTE | framebuffer X11 no disponible en esta VM |
| Comparación de exposición y lectura de ambos vehículos | PENDIENTE | requiere captura real |

El bloqueo de framebuffer está registrado en `/tmp/exo_visual_daylight_preset_v1/run-summary.txt`:
Godot no pudo abrir X11 ni Wayland (`X11 Display is not available`). Por rigor, la
fase no se marca como visualmente cerrada hasta repetirla con PNG reales.

### Publicación y regresión

El work unit está publicado como `f0c4656` en `origin/main`. Antes del push pasaron:

- `bash tools/ci_check.sh`;
- build de simulación y juego con 0 warnings/0 errores;
- `703/703` tests xUnit;
- startup quick check y smoke Godot;
- limpieza del autoload temporal y `git diff --check`.

### Siguiente captura obligatoria

```bash
bash tools/visual_playtest.sh --hotstage --flight7 \
  --sun-elevation 35 --camera-preset tower_side \
  --run-id next-hotstage-daylight
```

La revisión humana debe comprobar simultáneamente: Ship encendido sobre el booster,
pluma localizada en el interstage, ausencia de humo de pad en vacío, separación visible,
sin clipping amplio y sin que el HUD tape el plano de staging. Después se repite la
misma matriz para EDL y órbita antes de cerrar P1.

## Fase actual — reentrada: respuesta térmica de baja intensidad (2026-08-20)

### Hallazgo medido

Las capturas históricas de `/tmp/exo_visual_edl_yaw0_v1/` mostraban un `peak_heating`
con el casco casi negro y sólo una brasa roja. La causa no era que el plasma estuviera
desconectado: el controlador recibía un flujo finito, pero el rango lineal reservaba
demasiado contraste para la zona cercana a saturación.

La corrida de diagnóstico `/tmp/exo_reentry_telemetry_v1.log` registró:

```text
VISUAL_REENTRY slug=peak_heating coreVisible=True flux=4.491E+004 fluxIntensity=0.072 visualIntensity=0.072 shockHeat=0.072
```

### Corrección

`ReentryPlasmaController` aplica una respuesta perceptual acotada
`pow(fluxIntensity, 0.65)` antes del perfil de fase. Es exclusivamente presentación:
el flujo Sutton-Graves, temperatura, daño estructural, guidance y supervivencia no
cambian. El umbral mantiene entrada cero y la saturación mantiene salida uno.

### Resultado reproducible

La repetición `/tmp/exo_reentry_telemetry_v2.log` registró en la misma matriz:

```text
VISUAL_REENTRY slug=entry phase=ENTRY flux=4.919E+003 fluxIntensity=0.000 visualFluxInput=0.000 visualIntensity=0.000 shockHeat=0.000
VISUAL_REENTRY slug=peak_heating phase=AERO_DESCENT flux=4.547E+004 fluxIntensity=0.074 visualFluxInput=0.185 visualIntensity=0.181 shockHeat=0.176
VISUAL_REENTRY slug=retro_burn phase=RETRO_BURN flux=8.849E+001 fluxIntensity=0.000 visualFluxInput=0.000 visualIntensity=0.000 shockHeat=0.000
SUMMARY reason=CAUGHT
```

La comparación muestra un aumento de lectura térmica en el régimen visible bajo sin
crear plasma por debajo del umbral. La ejecución fue headless: prueba orden temporal,
valores finitos y cierre físico `CAUGHT`, pero no sustituye la inspección de PNG.

### Estado de aceptación

| Gate | Resultado | Evidencia |
|---|---|---|
| Flujo térmico finito y derivado del vehículo | PASS | `VISUAL_REENTRY`, `ComputeStagnationHeatFlux` |
| Plasma apagado bajo umbral | PASS | `entry`, `flux=4.919E+003`, `coreVisible=False` |
| Plasma legible en régimen bajo visible | PASS lógico | `peak_heating`, `shockHeat=0.176` |
| Catch físico sin regresión | PASS | `SUMMARY reason=CAUGHT`, 2 contactos en el log |
| PNG peak-heating nominal | PENDIENTE | framebuffer X11 no disponible |
| Comparación nominal/bad-attitude en píxeles | PENDIENTE | requiere `--reentry-compare` con framebuffer real |

### Siguiente revisión visual

```bash
bash tools/visual_playtest.sh --edl --sun-elevation 25 \
  --camera-preset edl_side --run-id next-edl-daylight
bash tools/visual_playtest.sh --reentry-compare \
  --sun-elevation 25 --camera-preset edl_side --run-id next-reentry-compare
```

La revisión humana debe confirmar que el shock windward ocupa la zona correcta del
casco, que el lado de sotavento sigue legible, que el plasma no se convierte en un
halo uniforme y que el HUD no queda lavado. Si el PNG muestra sobreexposición, se
ajusta el material/shader con una nueva captura; no se revertirá a una ganancia global
que oculte la relación entre flujo y VFX.

## Fase actual — órbita nocturna: observabilidad antes de calibración (2026-08-20)

### Evidencia visual disponible

Las capturas reales ya existentes (`/tmp/exo_visual_ship_v7/exo_play_ship_vacuum.png`,
`/tmp/exo_visual_atmosphere_v2/exo_play_120km_night.png` y
`/tmp/exo_visual_atmosphere_v2/exo_play_400km_night.png`) muestran una Tierra azul
oscura y un limbo legible, pero no permiten afirmar que las ciudades sean visibles en
ese encuadre: la cámara mira hacia la zona de terminador y el mapa nocturno queda por
debajo del umbral de lectura. No se cambia el gain del mapa con esa sola observación,
porque una ganancia global podría convertir el lado nocturno en una superficie plana y
romper la adaptación de exposición.

El material actual ya tiene los tres términos esperados y acotados:

- `earth_night.jpg` para ciudades;
- `night_floor`/earthshine para que el disco no desaparezca;
- `limb_strength` y el airglow del cielo para el borde atmosférico.

### Diagnóstico reproducible

El nuevo work unit `af6f14e` conserva telemetría geométrica también en la ruta dummy.
La corrida `/tmp/exo_play-orbit-night-observable.log` registró:

```text
VISUAL_SUN override=True elevationDeg=-35.00 phase=NIGHT physicalSunPositionUnchanged=True
VISUAL_CAMERA preset=orbit_beauty yawDeg=0.00 pitchDeg=45.00 distance=400000.00 fov=75.00 mode=Chase
VISUAL_PLANET body=earth slug=orbit_direct visible=True cameraDistanceM=7405432.3 angularDiameterDeg=118.7038 cameraForwardCos=0.77867
CAPTURE orbit_direct path=headless://orbit_direct alt=200000.0 spd=7400.4 vSpeed=-0.0 apo=200000.0 pe=200000.0
SUMMARY reason=ORBIT_DIRECT_OK frames=115
```

Esto descarta que el diagnóstico anterior fuera simplemente una Tierra fuera de
cuadro: el planeta queda visible, con un diámetro angular amplio y alineación de
cámara válida. La corrida no produce PNG porque esta VM no puede abrir X11/Wayland;
por tanto no se declara todavía una mejora de píxel para city lights o airglow.

### Decisión y gate pendiente

Se mantiene el shader sin cambio de energía hasta disponer de framebuffer real. La
siguiente captura debe repetirse en una máquina con X11 funcional:

```bash
bash tools/visual_playtest.sh --orbit --sun-elevation -35 \
  --camera-preset orbit_beauty --run-id next-orbit-night
```

La revisión debe comprobar: ciudad/airglow visibles sin halo blanco amplio, estrellas
conservadas, limbo azul fino, `surfaceWhiteClipFrac < 0.20`, sin `neonGreenFrac` amplio
y exposición estable. Sólo si esa comparación demuestra falta de lectura se ajustará
`night_lights` o el término de limbo, con un nuevo A/B day/night y sin tocar la física.

## Fase actual — VAB: escala de estudio y piso procedural (2026-08-20)

### Hallazgo visual

La captura histórica `/tmp/exo_visual_vab_selection_v1.png` mostró el Starship/Super
Heavy sobre un fondo negro transparente, sin contacto, escala ni sombra de estudio.
El vehículo se podía inspeccionar, pero el encuadre no permitía juzgar si flotaba, si
la base estaba alineada o si los materiales del casco y los tiles tenían una lectura
coherente. El defecto era de presentación del preview, no de `VesselRenderer` en Flight.

### Corrección publicada

`273b3f8` añade `PreviewFloor` sólo dentro del `SubViewport` de Construction:

- plano procedural de 180×180 unidades con paneles, juntas, desgaste macro y grano;
- shader aislado `assets/shaders/vab_floor.gdshader`, separado de la superficie de
  lanzamiento;
- posición derivada del mismo AABB renderizado que usa el auto-frame (`bottom - 0.08`),
  de modo que un craft custom o un Starship staged no quede flotando ni atraviese el
  piso;
- visibilidad y procesamiento siguen siendo demand-driven: el piso se apaga cuando
  el VAB está vacío y no cambia la física ni el coste de Flight.

### Estado de aceptación

| Gate | Resultado | Evidencia |
|---|---|---|
| Shader de piso aislado, juntas y grano | PASS | `vab_preview_lighting_contract_test.sh` |
| Anclaje al AABB real de la geometría | PASS | contrato + build 0/0 |
| VAB smoke y construcción | PASS | `vab_quick_check.sh`, 12 tests de construcción |
| PNG nuevo con piso visible y sombra de contacto | PENDIENTE | X11/Wayland no disponible en esta VM |
| Comparación de materiales Starship/tile sobre el piso | PENDIENTE | requiere framebuffer real |

Captura obligatoria en un entorno con X11 funcional:

```bash
CAPTURE_VAB_SCENARIO=selection \
CAPTURE_VAB_OUTPUT=/tmp/exosphere_vab_selection_floor.png \
Godot --path . --script tools/capture_vab.gd
```

La revisión debe comprobar contacto visual del motor con el piso, juntas que no
compitan con el vehículo, acero sin dominante dorada artificial, tiles distinguibles,
ausencia de clipping y que el VAB vacío conserve el mensaje “NO VEHICLE ON THE FLOOR”.

## Complejo de lanzamiento nocturno — ruta activa y campo de deluge (2026-08-20)

### Discrepancia corregida

La primera captura nocturna del complejo mostraba iluminación útil, pero la telemetría
del árbol activo registraba `delugeOutlets=0`. Existía un anillo de boquillas en un
constructor costero antiguo que no participa en `BuildEnvironment` para el Starbase
post-deluge actual. Por tanto, el problema era real: el pad tenía tuberías y vapor
visual, pero no tenía el campo explícito de boquillas en la ruta que usa el juego.

`1f550c4` añade en `BuildStarbaseCivilWorks` un anillo de 16 salidas nombradas
`DelugeOutlet0..15`, ancladas sobre la cota superior de `OlmFoundationMat`. No se
colocan en `GradeY`, porque quedarían enterradas dentro de la fundación. El cambio es
presentación local: no modifica caudal, empuje, masa ni la simulación de fluidos.

También se endureció el gate de `--launch`: una captura válida debe probar dos PNG
(`pad`, `liftoff`), `SUMMARY reason=LAUNCH_OK`, 4 floodlights, 16 outlets, 15 cuerpos
de tanques, 2 brazos de Mechazilla, 33 filas de motores y `delivered=33`, además de
clipping amplio y contaminación neón-verde por debajo del límite.

### Evidencia real reproducible

Artefactos:

```text
/tmp/exo_play-launch-night-deluge-v1/exo_play_pad.png
/tmp/exo_play-launch-night-deluge-v1/exo_play_liftoff.png
/tmp/exo_play-launch-night-deluge-v1.log
```

La corrida se ejecutó con framebuffer real llvmpipe, `--sun-elevation -35`,
`--camera-preset pad_side` y finalizó `LAUNCH_OK`. Las líneas estructurales fueron:

```text
VISUAL_LAUNCH slug=pad ... children=545 nightFloodlights=4 floodlightsActive=True delugeOutlets=16 tankBodies=15 chopsticks=2
VISUAL_LAUNCH slug=liftoff ... children=545 nightFloodlights=4 floodlightsActive=True delugeOutlets=16 tankBodies=15 chopsticks=2
VISUAL_ENGINES slug=liftoff ... nominal=33 rows=33 delivered=33 failed=0
```

Las métricas de imagen fueron `clippedFrac=0.00138/0.00144`,
`surfaceClippedFrac=0.00378/0.00463` y `neonGreenFrac=0.000000` en pad/liftoff.
La inspección visual confirma que el aprón, el tanqueado, el OLM, los brazos y la
pluma permanecen legibles en noche; el gate puede repetirse sin lanzar Godot con:

```bash
bash tools/visual_playtest.sh --launch \
  --run-id launch-night-deluge-v1 --verify-only
```

La decisión es conservar las cuatro luces sectorizadas con sombras acotadas y el
campo de 16 boquillas en la ruta activa. No se añadieron shadow maps adicionales ni
un fill global que aplane la noche.

## Matriz visual real — VAB, cabina, órbita y EDL (2026-08-20)

### VAB: vacío y Starship en piso de estudio

La captura real del VAB ya no queda sólo respaldada por contratos de código:

```text
/tmp/exosphere_vab_empty_floor-v1.png
/tmp/exosphere_vab_selection_floor-v1.png
```

El estado vacío conserva `NO VEHICLE ON THE FLOOR`. El estado Starship muestra el
vehículo apoyado sobre el plano procedural, juntas y grano visibles sin competir con
la silueta, y el panel lateral mantiene masa, propelente, TWR y `READY FOR FLIGHT`.
La base no queda flotando ni atraviesa el piso. La captura fue 1920×1080 con
framebuffer real; la prueba no cambia la escena de Flight ni la física.

### Cabina y órbita nocturna

Artefactos reales:

```text
/tmp/exo_play-visual-cockpit-final-v1/exo_play_cockpit.png
/tmp/exo_play-visual-orbit-night-final-v1/exo_play_orbit_direct.png
```

La cabina finalizó `COCKPIT_OK` a 118 km: los tres paneles tienen borde y contraste
legibles, el horizonte/espacio no lava el HUD y los valores quedan finitos
(`clippedFrac=0.00433`, `surfaceClippedFrac=0.01547`, `neonGreenFrac=0`). En órbita,
`VISUAL_PLANET` confirma Tierra visible con diámetro angular `118.7024°` y alineación
de cámara `cameraForwardCos=0.77872`; el limbo azul es continuo y las estrellas no
desaparecen (`sharpStarCount=78`). La imagen orbital tiene
`surfaceWhiteClipFrac=0` y `neonGreenFrac=0`.

El indicador orbital `ENG 0/6` es correcto en esta escena: el vehículo está en
coast con throttle `0%`; no representa una pérdida de motores. La semántica de
motor durante ignición/liftoff se valida separadamente con `33/33 delivered` en el
gate de lanzamiento.

### EDL y palillos chinos

La corrida real completa revalidada con el gate actual es:

```text
OUT_DIR=/tmp/exo_visual_edl_v4 \
LOG=/tmp/exo_visual_edl_v4.log \
bash tools/visual_playtest.sh --edl --run-id visual-audit-edl-v4 --verify-only
```

Resultado: `SUMMARY reason=CAUGHT`, `pins=2`, y cinco PNGs (`entry`,
`peak_heating`, `retro_burn`, `flip_complete`, `caught`). La inspección muestra el
shock térmico concentrado en el lado de avance, los tres motores en retro-burn y los
dos brazos de Mechazilla visibles junto al vehículo capturado. `clippedFrac` queda
entre `0.00087` y `0.00302` en las fases de descenso/catch; el catch no es una
animación inventada, porque el log prueba dos pines físicos asentados.

La repetición fresca con la revisión nocturna de esta fase no se declara como un
PASS artificial: `visual-edl-final-v1` llegó a 11.6 km en 300 s y
`visual-edl-final-v2` a 4.6 km en 720 s, ambos con progreso físico continuo pero sin
alcanzar `retro_burn` antes del límite de llvmpipe. Se conserva esa evidencia parcial
para diagnosticar coste de simulación/framebuffer; el artefacto completo anterior se
acepta porque el gate lo reevalúa sin relanzar Godot y el cambio posterior sólo toca
la geometría nocturna del pad.

### Decisión de esta pasada

No se modifica exposición ni material del casco basándose en una sola captura EDL
parcial: el shock ya es visible, no hay clipping masivo ni NaN, y la diferencia de
lectura entre la silueta oscura y el limbo corresponde a `solarVisibility=0` del
escenario. Queda como siguiente A/B visual medir un EDL diurno con visibilidad solar
directa y comparar luminancia del casco contra `peak_heating`; cualquier ajuste debe
preservar el flujo térmico y no levantar artificialmente el cielo nocturno.

## Ciclo solar de superficie — evidencia temporal real (2026-08-20)

La matriz completa revalidada con el gate actual es:

```text
OUT_DIR=/tmp/exo_visual_ground_v1 \
LOG=/tmp/exo_visual_ground_v1.log \
bash tools/visual_playtest.sh --atmosphere-ground \
  --run-id visual-audit-ground-v1 --verify-only
```

Sus cuatro PNGs 1920×1080 son `ground_day`, `ground_sunrise`, `ground_sunset` y
`ground_night`. Todos llevan `exposureSettled=True`, radiancia espectral finita y
`surfaceClippedFrac=0`; amanecer y noche tienen `clippedFrac=0`, mientras atardecer
queda en `0.00457`. El día tiene `clippedFrac=0.02748` y
`skyWhiteClipFrac=0.02440`, localizado en la zona del disco/cúpula solar, no en el
suelo. La noche conserva 28 estrellas nítidas y `neonGreenFrac=0`.

La inspección de los PNG confirma una transición continua de cielo azul a banda civil
oscura, horizonte naranja en atardecer y cielo estelar nocturno. El atlas de suelo
mantiene textura y no cambia de exposición de forma abrupta. La repetición fresca
`visual-ground-cycle-final-v1` se detuvo tras `ground_day` y `ground_sunrise` por el
límite de 360 s de llvmpipe, sin GAP físico; no reemplaza el PASS del artefacto
completo revalidado.

Decisión: no se toca el shader ni la física solar en esta pasada. El recorte medido
está confinado al sol visible y el resto de la matriz mantiene continuidad; cualquier
reducción posterior debe ser un A/B de luminancia solar para no desaturar el amanecer
ni quitar detalle a la noche.

## Ciclo solar y amanecer — verificación de comportamiento (2026-08-20)

El paso de tiempo ya está conectado a la física y al renderer, no a un contador de
frames: `Universe.CurrentTime` avanza con `realDelta × TimeScale`,
`SunController` calcula la elevación solar continua y los shaders reciben la dirección
actualizada. La clasificación visual conserva las bandas `DAY`, civil, náutica,
astronómica y `NIGHT`; el HUD muestra la elevación y el warp activo.

La cobertura automatizada actual es:

- `TimedSurfacePositionMovesAtRotationalSurfaceVelocity` verifica la derivada de la
  superficie frente a la velocidad rotacional.
- `TimedSurfacePositionCompletesOneSiderealDay` verifica que la superficie vuelva a
  su posición después de 86 164 s y cambie claramente durante un cuarto de día.
- `solar_cycle_contract_test.sh` verifica que la iluminación consuma
  `Universe.CurrentTime`, publique fase/elevación y exponga `TimeScale`.

La cobertura física y la secuencia real de PNG están cerradas para esta revisión en
`/tmp/exo_visual_ground_v1`: la misma cámara conserva continuidad entre día,
amanecer, atardecer y noche, con `ATMOS_STATE` y exposición asentada por caso. La
matriz no simula el paso del tiempo con cuatro frames de una misma fase; cada captura
se aplica a una elevación solar distinta y el gate comprueba la correspondencia
física antes de aceptar el PNG.
