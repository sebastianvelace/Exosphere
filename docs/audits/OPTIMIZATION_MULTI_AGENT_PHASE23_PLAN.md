# Plan operativo de optimización multiagente — fase 23

Estado: oleada 2 cerrada; gates atmosférico y GPU pendientes
Fecha: 2026-08-14  
Base: `3367186` (`main` limpio después de la auditoría funcional de vuelo)

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

La base funcional está verde:

- build Godot/.NET: 0 warnings, 0 errors;
- xUnit: 559/559;
- ascenso Flight 7: `ASCENT_ORBIT_OK`, `33/33` al liftoff, `39/39` en hot-stage;
- EDL Starship: `CAUGHT`, dos pasadores, `relativeSpeed=0.030`, `angularSpeed=0`;
- salto a Saturno: `SATURN_OK`, anillos visibles;
- GPU física: bloqueada en este host; el backend observado es Mesa llvmpipe.

Benchmark CPU reproducido el 2026-08-14 con .NET 8, `SAMPLES=80`, `WARMUP=10`:

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
