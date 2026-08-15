# Plan operativo de optimización multiagente — fase 23

Estado: fase 32 medida; fallos programados de motor sin closures por tick; interpolación de motores y consumo runtime promovidos; hot-stage promovido; gameplay Starship corregido; reentrada normal con gate físico pendiente de framebuffer; display, GPU y EventPipe externos pendientes
Fecha: 2026-08-14  
Base: `main` después de la fase 27; esta corrección añade regresiones de gameplay y reentrada

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

La base funcional está verde. El conteo vigente después de fase 32 es:

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
