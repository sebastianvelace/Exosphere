# Plan operativo de optimización multiagente — fase 23

Estado: fase 66 promovida como `SOLAR_GEOMETRY_SNAPSHOT / CPU_PRESENTATION`: SunController calcula la geometría solar a 20 Hz y SkyController reutiliza el snapshot a 12 Hz, con fallback de primer frame; fase 65 promovida como `PLANET_MESH_RESOURCE_REUSE / CPU_TRANSITION_ONLY / FRAMEBUFFER_PENDING`: la presentación lazy comparte una única esfera procedural 96×48 entre cuerpos y conserva materiales por instancia; fase 64 promovida como `ENGINE_TELEMETRY_BATCH / CPU_PRESENTATION`: el HUD consume un snapshot agregado junto a sus filas y elimina reevaluaciones repetidas de thrust/flujo/Isp; fase 63 cerrada como `A/B_MEASURED / LOW_DIAGNOSTIC_ONLY`: el perfil oficial `0.60` conserva el valor predeterminado; `0.25` redujo aproximadamente 33.8% el render Earth en este host llvmpipe, pero queda sólo como probe por sensibilidad cromática de Venus denso y ausencia de GPU física; fase 62 integrada como consulta booleana de presencia de motores (`PROMOTED CPU/PRESENTATION`); scheduler distingue pausa/entrada inválida/no inicializado y exporta dispatches Mixed/Rails con contrato dinámico; catch-up sigue sin presupuesto ni deuda temporal por seguridad; integración del sky normalizada para calidades fraccionarias y transmitancia de iluminación cacheada a 10 Hz; terreno marciano lazy y diagnóstico de render/presentación; telemetría del scheduler integrada al playtest y consulta de warp sin duplicación; vistas estables del universo y consumo de propelente sin boxing por tick; enumeración interna de partes/motores, fallos programados, interpolación de motores y consumo runtime promovidos; hot-stage promovido; gameplay Starship corregido; reentrada normal con gate físico pendiente de framebuffer; HUD secundario, navball y captura del HUD principal limitados a cadencias acotadas; tiempo a periapsis calculado una vez por snapshot y consumido por el HUD; caches de invalidación para navegación, phase track y vista/densidad; resolver de cámara/renderer con reintentos acotados; consultas de torre indexadas en el puente de simulación; dirty cache de Environment/lighting/exposure/ground; VFX de plumas Starship mantienen unidades agregadas; EventPipe externo pendiente
Fecha: 2026-08-14  
Base: `main` después de la fase 27; esta corrección añade regresiones de gameplay y reentrada

Actualización 2026-08-18: fase 67 promovida como `VESSEL_PRESENTATION_SAMPLE_CACHE /
CPU_PRESENTATION`. `VesselRenderer` selecciona cuerpo, altitud y presión una vez a 20 Hz y
reutiliza la muestra para plumas, flaps y térmica; la física del universo conserva sus
consultas y cadences propias.

Fase 68 promovida como `LAUNCH_PAD_PRESENTATION_DIRTY_CACHE / CPU_PRESENTATION`. El pad muestrea
estado de captura a 20 Hz, conserva interpolación suave de chopsticks, y evita escrituras
repetidas de luces/poses cuando no cambian; la captura física sigue siendo autoritativa.

## Resultado de la fase 56 — resolución acotada de cámara y renderer

La cámara conserva ahora sus referencias a `Camera3D`, `CockpitRenderer` y
`ActiveVesselRenderer` sin recorrer el árbol de escena en cada frame estable. Los nodos que
`SimulationBridge` crea de forma lazy siguen siendo detectados mediante reintentos de `0.25 s`;
el fallback `StarshipRenderer` del harness también puede ser sustituido cuando aparece el
renderer oficial. Las comprobaciones de `IsInstanceValid` evitan acceder a nodos liberados
durante una transición de escena.

El cambio es sólo de presentación: no toca `Universe.Tick`, orientación, seguimiento de
vehículo, entrada, plumas, térmica ni la física de salto/reentrada. El contrato estático exige
el cooldown, la cache y el retorno temprano. Se debe medir en framebuffer real antes de
atribuir una mejora de FPS; el host actual mantiene bloqueado ese gate por X11/llvmpipe.

Informe reproducible: `PERF_RENDERER_CAMERA_CACHE_PHASE56_REPORT.md`.

## Resultado de la fase 57 — hot path de presentación de la torre

`SimulationBridge._Process` usa ahora búsquedas indexadas para anclar la
torre, detectar aproximaciones y mantener los objetivos de captura de toda la flota. Esto
reduce trabajo de enumeración por interfaz sin cambiar la política física ni la actualización
por frame requerida por la rotación del sitio. El caso Ship activo + booster retornando queda
explícitamente conservado.

Informe: `PERF_SIMULATION_BRIDGE_CATCH_HOTPATH_PHASE57_REPORT.md`.

## Resultado de la fase 58 — dirty cache de sky y lighting

El sky y la iluminación atmosférica conservan sus valores actuales y sólo
escriben cuando cambian más de `1e-4`. Esto reduce invalidaciones de `Environment` y de la luz
direccional durante vuelo estable, sin reducir muestras, cambiar el orden de scattering ni
ocultar cues de eclipse, terminador o reentrada.

Informe: `PERF_SKY_ENVIRONMENT_DIRTY_CACHE_PHASE58_REPORT.md`.

## Resultado de la fase 59 — dirty cache de exposición

`VisualExposureController` conserva la adaptación por frame, pero evita
reescribir `TonemapExposure` cuando el valor no cambió perceptiblemente. Esto completa la
familia de caches de presentación de `SkyController`, `PhaseLightingController` y exposición
sin reducir el coste físico ni la calidad oficial del LUT.

Informe: `PERF_EXPOSURE_DIRTY_CACHE_PHASE59_REPORT.md`.

## Resultado de la fase 60 — VFX de motores y suelo local

La auditoría confirma que la escena no crea 33/39 emisores para Starship;
mantiene unidades agregadas y actualización limitada. El cambio aplicado reduce escrituras
redundantes del shader del suelo local sin congelar sus coordenadas geográficas.

Informe: `PERF_VFX_GROUND_DIRTY_CACHE_PHASE60_REPORT.md`.

## Resultado de la fase 61 — smoke de framebuffer y separación de costes

El smoke directo con `--run-id phase61-framebuffer` completó `SMOKE_OK` con un PNG válido,
50 frames, build de Godot sin warnings/errores y worker atmosférico Earth asíncrono de orden
4. En el framebuffer llvmpipe el frame medio fue `1,884.660 ms`, mientras que el scheduler
fue `1.791 ms` medio (`1.008 ms` p50); la telemetría no mostró `catch_up_risk` ni deuda
pendiente. El resultado refuerza que el atasco observado en este host está fuera de la física
del universo.

El A/B `0.60` frente a `0.25` no se cerró: `tools/perf/renderer_benchmark.sh` y los reintentos
del harness encontraron un Xvfb que no puede crear listeners porque `/tmp/.X11-unix` pertenece
a `nobody:nogroup`. No se publican FPS ni VRAM, no se promueve `sky_quality_low` y no se
modifica el runtime. Informe reproducible:
`PERF_FRAMEBUFFER_SMOKE_PHASE61_REPORT.md`.

## Resultado de la fase 62 — presencia de motores sin enumerador de interfaz

La presentación consultaba cinco veces por frame `ActiveEngines` sólo para saber si existía
algún motor de la etapa actual. `PartGraph.HasActiveEngineParts` y su wrapper de `Vessel`
resuelven esa presencia mediante el mismo buffer concreto, sin boxear el enumerador. El
enumerable público permanece compatible para los consumidores que sí requieren todas las
partes.

La prueba de equivalencia cubre estado normal, hot-stage y separación; la regresión de
allocations exige una mejora superior a 512 bytes en 256 consultas y un máximo de 512 bytes
para el camino nuevo. CI pasó `698/698`, los builds quedaron en `0/0` y los contratos de
presentación/telemetría permanecen verdes. Informe: `PERF_ACTIVE_ENGINE_PRESENCE_PHASE62_REPORT.md`.

## Resultado de la fase 63 — A/B de calidad atmosférica en framebuffer

El A/B bloqueado por X11 quedó medido en un framebuffer OpenGL3 llvmpipe con el mismo
escenario físico. La matriz Earth completa pasó 20/20 casos en los perfiles oficial `0.60`
y diagnóstico `0.25`; la matriz Mars/Venus pasó 6/6 en cada perfil. B redujo el render
Earth aproximadamente 33.8% en media después del calentamiento, sin alterar objetos,
primitivas, draw calls, estrellas, terminador, eclipse ni los gates de radiancia. Venus a
10 km mostró una diferencia cromática medible y clipping alto ya presente en ambos perfiles,
por lo que la reducción no se promueve al runtime.

El gate de `ATMOSPHERE_BODIES_OK` también se ajustó para aceptar el campo `frames=N` que el
resumen real añade después de la razón terminal, manteniendo el rechazo de resúmenes ausentes
o duplicados. Informe reproducible: `PERF_SKY_QUALITY_AB_PHASE63_REPORT.md`.

