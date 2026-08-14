# Auditoría N1 — asignaciones por tick y telemetría, fase 23

Estado: **PASS** en el alcance de benchmark. No se modificó runtime de producción, física,
renderer ni `project.godot`.

Fecha: 2026-08-14
Runtime: .NET 8.0.29
Muestras: 256 por escenario
Warm-up: 32 ticks
Contador: `GC.GetAllocatedBytesForCurrentThread`

## Objetivo y método

La auditoría separa cuatro fuentes que `Universe.Tick` mezcla normalmente:

1. `universe_tick`: tick real del scheduler con la flota del escenario.
2. `vessel_tick`: llamadas directas al API público `Vessel.Tick`. Es una medida por llamada,
   no una reproducción completa del scheduler. En `rails_fleet` es `NOT_APPLICABLE`, porque
   ese camino no despacha `Vessel.Tick`.
3. `scheduler_empty`: `Universe.Tick` sin cuerpos ni vessels. Es una cota del envoltorio
   scheduler/telemetría, no una simulación de gameplay.
4. `scheduler_telemetry_snapshot`: copia y consumo de `LastSchedulerTelemetry`, un
   `readonly record struct`, sin ejecutar otro tick.

Cada muestra se mide en el mismo hilo administrado. Se registran p50/p95/p99, desviación
estándar, CV, bytes medios por operación y colecciones GC. Los tiempos son diagnósticos de
esta máquina: no equivalen a FPS y no incluyen allocations nativas de Godot, GPU, renderer,
audio ni memoria del proceso fuera del hilo medido.

## Comando reproducible

```bash
OUT_DIR=/tmp/exo_phase23_n1_canonical_v2 \
SAMPLES=256 WARMUP=32 \
bash tools/perf/allocations_tick_phase23_benchmark.sh
```

Resultado: `allocations_tick_phase23_benchmark: PASS`; el TSV queda en
`/tmp/exo_phase23_n1_canonical_v2/allocations_tick_metrics.tsv`.

## Resultados del tick real

| Escenario | p50 ms | p95 ms | p99 ms | CV | Alloc B/tick | Dispatches/tick | Full physics | Rails | Proyecciones | Catch-up |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `full_single` | 0.040807 | 0.050425 | 0.062127 | 13.47% | 5,961.3 | 1.000 | 1.000 | 0.000 | 0.000 | 0 |
| `full_fleet` | 0.118062 | 0.140555 | 0.150874 | 8.61% | 19,952.5 | 4.000 | 4.000 | 0.000 | 0.000 | 0 |
| `rails_fleet` | 0.615087 | 0.764358 | 1.023696 | 16.51% | 190,057.3 | 32.000 | 0.000 | 32.000 | 0.000 | 0 |
| `mixed_fleet` | 2.937387 | 3.229447 | 3.316941 | 5.07% | 727,113.1 | 450.000 | 48.988 | 401.012 | 396.000 | 0 |
| `wake_catchup` | 0.858305 | 1.147889 | 1.252757 | 58.59% | 132,925.3 | 36.098 | 21.766 | 12.504 | 12.375 | 1 |

Los contadores de ventana fueron finitos y válidos en los cinco escenarios. La variabilidad
alta de `wake_catchup` es esperada: se inyecta un wake-up determinista a mitad de la ventana.
El contrato registró exactamente un catch-up (`sample_window_deadline_catchup=1`).

## Descomposición de asignaciones

| Escenario | `Universe.Tick` B | `Vessel.Tick` B/llamada | Scheduler vacío B/tick | Snapshot telemetría B/copia |
|---|---:|---:|---:|---:|
| `full_single` | 5,961.3 | 504.0 / 1 | 368.0 | ~0.002 |
| `full_fleet` | 19,952.5 | 466.0 / 4 | 368.1 | ~0.002 |
| `rails_fleet` | 190,057.3 | N/A | 368.1 | ~0.002 |
| `mixed_fleet` | 727,113.1 | 468.0 / 2 | 368.1 | ~0.002 |
| `wake_catchup` | 132,925.3 | 504.3 / 1 | 368.1 | ~0.002 |

La copia de telemetría observó aproximadamente 0.002 B por copia al amortizar el contador
total sobre 1,048,576 lecturas; p50/p95/p99 de allocation permanecieron en ese orden y no
hubo colecciones GC. Es un residuo fijo del harness, no una allocation material del snapshot
value-type.

Como control de nave real, el mismo benchmark midió una pila Flight 7 directamente por
`Vessel.Tick`: 3,976 B/tick, p50 0.013285 ms, p95 0.017984 ms, p99 0.024526 ms y CV
15.74%. La lectura estable de `Vessel.FillEngineReadouts` en esa pila dio 872 B/lectura;
la consulta de etapa actual es una ruta de instrumentación distinta del `PartGraph` de
booster aislado auditado previamente.

## Interpretación rigurosa

- La telemetría publicada no es la causa de un bloqueo: su copia no crea objetos observables.
- En `rails_fleet` no se ejecuta `Vessel.Tick`; los ~190 KiB/tick pertenecen a propagación
  analítica, slices y estructuras temporales del camino rails.
- En `mixed_fleet` hay aproximadamente 49 trabajos de física completa y 401 de rails por
  tick. Los ~727 KiB/tick están dominados por scheduler mixto, rails, proyecciones e
  integración repetida, no por la copia de telemetría.
- El `Vessel.Tick` simple cuesta aproximadamente 0.47–0.50 KiB por llamada en estos
  fixtures. Una Flight 7 completa sube a ~3.98 KiB por tick por motores, gimbal,
  propelente y aero; es un objetivo de profiling posterior, no una autorización para
  eliminar física a ciegas.
- El baseline `scheduler_empty` demuestra un coste fijo pequeño (~368 B), pero no permite
  atribuir el residuo completo de `Universe.Tick` a una única función: cuerpos,
  clasificación, propagación, listas de trabajo y cada vessel participan.
- La diferencia entre `vessel_tick` directo y el tick real no debe restarse como si fuera
  una identidad exacta: el tick real también actualiza cuerpos, scheduler, rails, deadlines
  y telemetría.

## Decisión y límites

Resultado de esta subfase: **PASS para medición; sin optimización de runtime promovida**.

No es seguro dormir un vessel sólo porque hoy esté en rails: wake-up, periapsis atmosférico,
contacto, staging, SOI y fuerzas externas deben seguir bajo autoridad del scheduler. La
siguiente optimización debe instrumentar motivos de wake-up y hot paths con EventPipe o
`dotnet-trace`, repetir esta matriz antes/después y añadir una prueba de equivalencia física.

Esta auditoría no demuestra FPS, memoria GPU, coste de render ni allocations nativas de
Godot. Esos indicadores requieren perfiles de proceso y hardware separados.

## Validación focal

- `dotnet build tools/SchedulerBenchmark/SchedulerBenchmark.csproj --no-restore`: 0 warnings,
  0 errors.
- `bash tools/perf/allocations_tick_phase23_benchmark.sh`: PASS; cinco escenarios finitos,
  snapshot válido y catch-up cubierto.
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build
  --no-restore --filter FullyQualifiedName~PhysicsSchedulerPerformanceTests`: 14/14 PASS.
- `bash tools/perf/scheduler_phase6_benchmark_contract_test.sh`: PASS.
- `bash -n tools/perf/allocations_tick_phase23_benchmark.sh`: PASS.
- `git diff --check`: PASS.
