# Perfilado de rails/projections — Phase 24 / N7

Fecha de ejecución: 2026-08-14 (UTC).

Estado: `BLOCKED_EVENTPIPE`, con fallback determinista Phase 23 en `PASS`.
No se modificó runtime C#, shader ni `project.godot`. No se hizo commit.

## Alcance y regla de validez

El objetivo era separar el coste dominante de `rails_fleet` y `mixed_fleet` en CPU,
allocations y métodos calientes. El benchmark existente permite medir tiempo de tick,
CPU de proceso, allocations administradas y contadores del scheduler. Un nombre de
método caliente requiere EventPipe; no se infiere a partir de p95 ni de allocations.

La medición no traduce milisegundos de simulación a FPS: el harness no es un frame de
Godot, no contiene render ni sincronización de presentación y sus muestras no son una
promesa de rendimiento visual.

## Herramientas y host

| Elemento | Resultado |
|---|---|
| SDK | .NET 8.0.129 |
| Runtime | Microsoft.NETCore.App 8.0.29 |
| Host | Linux 7.0.0-28-generic x86_64, GNU/Linux |
| `dotnet-trace` | No instalado; `command -v` y herramientas globales sin resultado |
| `dotnet-counters` | No instalado; `command -v` y herramientas globales sin resultado |
| EventPipe collector | `BLOCKED_NOT_INSTALLED`; no se generó un perfil |
| Instalación de paquetes | No realizada |

El runner registra estos estados en `matrix.meta`, aplica timeout y conserva el
benchmark determinista cuando EventPipe no está disponible:

```text
/tmp/exo_n7_rails_phase24_final/matrix.meta
/tmp/exo_n7_rails_phase24_final/rails_mixed_metrics.tsv
/tmp/exo_n7_rails_phase24_final/baseline/allocations_tick_metrics.tsv
/tmp/exo_n7_rails_phase24_final/baseline.console.log
```

## Comandos reproducibles

```bash
OUT_DIR=/tmp/exo_n7_rails_phase24 \
SAMPLES=256 WARMUP=32 TIMEOUT_SEC=120 \
bash tools/perf/rails_eventpipe_phase24.sh

bash tools/perf/rails_eventpipe_phase24_contract_test.sh
bash tools/perf/allocations_tick_phase23_contract_test.sh
bash tools/perf/scheduler_phase6_benchmark_contract_test.sh
bash tools/perf/scheduler_phase6_benchmark.sh
```

Resultado del runner:

```text
rails_eventpipe_phase24: BLOCKED_EVENTPIPE baseline=PASS reason=BLOCKED_NOT_INSTALLED
```

El baseline fue ejecutado con 256 muestras y 32 warm-up. Todos los valores finitos y
los contratos de evento del scheduler pasaron.

## Baseline determinista

| Escenario | p50 tick (ms) | p95 tick (ms) | p99 tick (ms) | CPU proceso en ventana (ms) | Alloc/tick (B) | Dispatch/tick | Full/tick | Rails/tick | Proyecciones/tick |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `rails_fleet` | 0.611271 | 0.753960 | 1.032484 | 190.0 | 190,057.344 | 32.000 | 0.000 | 32.000 | 0.000 |
| `mixed_fleet` | 2.886436 | 3.129764 | 3.550075 | 830.0 | 727,113.188 | 450.000 | 48.988 | 401.012 | 396.000 |

El contador `sample_window_rails_slices` del scheduler fue 640.000 por tick en
`rails_fleet` y 5.012 por tick en `mixed_fleet`; se conserva tal como lo expone el
telemetry contract y no se reinterpreta como número de naves ni como FPS.

Observaciones acotadas:

- `mixed_fleet` tiene aproximadamente 14.1 veces más dispatches por tick que
  `rails_fleet`, y 396 proyecciones de deadline por tick.
- Su p95 de tick fue aproximadamente 4.1 veces mayor y sus allocations aproximadamente
  3.8 veces mayores en esta ejecución.
- El `rails_fleet` puro no reportó proyecciones de deadline: su coste está asociado al
  camino de propagación on-rails y sus slices, no a una inferencia de `Vessel.Tick`.
- La variabilidad y el CPU medidos pertenecen al proceso del benchmark; no son una
  medición de render, input, GPU, frame pacing ni experiencia de vuelo.

## Métodos calientes

`NOT_AVAILABLE`.

No hay `dotnet-trace` ni `dotnet-counters` instalados y el benchmark actual termina al
completar sus muestras. No se adjuntó un PID inventado, no se instaló una herramienta y
no se publicó un porcentaje de métodos calientes. El runner marca EventPipe como
`BLOCKED` y, si en otro host existe `dotnet-trace`, aplica un timeout y sólo conserva un
artefacto si el collector produce un archivo no vacío. `dotnet-counters` queda bloqueado
hasta que exista un modo benchmark de larga duración con un PID explícito.

## Candidato de investigación — no promovido

La única hipótesis acotada respaldada por los contadores es revisar el coste de las
proyecciones repetidas de deadline en `mixed_fleet` (396/tick), potencialmente mediante
reutilización de entradas estables. Esto es un candidato de profiling, no un cambio
aprobado: faltan nombres de métodos, porcentajes EventPipe y una prueba de equivalencia
de eventos/estado físico antes y después.

No se propone hibernar física global ni cambiar la frecuencia de rails. Cualquier futura
optimización debe demostrar simultáneamente:

1. reducción de p95 y allocations en el benchmark reproducible;
2. igualdad de dispatches, proyecciones, catch-up y estado físico dentro de tolerancias;
3. ausencia de regresiones en Flight 7, rendezvous, docking, SOI y reentrada;
4. una nueva medición EventPipe con métodos calientes identificados.

## Validación final

- `bash -n tools/perf/rails_eventpipe_phase24.sh`: PASS.
- `rails_eventpipe_phase24_contract_test.sh`: PASS.
- `allocations_tick_phase23_contract_test.sh`: PASS.
- `scheduler_phase6_benchmark_contract_test.sh`: PASS.
- `allocations_tick_phase23_benchmark.sh`: PASS; todos los escenarios finitos.
- `scheduler_phase6_benchmark.sh`: PASS; todos los escenarios finitos.
- `git diff --check`: PASS.
- No se promovió código de runtime ni se declaró FPS.