## Resultado de la fase 64 — snapshot agregado de telemetría de motores

`EngineGridHUD` y `FlightHudPresenter` llenaban las filas de motores y luego repetían la
consulta de etapa activa para thrust, flujo másico, Isp y nominales. `PartGraph` ahora calcula
esos agregados durante el mismo `FillEngineReadouts`, los conserva junto al cache de filas y
los expone mediante `EngineTelemetrySummary`. El overload anterior permanece compatible.

La prueba aislada con 33 motores runtime y 2.000 muestras pasó de 181.879 ms en la secuencia
legacy a 3.563 ms en la lectura por lote, una reducción medida de 98.04% del trabajo agregado
repetido. Es un microbenchmark de CPU, no una medición de FPS; el host sigue limitado por
llvmpipe. Las pruebas cubren cluster runtime, engine-out, cluster agregado y cache estable.
La suite terminó en 701/701 y el startup headless pasó; el smoke framebuffer quedó bloqueado
por la propiedad incorrecta de `/tmp/.X11-unix`, por lo que no se declara un gate visual PASS.

Informe: `PERF_ENGINE_TELEMETRY_BATCH_PHASE64_REPORT.md`.

## Resultado de la fase 65 — reutilización de geometría planetaria lazy

La carga inicial ya materializaba sólo el cuerpo dominante, pero cada transición posterior
reconstruía la misma `SphereMesh` unitaria de 96×48. `SimulationBridge` ahora conserva una
esfera compartida por instancia y mantiene el material override por planeta, sin alterar
radios, eclipses, Saturno ni el estado físico.

El arranque headless confirmó una sola marca `mesh_cache=created` y `simulation_loaded` en
1318.1 ms; frente al baseline lazy de 1323.1 ms no se declara una mejora de startup por estar
dentro del ruido. El beneficio pendiente es el hitch de Earth→Mars/Venus/Saturn, que requiere
framebuffer y medición de memoria. Informe: `PERF_PLANET_MESH_CACHE_PHASE65_REPORT.md`.

## Resultado de la fase 66 — snapshot compartido de geometría solar

`SunController` y `SkyController` repetían la misma integración geométrica del disco solar.
La propiedad se centralizó en un snapshot de 20 Hz que el sky consume a 12 Hz. El primer frame
mantiene un fallback para evitar una transición visual incompleta; después del arranque el log
confirma `mode=shared` y `consumer=sky cache_hit=True`.

La reducción estructural es de 224 a 140 evaluaciones por segundo en el fixture de siete
cuerpos no solares (**37.5%** menos). Esto es CPU de presentación y no una medición de FPS.
Informe: `PERF_SOLAR_GEOMETRY_CACHE_PHASE66_REPORT.md`.

## Resultado de la fase 67 — muestra cacheada del renderer exterior

`VesselRenderer` consultaba `Universe.GetDominantBody`, altitud y presión atmosférica en cada
frame visible, aunque las plumas se actualizan a 30 Hz y los flaps/tren a 20 Hz. La ruta ahora
refresca una muestra de presentación a 20 Hz y la comparte con plumas, flaps y térmica. La
primera entrada y cada reconstrucción de nave fuerzan una muestra; al ocultar el renderer no se
hace ninguna consulta. `Universe.Tick`, `Vessel` y los sistemas de gameplay no consumen este
cache ni reducen su frecuencia.

En un cuerpo atmosférico, el caso típico pasa estructuralmente de 60 selecciones de cuerpo +
60 altitudes + 60 presiones (presión explícita de pluma y la presión interna del llenado de
filas) por segundo a una muestra compartida de 20 + 20 + 20 (**180 → 60 lecturas de API,
66.7% menos**). El renderer usa un overload de presentación que recibe esa presión ya
muestreada; el overload físico basado en cuerpo permanece intacto para HUD y simulación. Es una
reducción de CPU de presentación calculada por cadencia, no una promesa de FPS ni una
modificación de la física. El intervalo máximo de datos visuales obsoletos es 50 ms.

Validación: contratos de cadencia/telemetría PASS; builds de la librería y Godot con 0
warnings/0 errors; suite xUnit **702/702 PASS**; contratos de optimización **46/46 PASS**;
Godot Flight headless/OpenGL3 exit 0 usando log temporal. La captura de framebuffer/FPS sigue
pendiente porque este entorno no puede escribir su log persistente/Xvfb; no se publica una
ganancia de FPS. Informe: `PERF_VESSEL_PRESENTATION_SAMPLE_PHASE67_REPORT.md`.

## Resultado de la fase 68 — dirty-cache del pad y chopsticks

`LaunchPadController` recorría la flota dos veces por frame para conocer si existía un vehículo
armado o capturado, reescribía las ocho posiciones visuales de los chopsticks en cada frame y
volvía a asignar `Visible` a todas las luces nocturnas aunque el día/noche no hubiera cambiado.
La ruta ahora conserva el estado de captura muestreado a 20 Hz, interpola los brazos por frame
para no perder suavidad y sólo escribe su pose cuando cambia más de `0.0001`. Las luces se
actualizan sólo al cambiar el umbral nocturno. Los pads sin chopsticks salen antes de escanear
la flota.

No se movió ni relajó `Universe.EvaluateCatchContact`, `Vessel.IsCaught`, la guía de reentrada,
la visibilidad especial del complejo durante EDL ni el criterio de dos pasadores. La demora
máxima visual del estado es 50 ms y no puede crear un `CAUGHT` falso: el cierre sigue dependiendo
de `CatchCaptured`, que sólo se alimenta desde `Vessel.IsCaught`.

Validación final: contrato del pad PASS; CI `EXIT=0`; contratos de optimización `46/46 PASS`;
builds `0 warnings/0 errors`; xUnit `702/702 PASS`; startup Flight y Construction headless PASS.
El intento de framebuffer EDL no produjo harness/capturas en este host por la limitación
X11/Xvfb, por lo que no se declara aceptación visual ni FPS. Informe reproducible:
`PERF_LAUNCH_PAD_PRESENTATION_PHASE68_REPORT.md`.

## Auditoría de gameplay Starship — cierre de la fase actual

La discrepancia del HUD quedó clasificada: `THR 100%` es throttle comandado, mientras que
los puntos rojos representan instancias con `FailureCode`; por tanto, la captura con `ENG 0/33`
y los 33 puntos rojos era un engine-out real, no un estado normal de espera. El HUD ahora
expone la primera familia de fallo (`STARVATION`, `FEED LIMIT`, `OVERHEAT` o `RESTART LIMIT`)
para que el diagnóstico no dependa de interpretar dos indicadores contradictorios.

También se cerró el salto `J`: antes de aplicar la orientación y el cuerpo destino se limpian
los comandos retrasados del `GroundCommandRelay` y el estado transitorio de motor (presión de
cámara, gimbal y throttle). Se conservan los contadores/historiales de fiabilidad, de modo que
el reset no borra fallos persistentes ni falsea telemetría.

La reentrada normal de una Starship desde un sitio Starbase terrestre arma ahora el mismo
acercamiento físico de la torre que la demostración: la presentación queda anclada al vehículo
que realmente intenta la captura, la torre permanece visible durante todo el EDL y el solver
de dos pasadores, velocidad relativa y asentamiento sigue siendo quien decide `CAUGHT`. Otros
cuerpos, otros sitios y vehículos sin configuración Starship conservan el camino de patas.

Evidencia de integración de esta auditoría:

- suite xUnit completa: `596/596 PASS`, 0 omitidos (tras la cobertura térmica/autoridad de flaps);
- build Godot C# con `--no-restore`: 0 warnings, 0 errors;
- contratos de gameplay: PASS; contratos acumulados de optimización: `34/34 PASS`;
- startup quick-check: PASS; escenas Flight y Construction headless: exit 0;
- EDL visual: `CAUGHT`, `pins=2`, `relativeSpeed=0.030`, `angularSpeed=0.0000`, 616 frames;
- ascenso visual parcial: a 2.5 km se observaron `runningEngines=33` y `failedEngines=0`;
  se interrumpió por el coste del framebuffer llvmpipe, por lo que no se etiqueta como
  `ASCENT_ORBIT_OK`.

## Siguiente etapa — estabilidad post-J y reentrada orbital normal

La matriz CPU post-`J` quedó incorporada en `PostJumpStabilityTests`: Earth, Mars y Venus
mantienen cuerpo de referencia, geometría finita, throttle/actitud/velocidad angular nulos y
estado no destruido durante 150 ticks; el relay tampoco reaplica comandos previos. Resultado:
`4/4 PASS` nuevos, más la regresión de control retrogrado y la protección térmica del conjunto
de motores; `596/596 PASS` en la suite completa.

El harness ahora tiene `--orbital-reentry`, opt-in y fail-closed. Usa `JumpToOrbit` sólo como
setup explícito, arma el deorbit con el flujo real de mapa (`B` + `Enter`) y exige telemetría
`normalFlow=True`, entrada, peak heating, retro-burn y captura física. Los contratos aceptan un
fixture normal válido y rechazan tanto demo-only como ausencia de captura.

La primera corrida framebuffer oficial alcanzó `ENTRY`, `PEAK_HEATING` y `AERO_DESCENT`, pero
terminó en `GroundImpact` porque el modelo térmico trataba el conjunto de motores, ubicado
detrás del tanque, como metal desnudo. La traza mostró que el conjunto desaparecía antes de
la compuerta de 8 km; no fue un fallo de deorbit, de selección de motores ni un bypass del
solver. El perfil de datos ahora declara su protección térmica y una regresión exige que esa
propiedad permanezca. La repetición headless alcanzó `ENTRY` sin fallos de motor y con reserva
de propelente, pero no puede producir las capturas ni cerrar el contacto físico en el renderer
dummy. La repetición framebuffer a 1.200 km sigue siendo obligatoria y sólo se promoverá como
`ORBITAL_REENTRY_OK` si registra `flip`, `CAUGHT` y contacto físico.

La validación de GPU física, EventPipe y la matriz framebuffer completa sigue `BLOCKED` en este
host por llvmpipe; no se convertirán esos bloqueos de infraestructura en supuestas ganancias
de FPS.

## Resultado de la fase 27 — hot path de staging

La primera optimización local de esta oleada se limitó a `PartGraph.BuildCurrentStageParts`.
El camino anterior creaba una lista LINQ de desacopladores, enumeradores de `GetChildren` y
una lista temporal del subárbol cada vez que el scheduler consultaba motores activos. El
camino nuevo reutiliza dos buffers privados y recorre `_parts`/`_joints` por índice; conserva
el orden anterior y no modifica las fórmulas físicas, los deadlines ni la política de
wake-up.

Medición comparable en .NET 8, `SAMPLES=80`, `WARMUP=10`:

| Escenario | p95 antes | p95 después | allocations antes | allocations después | cambio allocations |
|---|---:|---:|---:|---:|---:|
| `full_single` | 0.0442 ms | 0.0477 ms | 5,981.9 B/tick | 5,253.9 B/tick | −12.17% |
| `rails_fleet` | 0.7363 ms | 0.7421 ms | 190,077.9 B/tick | 186,749.9 B/tick | −1.75% |
| `mixed_fleet` | 3.7606 ms | 3.6976 ms | 718,564.0 B/tick | 598,761.2 B/tick | −16.67% |

Con `SAMPLES=256`, `mixed_fleet` quedó en 4.0265 ms p95 y 605,960.5 B/tick, con
`dispatches=450`, `projections=396` y todos los indicadores finitos/válidos. La variación
de p95 no se presenta como una ganancia de FPS hasta tener una captura de framebuffer en
hardware objetivo; la decisión de promoción se basa aquí en la reducción directa de
allocations y la equivalencia funcional.

Cobertura nueva: `PartGraphHotPathTests` verifica que desacopladores activos anidados,
desacopladores ya disparados y orden de partes conservan exactamente la selección de etapa.
Suite completa posterior: `585/585 PASS`, 0 omitidos. Informe: `PERF_PARTGRAPH_ALLOCATIONS_PHASE27_REPORT.md`.

La auditoría P1 conserva `sample_window` y deadlines; no se reducen dispatches ni
proyecciones. El candidato de reutilizar la clasificación `OnRails` fue medido de nuevo,
no reprodujo una mejora estable y quedó descartado para esta fase. Informe:
`PERF_SCHEDULER_SAMPLE_WINDOW_PHASE27_REPORT.md`.

La oleada P2/P4 posterior integró dos mejoras independientes y verificadas: buffers/caches
para reducir allocations de simulación y HUD, y cache de nodos de cámara que oculta el
`ActiveVesselRenderer` correcto durante cockpit. Los cambios no alteran la física. Informes:
`PERF_ALLOCATIONS_HOTPATH_PHASE27_P2_REPORT.md` y el commit
`4cd5117 perf: cache cockpit and exterior renderer nodes`.

## Resultado de la fase 28 — allocations del scheduler y RK4

El perfil de la siguiente etapa aisló dos fuentes independientes de garbage en el tick:

- `KeplerPropagator.PropagateAllBodies` reconstruía un `Dictionary` y dos `HashSet` en cada
  subpaso mixto. `Universe` ahora posee un workspace reutilizable por instancia; la API
  pública enumerable conserva su comportamiento para callers externos.
- `RK4Integrator.StepPosVel` creaba el vector de estado y múltiples arrays temporales por
  integración. El camino 6-DoF usa las mismas cuatro etapas RK4 sobre `Vector3d`; el API
  genérico por arrays queda intacto para estados arbitrarios.

También se eliminaron predicados LINQ capturados de `Universe.GetBody`, el fallback LINQ de
`GetDominantBodyAt` y la enumeración por interfaz en las sobrecargas de fuerza que utiliza
`Universe`. No se cambiaron pasos máximos, deadlines, fórmulas de gravedad, drag, thrust ni
criterios de wake-up.

Medición reproducible del benchmark `tools/perf/allocations_tick_phase23_benchmark.sh`,
`.NET 8`, `SAMPLES=80`, `WARMUP=10` (el baseline previo equivalente fue
`mixed_fleet=583,989.8 B/tick`, `p95=5.0599 ms`, con 40 muestras):

| Escenario | p95 actual | allocations/tick actual | dispatches | proyecciones |
|---|---:|---:|---:|---:|
| `full_single` | 0.0564 ms | 2,734.0 B | 1 | 0 |
| `rails_fleet` | 0.5616 ms | 5,931.3 B | 32 | 0 |
| `mixed_fleet` | 4.0478 ms | 182,965.4 B | 450 | 396 |
| `wake_catchup` | 1.3890 ms | 88,215.9 B | 50.013 | 12.375 |

`mixed_fleet` reduce allocations aproximadamente 68.7% frente al baseline medido y su p95
queda aproximadamente 20.0% por debajo. La mejora de tiempo se considera CPU de referencia,
no FPS de hardware: la validación Vulkan/GPU y el framebuffer orbital siguen bloqueados por
el host llvmpipe/X11. La regresión `RK4AllocationRegressionTests` exige resultado exacto
para aceleración constante y ≤64 B por subpaso en el camino 6-DoF.

Decisión: promover la reutilización de buffers y el RK4 especializado. Mantener sin cambios
la política de hibernación y los deadlines hasta obtener EventPipe/GPU y equivalencia visual
en hardware objetivo.

## Resultado de la fase 36 — telemetría de catch-up y entradas inválidas

La auditoría multiagente confirmó que el riesgo más importante para un “freeze” después de
entrar al nivel no era un error de rails, sino el catch-up ilimitado: `Universe.Tick` puede
convertir un hitch de wall-clock en cientos o miles de substeps cuando `TimeScale` es alto.
Cambiar ahora la política para descartar tiempo simulado sería peligroso porque podría saltar
impactos, SOI, calentamiento o wake-ups.

La fase 36 añade instrumentación sin alterar la física:

- `PhysicsSchedulerTelemetry.WallClockMilliseconds` mide el coste del scheduler por tick.
- `CatchUpRisk` se marca cuando una llamada alcanza o supera `Universe.CatchUpWarningSubsteps` (`128`).
- Un hitch de `0.5 s` a `x1000` conserva exactamente `500 s` simulados y registra `250`
  substeps con `CatchUpRisk=True`.
- `NaN`, infinito, delta negativo y escalas inválidas se convierten en no-op seguro; el
  reloj, el estado de cuerpos y la rama publicada no se corrompen.
- El benchmark serializa `scheduler_wall_clock_ms` y `catch_up_risk` para correlacionar la
  próxima captura de entrada al nivel con substeps reales.

La prueba focalizada del scheduler pasó `21/21`, la suite completa `605/605`, y el benchmark
con `8` muestras por escenario terminó con `summary_finite=true` y `summary_valid=true`.
La decisión es mantener la política actual y usar esta telemetría para diseñar la siguiente
fase: acumulador fijo o presupuesto de catch-up con equivalencia física explícita. No se activa
hibernación real ni se aumenta `MaxCoastStep` todavía.

Informe reproducible: `PERF_SCHEDULER_CATCHUP_TELEMETRY_PHASE36_REPORT.md`.

## Resultado de la fase 37 — telemetría del playtest y consulta de warp sin duplicación

La biblioteca ya exponía el coste de `Universe.Tick`, pero el harness de Godot sólo registraba
el wall-clock del callback. Ahora cada `PERF_FRAME` correlaciona `frame_ms` con
`scheduler_ms`, rama (`FullPhysics`, `Mixed`, `Rails` o `None`), substeps, cap efectivo,
segundos simulados y `catch_up_risk`. Esto permite distinguir un atasco de física de uno de
LUT, render o presentación en la captura real de entrada al nivel.

El puente de warp también dejó de llamar por separado a `RequiresOffRailsPhysics` y
`RequiresBoundedWarpPropagation`. `GetWarpPhysicsRequirements` calcula ambas decisiones una
sola vez por frame, sin cachearlas entre frames ni alterar las APIs existentes. La regresión
`CombinedWarpRequirementsMatchIndividualQueries` exige igualdad con las dos consultas
individuales.

Resultado de verificación de esta fase: `25/25` pruebas focalizadas, suite completa
`606/606`, build Godot con `0 warnings / 0 errors`, contrato estático `32 PASS / 1 dynamic
skip` y contrato dinámico `40 PASS / 0 FAIL` sobre el log real (`234/234` frames válidos).
El playtest llegó a `ASCENT_SH` con `33/33` motores y estado finito; el gate orbital queda
pendiente porque el host llvmpipe mide ~1,011 ms de mediana por frame, aunque el scheduler
midió sólo `4.824 ms` de mediana. La siguiente fase debe perfilar render/presentación/UI/LUT
con esta misma correlación antes de tocar el presupuesto físico.

Informe reproducible: `PERF_PLAYTEST_SCHEDULER_TELEMETRY_PHASE37_REPORT.md`.

## Resultado de la fase 38 — diagnóstico render/presentación y terreno marciano lazy

La captura de fase 37 aisló el coste fuera de `Universe.Tick`; esta fase lo confirmó con el
probe de render in-process. En el host llvmpipe, el smoke Earth normal midió `1,098.077 ms`
CPU render y `1,102.228 ms` GPU por frame, con `9,774` objetos, `1,218,406` primitivas y
`15,772` draw calls. El A/B sin sombras bajó a `984.074 ms`, `8,035` objetos, `982,074`
primitivas y `12,293` draw calls; el A/B ocultando el pad no cambió los contadores, así que
no se atribuye el cuello de botella al pad sin una medición de hardware/nodo más precisa. El
A/B ocultando el sky bajó a `416.502 ms` CPU y `424.003 ms` GPU, confirmando que el shader
atmosférico es el cuello de botella dominante en este backend; no se elimina porque rompería
la escena.

`MarsTerrainController` dejó de construir síncronamente su malla 96×96 durante el arranque
Earth. Ahora sólo se crea al acercarse realmente a Mars y registra su coste puntual. Esto es
una mejora segura de trabajo innecesario, no una afirmación de FPS: el backend llvmpipe tiene
variación de arranque y las corridas A/B terminaron como `SMOKE_OK`, no como benchmark de GPU
física.

El probe permite las variantes opt-in `hide_pad`, `hide_sky`, `no_directional_shadows`,
`hide_launch_effects`, `hide_vessel`, `hide_hud`, `hide_starfield` y `hide_earth_ground`.
Se conserva la configuración oficial de sombras y la calidad atmosférica `0.60`; la siguiente
fase probará una calidad baja del sky con gates visuales y medirá VFX/HUD durante ignición,
reentrada y captura.

Informe reproducible: `PERF_RENDER_PRESENTATION_PHASE38_REPORT.md`.

## Resultado de la fase 39 — A/B de calidad del sky atmosférico

El probe midió el uniforme ya existente `atmosphere_quality` sin alterar el valor oficial.
En el host llvmpipe, `0.60` dio `1,098.077 ms` CPU render y `1,102.228 ms` GPU; `0.25`
dio `788.115 ms` y `795.604 ms`, una reducción aproximada de `28%` con los mismos
`9,774` objetos, `1,218,406` primitivas y `15,772` draw calls. Retirar completamente el
sky baja a `424.003 ms` GPU, confirmando la atribución pero no siendo una solución visual.

Se agregaron las variantes opt-in `sky_quality_low` (`0.25`) y `sky_quality_min` (`0.0`)
al probe; el renderer normal sigue en `0.60`. La captura pad baja no muestra una regresión
obvia, pero no valida terminador, eclipse, limbo ni Mars/Venus. No se promueve aún: la
siguiente etapa debe ejecutar la matriz visual completa y medir una GPU física antes de
convertirlo en preset `Low`.

Informe reproducible: `PERF_SKY_QUALITY_AB_PHASE39_REPORT.md`.

## Resultado de la fase 40 — integración segura y cadencia de iluminación

La calidad atmosférica fraccionaria ya no sobrepasa el extremo de los rayos: el shader usa
`ceil(requested_steps)` como denominador y como límite efectivo en vista, transmitancia solar,
vista de nubes y sombra de nubes. Esto es una corrección numérica válida para el perfil oficial
y para el diagnóstico, no una promoción de calidad.

`PhaseLightingController` ahora mantiene `DirectSolarTransmittance` durante `100 ms` y fuerza
invalidación por cuerpo dominante, horizonte solar, salto de `2 km` o cambio significativo de
dirección. La visibilidad de eclipse sigue llegando desde `SunController` y el cálculo físico
de térmica no se comparte ni se pausa.

Con el shader corregido y el cache activo, el smoke llvmpipe dio `1,105.361 ms` GPU mediana en
`0.60` frente a `944.074 ms` en `0.25` (`~14.6%`), con `SMOKE_OK` en ambos casos. La corrida
Earth completa fue interrumpida en `7/20` capturas por el coste extremo del framebuffer, así
que no se habilita `0.25` en el runtime. La matriz completa Earth y Mars/Venus queda como gate
de la próxima fase en hardware físico.

Informe reproducible: `PERF_PRESENTATION_PHASE40_OPTICAL_CADENCE_REPORT.md`.

## Resultado de la fase 41 — scheduler observable antes de introducir deuda temporal

`PhysicsSchedulerTelemetry` distingue `NotInitialized`, `Paused`, `InvalidDelta`,
`InvalidTimeScale` y tick válido. El harness conserva `PERF_FRAME` y añade
`PERF_SCHEDULER schema=1` con contadores de FullPhysics, Rails, anclados, destruidos,
docking y deadlines. El contrato dinámico exige que todas las categorías sumen `total_work`
y que la rama/razón sean coherentes.

El smoke real produjo `50/50` líneas válidas, `SMOKE_OK` y `47 PASS / 0 FAIL / 0 SKIP` en
`performance_acceptance_contract_test.sh`. En el pad, la carga aparece como `GroundHeld`,
no como física dinámica falsa.

La hibernación real y el cap de catch-up quedan deliberadamente pendientes: `Universe.Tick`
todavía consume el intervalo completo. La próxima implementación debe conservar deuda exacta,
interrumpir sólo entre pasos globales completos, presupuestar también Rails y consumir el
tiempo realmente procesado en los sistemas de gameplay. No se acepta descartar tiempo ni
ocultar impactos/SOI/contactos.

Informe reproducible: `PERF_SCHEDULER_TELEMETRY_PHASE41_REPORT.md`.

## Resultado de la fase 42 — deuda temporal exacta y wake-up seguro (opt-in)

Se añadió deuda temporal global exacta y telemetría separada de tiempo solicitado,
procesado y pendiente. El presupuesto determinista se detiene sólo entre pasos globales
completos y permanece desactivado por defecto hasta migrar los sistemas de gameplay a
segundos procesados. `SetCurrentTime`/`SetSimulationTime` limpian deuda y sincronizan
cuerpos; staging, docking, undock y wake-up invalidan conics obsoletas. Estados no finitos
no se clasifican como rails analíticos.

El harness usa `PERF_SCHEDULER schema=2` con deuda y razón de presupuesto. Validación:
`613/613` xUnit, build Godot `0 warnings/0 errors`, smoke real `SMOKE_OK`, contrato
dinámico `54 PASS / 0 FAIL / 0 SKIP`, y `50/50` líneas de scheduler válidas. El cap no se
promueve al runtime: los sistemas físicos de gameplay todavía deben consumir el tiempo
realmente procesado antes de activar la deuda en el juego.

Informe reproducible: `PERF_SCHEDULER_DEBT_WAKEUP_PHASE42_REPORT.md`.

## Resultado de la fase 43 — consumo de tiempo simulado procesado

`SystemsController` conserva su pre-pase para relay y consecuencias, pero los consumibles
se actualizan después de `Universe.Tick` mediante `AdvanceProcessedSimulation`. Life support,
potencia, térmica, comunicaciones, Δv de maniobras/autopilot y temporizadores físicos de
EDL consumen `ProcessedSimulationSeconds`; la pausa no adelanta ninguno. La presentación y
la entrada continúan usando wall-clock.

Build Godot: `0 warnings / 0 errors`; smoke real `SMOKE_OK`; contrato dinámico `54 PASS /
0 FAIL / 0 SKIP`; `50/50` líneas de scheduler válidas. La deuda y el presupuesto siguen
desactivados por defecto hasta completar paridad de consumibles, blackout, EDL, staging y
docking bajo deuda.

Informe reproducible: `PERF_PROCESSED_SIM_TIME_PHASE43_REPORT.md`.

## Resultado de la fase 52 — cadencia acotada del HUD de presentación

La auditoría de presentación encontró `QueueRedraw()` de `SystemsHUD` y
`AttitudeDataStrip`, además del cálculo completo de `AttitudeNavball`, ejecutándose a la
frecuencia del renderer aunque son datos de presentación. `SystemsHUD` ahora redibuja a
10 Hz; navball y data strip a 30 Hz, con redraw inmediato al hacerse visibles y acumulación
de tiempo para el filtro de rumbo. El cambio no toca la simulación ni los comandos.

La compilación de ambos proyectos pasa con 0 warnings/0 errores, la suite directa pasa
`696/696`, startup alcanza 60 frames y los contratos de cadencia/telemetría/hot-path pasan.
El A/B framebuffer no se etiqueta como medido porque Xvfb falló antes de crear el display;
el reporte reproducible es `PERF_HUD_CADENCE_PHASE52_REPORT.md`. No se afirma una ganancia
de FPS hasta repetirlo en un host con framebuffer válido.

## Resultado de la fase 53 — captura del HUD principal a 30 Hz

`FlightHudPresenter.Capture` costó `0.019567 ms` p50 y `922.2 B` por operación en la
referencia CPU Flight 7. `HUDController` conserva input y throttle por frame, pero captura
el snapshot y actualiza sus paneles a 30 Hz; cambios de nave, fase o vista invalidan la
cadencia inmediatamente. El toast mantiene su temporizador wall-clock.

La suite completa pasa `696/696`, el build y startup pasan, y el contrato de cadencia cubre
la frontera. El A/B de framebuffer continúa bloqueado por Xvfb; por tanto, la decisión es
CPU/presentación y no una afirmación de FPS. Reporte: `PERF_HUD_MAIN_CADENCE_PHASE53_REPORT.md`.

## Resultado de la fase 54 — autoridad orbital única en el snapshot

El HUD ya no recalcula `OrbitalElements.FromStateVector` para obtener el tiempo a periapsis:
`FlightHudPresenter` entrega `TimeToPeriapsisS` junto con apoapsis/periapsis y
`HUDController` consume ese dato. La prueba focalizada verifica finitud y paridad con
`MissionPhaseTrack`; el contrato rechaza cualquier llamada orbital residual en el HUD.

La suite completa pasa `696/696`, builds secuenciales y startup pasan. El benchmark aislado
del presenter incluye ahora el dato adicional y por eso no se usa como mejora; la decisión
se basa en quitar la segunda resolución confirmada en la ruta Godot. El framebuffer/GPU sigue
pendiente por Xvfb. Reporte: `PERF_HUD_ORBITAL_DEDUP_PHASE54_REPORT.md`.

## Resultado de la fase 55 — invalidaciones visuales con frontera explícita

El HUD conserva el último modo de navegación, fase/estado de entrada y vista/densidad
aplicados. Sólo cambios reales reaplican estilos, colores, visibilidad, `ProcessMode` y
repintado de los puntos de fase; F3 invalida la densidad de forma inmediata. La suite
`696/696`, build, startup y contratos pasan. No se declara FPS sin framebuffer válido.
Reporte: `PERF_HUD_INVALIDATION_CACHE_PHASE55_REPORT.md`.

## Resultado de la fase 35 — vistas estables y consumo sin boxing por tick

La auditoría del camino de entrada al nivel encontró que `Universe.Bodies`, `Universe.Vessels`
y `Universe.DockingConnections` ejecutaban `AsReadOnly()` en cada lectura. Estas propiedades
son consultadas por `SimulationBridge`, HUD, mapa, cielo, comunicaciones y controladores de
vuelo; por tanto, el wrapper podía convertirse en garbage repetitivo incluso cuando el
contenido de la simulación no cambiaba.

`Universe` ahora crea una sola vista read-only por lista en su constructor y expone siempre la
misma referencia. La semántica permanece igual: los callers siguen viendo una colección no
mutable y las mutaciones pasan únicamente por `AddBody`, `AddVessel` y las operaciones
autoritativas existentes. No se cambiaron dispatches, cadencias, integración RK4, rails ni
condiciones de wake-up.

La regresión `UniverseCollectionViewsAreStableAndAllocationFreeAfterConstruction` verificó
identidad estable y `0` bytes asignados al realizar 10.000 lecturas de cada colección. La
auditoría del fixture Flight 7 encontró además el último boxing: el reparto de propelente
recorría `tankPool` como `IReadOnlyList<Part>` mediante `foreach`. Se sustituyó por un loop
indexado, sin cambiar la distribución de LF/Ox ni las reglas de starvation.

La revisión del scheduler encontró dos consultas adicionales de `ActiveEngines.Any(...)` en
la selección de cap y en `RequiresOffRailsPhysics`. Ambas usan ahora el mismo buffer concreto
con un loop indexado, manteniendo intactas las reglas de wake-up y rails.

| Escenario | Después fase 34 | Después fase 35 | Reducción |
|---|---:|---:|---:|
| Motores apagados | 0 B/tick | 0 B/tick | 0.00% |
| Motores encendidos | 40 B/tick | 0 B/tick | 100.00% |
| Motores encendidos + TVC | 40 B/tick | 0 B/tick | 100.00% |

La prueba focalizada pasó `20/20`, el contrato de optimización pasó `38/38`, y la suite
completa pasó `603/603`. El benchmark standalone fue reproducido por la auditoría delegada
con `8/8 PASS`; los intentos locales que coincidieron con el primer perfilado fueron
bloqueados por el socket de VSTest del host (`Permission denied`), no por una aserción de
simulación.

Decisión: promover las tres correcciones. El desglose Flight 7 queda en `0 B/tick` en los tres
escenarios medidos; no se cambia scheduler, cadencia ni hibernación. El siguiente perfilado
debe concentrarse en tiempo de CPU, GPU y allocations fuera de este fixture.
Informe reproducible: `PERF_UNIVERSE_COLLECTION_VIEWS_PHASE35_REPORT.md`.

## Resultado de la fase 34 — enumeración interna de partes sin boxing por tick

El residual después de la fase 33 era `80–120 B/tick` en el fixture Flight 7. La revisión de
los callers internos encontró tres recorridos de `Parts.Parts`, cuya superficie pública es
`IReadOnlyList<Part>`: autoridad estructural, área efectiva de paracaídas y centro
aerodinámico/flaps. En cada `foreach`, la enumeración por interfaz podía boxear el enumerador
de la lista concreta durante el tick.

Se añadió `PartGraph.PartList` como acceso interno al buffer estable `_parts` y se migraron
únicamente esos recorridos de física. La fachada pública `Parts` conserva su tipo
`IReadOnlyList<Part>` y no cambia la semántica para HUD, tests, staging ni callers externos.
El recorrido de teletransporte, las consultas LINQ de presentación y las operaciones de
staging permanecen fuera de este cambio por no ser hot paths del tick.

| Escenario | Después fase 33 | Después fase 34 | Reducción fase 34 |
|---|---:|---:|---:|
| Motores apagados | 80 B/tick | 0 B/tick | 100.00% |
| Motores encendidos | 120 B/tick | 40 B/tick | 66.67% |
| Motores encendidos + TVC | 120 B/tick | 40 B/tick | 66.67% |

La prueba focalizada pasó `19/19`, la suite completa `602/602`, y el contrato de hot paths
pasó. El límite de la regresión sigue siendo `<=1,000 B/tick`; el cambio no toca la cadencia
del scheduler, la hibernación, los deadlines ni las fórmulas aerodinámicas. El residual de
`40 B/tick` con motores activos queda documentado como siguiente objetivo de perfilado, no
como justificación para degradar la física.

Decisión: promover el buffer concreto de topología. La API pública permanece compatible.
Informe reproducible: `PERF_PART_ENUMERATION_PHASE34_REPORT.md`.

## Resultado de la fase 33 — enumeración interna de motores sin boxing por tick

El residual después de la fase 32 era `240–360 B/tick`. La causa restante era que los callers
internos recorrían `PartGraph.ActiveEngines`, cuya firma pública devuelve `IEnumerable<Part>`.
Aunque el almacenamiento real era un `List<Part>` reutilizable, cada `foreach` desde esa
interfaz podía boxear el enumerador struct de la lista.

Se añadió `ActiveEngineList` como buffer `List<Part>` interno para la simulación y se migraron
`Vessel.Tick`, `ControlAuthority` y los cálculos de thrust/torque/TVC de `PartGraph`. La API
pública `ActiveEngines` sigue existiendo y delega al mismo buffer, por lo que no se rompe la
compatibilidad de presentation/external callers.

| Escenario | Antes fase 33 | Después | Reducción |
|---|---:|---:|---:|
| Motores apagados | 240 B/tick | 80 B/tick | 66.67% |
| Motores encendidos | 280 B/tick | 120 B/tick | 57.14% |
| Motores encendidos + TVC | 360 B/tick | 120 B/tick | 66.67% |

La cobertura focalizada pasó `19/19` y el desglose mantiene `<=1,000 B/tick` en los tres
escenarios. No cambian selección de etapa, authority, torque, TVC ni telemetría pública.

Decisión: promover el buffer concreto. El residual de `80–120 B/tick` queda dentro del
presupuesto de simulación administrada; no se introducen cadencias reducidas ni hibernación
física.

Informe reproducible: `docs/audits/PERF_ACTIVE_ENGINE_ENUMERATION_PHASE33_REPORT.md`.

## Resultado de la fase 32 — agenda de fallos de motor sin closure por tick

El desglose de Flight 7 runtime mostró un coste persistente incluso con motores apagados:
`3,672 B/tick` sin input, `3,712 B/tick` con motores encendidos y `3,792 B/tick` con TVC.
La causa era `Part.ApplyScheduledFailure`: `List.FindIndex` recibía un predicado que cerraba
sobre el estado de cada motor en cada tick, aunque `_scheduledFailures` estuviera vacío.

La búsqueda ahora recorre la lista por índice y conserva el primer match, las condiciones de
estado/intento/tiempo y la eliminación de una sola inyección antes de `FailEngine`. No se
modifica el orden de fiabilidad ni la transición térmica/ignición.

| Escenario | Antes | Después | Reducción |
|---|---:|---:|---:|
| Motores apagados | 3,672 B/tick | 240 B/tick | 93.46% |
| Motores encendidos | 3,712 B/tick | 280 B/tick | 92.46% |
| Motores encendidos + TVC | 3,792 B/tick | 360 B/tick | 90.51% |

La regresión `RuntimeFlight7AllocationBreakdownReportsControlHotPaths` exige `<=1,000 B/tick`
en los tres escenarios. `EngineReliabilityTests` conserva PASS para fallos programados,
sobretemperatura, reinicios y round-trip de save. El contrato `starship_hotpath_contract_test`
también fue ajustado al límite más estricto.

Decisión: promover el recorrido indexado. El residual de 240–360 B/tick queda dentro del
presupuesto local y no justifica todavía tocar la cadencia del scheduler ni desactivar física.

Informe reproducible: `docs/audits/PERF_ENGINE_FAILURE_SCHEDULE_PHASE32_REPORT.md`.

## Resultado de la fase 31 — performance map de motores sin allocations por muestra

El fixture histórico de `StarshipPerformanceRegressionTests` no resolvía los modelos de motor
runtime y, por tanto, no ejercitaba el coste real de `EnginePerformanceEvaluator`. Se añadió un
fixture Flight 7 construido con `PartCatalog`, que carga los `performanceMap` de los Raptors y
mide el mismo `Vessel.Tick` que usa la simulación.

La auditoría aisló dos fuentes de garbage en ese camino:

- `ConsumePropellantFromPool` recorría `GetEngineTelemetry`, creando el estado del iterador y
  records de presentación para obtener sólo el flujo de masa. La física ahora lee un
  `EnginePerformanceSample` por índice mediante una API interna, dejando la enumeración de
  telemetría para HUD/compatibilidad.
- `EnginePerformanceEvaluator` reconstruía `GroupBy`/`OrderBy`/`Select`/`ToArray` por cada
  instancia y tick. La interpolación de presión y throttle ahora usa un escaneo acotado de la
  lista validada, conservando la regla de límites de la implementación anterior.

Medición A/B del fixture runtime, 32 ticks de warm-up y 128 ticks medidos:

| Estado | Allocations |
|---|---:|
| Antes de la reescritura del `performanceMap` | 141,520 B/tick |
| Después, con `EnginePerformanceSample` y escaneo directo | 3,712 B/tick |
| Reducción | 97.38% |

La prueba del mapa confirma la interpolación de presión/throttle y mide `0 B` en 1,000
muestras después del warm-up. El benchmark scheduler global conservó `mixed_fleet` en
`182,965.6 B/tick`; su p95 fue `4.4282 ms`, dentro de la variación del host y sin cambiar
dispatches, proyecciones ni contratos.

La suite completa pasó `601/601`, los contratos de optimización `34/34`, el build quedó en
`0 warnings / 0 errors` y el startup quick-check pasó. Decisión: promover la ruta de consumo
sin telemetría materializada y la interpolación sin LINQ; mantener el umbral runtime en
`10,000 B/tick` como regresión de seguridad.

Informe reproducible: `docs/audits/PERF_ENGINE_RUNTIME_ALLOCATIONS_PHASE31_REPORT.md`.

## Resultado de la fase 30 — hot-stage sin garbage por tick

La auditoría específica del vuelo Flight 7 aisló una asignación repetida durante el
solapamiento de hot-stage: `PartGraph` reconstruía un `HashSet<Part>` y una `List<Part>` en
cada tick para separar el conjunto inferior del conjunto superior. Se sustituyó esa ruta por
dos buffers privados reutilizables, rellenados por índice, sin cambiar la partición de partes,
el orden de consumo, los límites de combustible ni la física de staging.

La regresión `HotStagePropellantPoolStaysWithinAllocationBudget` calienta 32 ticks, mide 128
ticks y exige una asignación administrada inferior a 800 B/tick, además de masa finita y
positiva. La medición directa de la suite registró:

| Caso | Resultado |
|---|---:|
| Flight 7, 500 ticks | 360.08 B/tick |
| Hot-stage overlap, 128 ticks | 320.00 B/tick |
| `mixed_fleet` benchmark global | 182,965.6 B/tick; p95 4.1550 ms |

El benchmark global no mostró una regresión de allocations ni de contrato después del
cambio. La suite Starship focalizada pasó `6/6`; la suite xUnit completa pasó `599/599`; el
build y `ci_check.sh` terminaron con 0 warnings y 0 errores. La decisión es promover la
reutilización de buffers como optimización local de bajo riesgo y mantener intacta la física.

El wrapper `tools/perf/starship_hotpath_benchmark.sh` no pudo abrir el socket de ejecución de
VSTest (`SocketException: Permission denied`) incluso con build omitido. Esto es una limitación
del harness del host, no un fallo del código: las mismas pruebas filtradas ejecutadas
directamente sí pasaron `6/6`, por lo que no se usa el wrapper como evidencia de rendimiento
negativa.

Informe reproducible: `docs/audits/PERF_STARSHIP_HOTSTAGE_PHASE30_REPORT.md`.

## Resultado de la fase 29 — baseline vigente y bloqueos externos

Se repitió el scheduler después del push de fase 28 con `SAMPLES=80`, `WARMUP=10`:

| Escenario | p95 ms | allocations/tick | dispatches | proyecciones | estado |
|---|---:|---:|---:|---:|---|
| `full_single` | 0.0561 | 2,734.3 B | 1.000 | 0.000 | PASS |
| `rails_fleet` | 0.6375 | 5,931.5 B | 32.000 | 0.000 | PASS |
| `mixed_fleet` | 4.3208 | 182,965.6 B | 450.000 | 396.000 | PASS |
| `wake_catchup` | 1.3597 | 88,216.1 B | 50.013 | 12.375 | PASS |

El runner de EventPipe terminó `BLOCKED_EVENTPIPE` con `BLOCKED_NOT_INSTALLED`. La máquina
no expone `/dev/dri`, no tiene `dotnet-trace` ni `dotnet-counters`, y el socket X11 compartido
es propiedad de `nobody:nogroup`; no se modificaron permisos del sistema ni se forzó una
captura visual insegura. Por tanto, no se promueve una reducción adicional de deadlines,
hibernación por distancia, LOD físico ni cambios de textura en esta fase.

La siguiente acción requiere un host de validación con GPU física, framebuffer controlable y
collectors .NET. Allí se debe ejecutar `scheduler_phase6_benchmark.sh`,
`rails_eventpipe_phase24.sh`, `--orbital-reentry`, `--edl`, `--cockpit` y la matriz Earth/
Mars/Venus antes de abrir otra modificación de runtime.

## Resultado de la fase 26

La corrección SOI quedó protegida con cobertura permanente y el preflight externo se
cerró sin asumir capacidades inexistentes:

- `OrbitalElementsRoundTripTests`: 8 casos PASS para cuatro cuadrantes retrógrados,
  circular retrógrada, prograde e inclinadas;
- preflight display/GPU/EventPipe: `BLOCKED` por X11/Wayland, `/dev/dri` y ausencia de
  `dotnet-trace`/`dotnet-counters`;
- no se modificaron runtime adicional, scheduler, harness ni configuración del host;
- la matriz Mars/Venus sigue requiriendo un host válido para producir las seis capturas.

El siguiente trabajo que requiere estado externo es ejecutar
`--atmosphere-bodies` y `rails_eventpipe_phase24.sh` en una máquina con display/GPU y
collectors instalados. Hasta entonces, el fallback CPU y los gates fail-closed son la
única evidencia aceptable.

## Resultado de la fase 25

La divergencia SOI quedó corregida en su origen matemático, sin introducir un fallback
numérico en el scheduler:

- causa: órbitas ecuatoriales retrógradas (`i = π`) almacenaban el argumento de periapsis
  con el signo incompatible con la matriz perifocal de `MathUtils`;
- corrección: `OrbitalElements.FromStateVector` usa `-atan2(eᵧ, eₓ)` sólo cuando
  `h.Z < 0` y `|n|/|h| ≤ 1e-12`; órbitas prograde e inclinadas no entran en la rama;
- equivalencia SOI: `1/1 PASS`, posición y velocidad dentro de las tolerancias `1e-6 m` y
  `1e-9 m/s`;
- regresión scheduler: `19/19 PASS`; suite completa: `576/576 PASS`;
- el fallback RK4 experimental de N10 fue descartado por coste y porque habría ocultado el
  defecto de representación.

Informe: `PERF_RAILS_ORBITAL_ELEMENTS_FIX_PHASE25_REPORT.md`.

La siguiente etapa vuelve a ser externa al código: repetir la matriz framebuffer Mars/Venus
en un host con X11/GPU funcional y obtener un perfil EventPipe real antes de optimizar las
396 proyecciones/tick de `mixed_fleet`.

## Resultado de la fase 24

La siguiente etapa quedó instrumentada y cerrada con tres bloqueos explícitos; no se
promovió una optimización de runtime sin trazas ni equivalencia:

| Línea | Resultado | Decisión |
|---|---|---|
| Rails/EventPipe | `dotnet-trace` y `dotnet-counters` no están instalados; fallback Phase 23 PASS (`rails_fleet` p95 0.754 ms, `mixed_fleet` 727 KiB/tick) | mantener el candidato de caché de proyecciones en investigación; no cambiar frecuencia ni hibernación |
| Mars/Venus visual | harness nuevo con 6 casos (`--atmosphere-bodies`), Earth intacto; Xvfb bloqueó la corrida real | aceptar el gate sintético; exigir seis capturas en un host con display/GPU válido |
| Equivalencia rails/SOI | deadline, wake-up, atmósfera y contacto pasan; el cruce SOI diverge 6,082.76 m y 2,000 m/s | bloquear optimización SOI; diagnóstico marcado `Skip` para mantener CI verde hasta triagear `Universe` |

Verificación de esta fase:

- contratos Mars/Venus y Phase 23: `34/34 PASS`;
- contrato EventPipe fail-closed: `PASS`;
- pruebas scheduler focalizadas: `18 PASS, 1 SKIP` por la divergencia SOI documentada;
- build del juego: 0 warnings, 0 errors;
- no se declaró `MARS_VENUS_OK`, `EVENTPIPE_OK` ni una mejora de FPS.

Informes: `PERF_RAILS_EVENTPIPE_PHASE24_REPORT.md`,
`PERF_ATMOSPHERE_MARS_VENUS_PHASE24_REPORT.md` y
`PERF_RAILS_EQUIVALENCE_PHASE24_REPORT.md`.

La siguiente acción prioritaria ya no es reducir trabajo: es corregir o explicar el cambio
de marco inercial en el cruce SOI y volver a ejecutar la prueba sin `Skip`. Sólo después
de esa equivalencia se puede evaluar una reutilización de proyecciones en `mixed_fleet`.

## Resultado de la oleada 2

La segunda oleada se cerró el 2026-08-14 con medición reproducible y sin promover cambios
físicos por intuición:

| Agente | Resultado | Decisión |
|---|---|---|
| N1 allocations/tick | benchmark separado con 256 muestras y 32 warm-up; `mixed_fleet` ~727 KiB/tick, `rails_fleet` ~190 KiB/tick, Flight 7 directo ~3,976 B por `Vessel.Tick`; snapshot de telemetría ~0 B material | aceptar la instrumentación; no optimizar runtime hasta perfilar rails/proyecciones con equivalencia |
| N2 atmósfera visual | 8/20 casos Earth preservados; `--verify-only` rechazó la matriz incompleta; Xvfb no permitió cerrar la corrida y Mars/Venus no existen aún en el modo visual | `INCOMPLETE/BLOCKED`; no declarar `ATMOSPHERE_OK` |
| N3 GPU física | Godot observó Mesa llvmpipe; `real_gpu_observed=false`; sólo `8k_nomip` hizo preflight | `BLOCKED`; no publicar FPS/VRAM ni promover textura |

Gates de la oleada:

- benchmark de asignaciones: `PASS`, cinco escenarios finitos y un catch-up determinista;
- xUnit: `571/571`, sin fallos;
- contratos Phase 23: `25/25`, además de contratos atmosférico, render y visual en verde;
- builds de herramientas: 0 warnings, 0 errores;
- GPU física y matriz atmosférica completa: no aprobadas por evidencia insuficiente del entorno.

Informes: `PERF_ALLOCATIONS_TICK_PHASE23_REPORT.md`,
`PERF_ATMOSPHERE_FULL_PHASE23_REPORT.md` y `PERF_TEXTURE_GPU_MATRIX_PHASE23_REPORT.md`.

La siguiente etapa debe atacar el coste dominante medido en `rails_fleet`/`mixed_fleet`
con un perfil EventPipe o `dotnet-trace` y una prueba de equivalencia de wake-up; no debe
empezar por hibernación física global. En paralelo, el harness atmosférico debe incorporar
Mars/Venus y recuperar un display X11 funcional antes de repetir la matriz completa.

## Resultado de la oleada 1

La primera oleada se integró en `25f6bde` después de tres commits de agentes y una revisión
central:

| Agente | Resultado | Decisión |
|---|---|---|
| P1 scheduler | 4 tests de wake-up y benchmark `scheduler_phase23_v1` con ventana de dispatch/proyección/catch-up | aceptar como instrumentación y cobertura; no se alteró `Universe.cs` |
| P2 allocations | `FillEngineReadouts` de 73.656 B a 104 B por muestra estable; engine-out invalida cache | promover; `Vessel.Tick` permanece sin mejora y sin regresión medida |
| P4 render | early-out térmico para renderers sin materiales Starship | promover; Starship conserva ruta térmica completa |
| P5 atmósfera | cache key completo y telemetría de cancelación/bytes/cola | promover; orden 4 oficial, orden 5 offline |
| P6 QA | 22 pruebas focalizadas y 25 gates con fixtures inválidos | promover como barrera de integración |

Evidencia de integración:

- build y suite completa: `571/571`, 0 warnings, 0 errors;
- `ci_check.sh`: contratos anteriores y phase23 PASS;
- `atmosphere_quick_check`: `81/81 PASS`;
- contratos P1/P4/P5/P6: PASS;
- GPU física: `BLOCKED` por llvmpipe;
- validación espectral: finita y monótona, con Venus todavía `order4NoWorse=False`;
- ascenso post-integración: pre-launch `0/33` sin rojo y liftoff/Max-Q `runningEngines=33`,
  `failedEngines=0`; la corrida fue cancelada antes de órbita por el coste del framebuffer
  software, por lo que no se etiqueta como un nuevo `ASCENT_ORBIT_OK`.

La siguiente oleada debe medir una mejora de tiempo real en hardware objetivo antes de
promover scheduler por deadlines más agresivo, reducción de texturas o cambios de calidad.

## Objetivo

Hacer que el vuelo sandbox sea más fluido sin ocultar trabajo físico que todavía pueda
afectar a la nave activa, los contactos, el staging, la atmósfera, la termodinámica, la SOI,
el docking o los palillos de Starship. La fase se divide por dominio para que cada agente
pueda medir y cambiar una superficie pequeña; ningún agente puede promocionar una hipótesis
de rendimiento sin una medición comparable y una prueba de equivalencia.

La regla principal es: primero medir el workload y sus deadlines; después reducir trabajo
demostrablemente redundante. No se introducirá un `continue` por distancia, un tick de baja
frecuencia ni paralelización de `Vessel.Tick` sólo porque parezca intuitivamente barato.

## Baseline que todos deben usar

La base funcional está verde. El conteo vigente después de fase 33 es:

- build Godot/.NET: 0 warnings, 0 errors;
- xUnit: 602/602;
- ascenso Flight 7: `ASCENT_ORBIT_OK`, `33/33` al liftoff, `39/39` en hot-stage;
- EDL Starship: `CAUGHT`, dos pasadores, `relativeSpeed=0.030`, `angularSpeed=0`;
- salto a Saturno: `SATURN_OK`, anillos visibles;
- GPU física: bloqueada en este host; el backend observado es Mesa llvmpipe.

El benchmark de esta tabla es el baseline histórico previo a fase 28. El baseline vigente
queda registrado en `PERF_SIMULATION_ALLOCATIONS_PHASE28_REPORT.md` y se repitió en fase 29.
Benchmark histórico reproducido el 2026-08-14 con .NET 8, `SAMPLES=80`, `WARMUP=10`:

| Escenario | Rama | p50 ms | p95 ms | p99 ms | alloc/tick | trabajo |
|---|---|---:|---:|---:|---:|---:|
| `full_single` | FullPhysics | 0.0386 | 0.0486 | 0.0667 | 6,059 B | 1 full |
| `full_fleet` | FullPhysics | 0.1207 | 0.1423 | 0.2458 | 19,971 B | 4 full |
| `rails_fleet` | Rails | 0.6000 | 0.7268 | 1.1361 | 190,078 B | 32 rails / 640 slices |
| `mixed_fleet` | Mixed | 2.9707 | 3.7288 | 4.1200 | 718,566 B | 450 dispatches / 25 outer |

Reproducir:

```bash
OUT_DIR=/tmp/exo_phase23_scheduler_baseline \
SAMPLES=80 WARMUP=10 \
bash tools/perf/scheduler_phase6_benchmark.sh
```

Estos números son comparativos de esta máquina. No son un presupuesto de GPU ni una
promesa de FPS para hardware de jugador.

## División de agentes y ownership

Cada agente debe crear una rama y un worktree propios desde `main` actualizado. Las salidas
van a `/tmp/exo_phase23_<agent-id>/`; nunca se comparten `project.godot`, autoloads
temporales ni archivos `scripts/_*Shot.cs`.

### Agente P0 — coordinador y baseline

Ownership: `docs/audits/`, manifiestos de ejecución y revisión de integración; no modifica
la física mientras otros agentes trabajan.

Trabajo:

- ejecutar `ci_check.sh`, benchmark scheduler y la matriz visual mínima;
- conservar los logs y hashes de commit de cada agente;
- verificar que cada resultado compare exactamente el mismo escenario, samples y backend;
- integrar sólo commits que traigan pruebas, métricas y rollback claro.

Entrega: `phase23-baseline.tsv`, índice de artefactos y reporte final de decisión.

### Agente P1 — scheduler físico y deadlines

Ownership: `ExosphereSimulation/Universe.cs`, `ExosphereSimulation/Physics/`,
`tools/SchedulerBenchmark/`, tests `PhysicsScheduler*`.

Hipótesis: el escenario `mixed_fleet` repite exploración global y slices rails durante
25 substeps. Ya existe un deadline conservador para coasting analítico; hay que comprobar
si su política de wake-up puede reducir inspecciones sin atrasar estados públicos.

Permitido:

- agrupar comprobaciones por deadline sólo para coasting analítico fuera de atmósfera,
  contacto, thrust, staging, docking y SOI;
- mantener `LastSimulatedTime`, proyección finita y catch-up antes de cualquier fuerza;
- añadir contadores deterministas de dispatch, proyección, catch-up y motivo de wake.

Prohibido:

- saltar posición/velocidad de una nave activa;
- diferir una nave dentro de `radius + atmosphere corridor`;
- paralelizar `Vessel.Tick` sin demostrar ausencia de escritura compartida y equivalencia;
- cambiar límites de warp o integrador RK4 en esta entrega.

Gates: equivalencia posición ≤0.1 mm y velocidad ≤1 nm/s en órbita diferida; casos de
atmósfera, impacto, docking, staging, SOI, thermal y catch-up; `mixed_fleet` debe reducir
p95 al menos 10% o mostrar una reducción explícita de dispatches sin empeorar `full_single`
más de 2%.

### Agente P2 — allocations y hot paths .NET

Ownership: `ExosphereSimulation/Vessel.cs`, `PartGraph.cs`, HUD telemetry y benchmarks
de asignación; no cambia fórmulas físicas ni contratos públicos salvo un buffer explícito.

Trabajo:

- medir `GC.GetAllocatedBytesForCurrentThread` por tick y separar simulation, telemetry y
  renderer;
- localizar LINQ, enumeraciones, snapshots, strings y arrays en `Vessel.Tick`, motores,
  `FillEngineReadouts` y `Universe.Tick`;
- reemplazar sólo allocations demostradas por buffers reutilizados, límites fijos o
  iteración indexada;
- añadir pruebas de finitud y concurrencia de buffers si el renderer consume snapshots.

Gates: no cambia el resultado determinista de una corrida de 10 s; reduce allocations del
escenario elegido ≥20% o documenta por qué no es seguro; no aumenta p95 de CPU; xUnit y
contratos hot-path en verde.

### Agente P3 — render, texturas y memoria

Ownership: `tools/perf/texture_gpu_matrix.sh`, imports en worktree temporal, shaders/materiales
sólo para medición; no promueve cambios de producción en este host.

Trabajo:

- repetir matriz 8K sin mip, 8K mip, 4K mip y 2K mip en GPU física con Vulkan/OpenGL;
- separar RSS, caché importado, render CPU/GPU, draw calls/primitivas y memoria de vídeo
  cuando la API del backend lo exponga;
- evaluar visualmente Earth, starmap, Saturn rings, terminador, eclipse y navegación;
- si no hay GPU física, dejar el estado `BLOCKED` y no inferir VRAM/FPS desde llvmpipe.

Gates: variante candidata debe conservar los gates visuales y medir reducción real de memoria
o frame p95; no se modifica la resolución oficial sólo por tamaño JPEG. La promoción queda
prohibida mientras `physical_gpu_gate != PASS`.

### Agente P4 — cadence y visibilidad Godot

Ownership: `scripts/CockpitInstruments.cs`, `VesselRenderer.cs`, `CameraController.cs`,
`Construction*`, viewport policies y contratos de render.

Trabajo:

- medir Flight exterior, cockpit, VAB y mapa por separado;
- comprobar que un renderer oculto no procesa, que un cockpit fuera de vista no actualiza
  sus subviewports y que el mapa no redibuja por eventos inexistentes;
- proponer cadencias limitadas sólo para paneles que no sean controles de vuelo críticos;
- no apagar el vessel activo, sky, horizonte, engine plume ni cues de reentrada por distancia
  sin captura comparativa.

Gates: p95/p99 de frame y número de refresh por escenario; ningún panel de navegación pierde
un evento de input; smoke, cockpit, VAB y EDL conservan PNG y telemetría válidos.

### Agente P5 — atmósfera y workers

Ownership: `SkyController.cs`, `AtmosphereLut*`, `SpectralAtmosphereOracle`, contratos
`tools/atmosphere_quick_check.sh` y visual atmosphere/spectral.

Trabajo:

- medir cola, tiempo de worker, bytes CPU retenidos, upload y cancelación al salir de escena;
- revisar si la reconstrucción espectral/oráculo sólo corre en harness y nunca en el frame
  de juego;
- validar cache key por perfil, resolución y orden; mantener renderer RGB orden 4;
- no mover trabajo costoso al hilo principal y no promover orden 5 automáticamente.

Gates: transmitancia y RGB finitos/no negativos; quick-check atmosférico; matrices day,
terminator, eclipse y night sin `neonGreenFrac` ni clipping anómalo; shutdown sin worker
huérfano.

### Agente P6 — QA de equivalencia y visual

Ownership: `ExosphereSimulation.Tests/`, contratos `tools/tests/`, validación final de
`tools/visual_playtest.sh` y documentación de tolerancias.

Trabajo:

- congelar fixtures de una nave, dos naves, flota rails y mixed;
- comparar seed, posición, velocidad, masa, propellant, engine failures, contacts y mission
  phase antes/después;
- ejecutar `--ascent --flight7`, `--edl`, `--saturn`, `--cockpit`, `--atmosphere` y
  `--reentry-compare` con IDs aislados;
- rechazar cualquier mejora que sólo reduzca trabajo porque dejó de simular un evento.

Entrega: tabla PASS/FAIL/SKIP por gate, capturas y lista de regresiones reproducibles.

## Orden de integración

1. P0 publica baseline y congela hashes.
2. P2 y P4 pueden trabajar en paralelo: no comparten archivos con P1.
3. P1 entrega scheduler sólo después de que P6 tenga fixtures de equivalencia.
4. P3 permanece en medición hasta tener GPU física; su rama no bloquea cambios CPU seguros.
5. P5 se integra después de P1/P2 para no mezclar coste de worker con coste de simulación.
6. P6 corre gates completos sobre cada candidato.
7. P0 integra en commits separados: scheduler, allocations, render cadence, atmosphere,
   tests/docs. Si un commit falla un gate, se revierte sólo ese commit.

## Gates de promoción de la fase

Un cambio entra a `main` sólo si cumple todos los aplicables:

- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore`;
- `bash tools/ci_check.sh`;
- startup smoke PASS;
- ascent estable `ASCENT_ORBIT_OK`;
- EDL termina en `CAUGHT` con dos pins o `LANDED` con contactos físicos válidos;
- salto `J`/`JumpToBody` conserva órbita, orientación finita, throttle 0 inicial y no
  deja piloto ejecutando comandos residuales;
- ninguna radiancia, velocidad, masa, energía o contacto NaN/negativa;
- no regresión visual amplia en clipping, exposición, estrellas, terminador o chopsticks;
- benchmark comparativo y decisión explícita: promover, conservar o descartar.

Si el host no expone GPU física, el gate de GPU queda `BLOCKED`, nunca `PASS`, y la entrega
puede incluir sólo optimizaciones CPU/render lógicamente verificables.

## Siguiente ejecución recomendada

```bash
git status --short --branch
OUT_DIR=/tmp/exo_phase23_scheduler_baseline \
  SAMPLES=80 WARMUP=10 bash tools/perf/scheduler_phase6_benchmark.sh
bash tools/ci_check.sh
```

Después del baseline, lanzar P1/P2/P4/P5/P6 en worktrees separados y reservar P3 para una
máquina con GPU física. La primera propuesta de implementación debe ser la de menor riesgo:
reducir allocations medidas y trabajo de presentación fuera de pantalla; la hibernación
física por distancia queda fuera hasta que exista una matriz completa de wake-up/eventos.

En la siguiente iteración P1 debe medir el coste de la instrumentación `sample_window` sin
confundirlo con una mejora del runtime; P2 debe separar allocations del tick físico de las
lecturas HUD; P3 debe repetir la matriz en GPU física; P6 debe completar los casos atmosféricos
de 400 km y la matriz visual completa antes de marcar la fase como cerrada.
