# Fase 29 — baseline vigente y gates externos

## Baseline CPU

Comando:

```text
OUT_DIR=/tmp/exo_phase29_scheduler_baseline SAMPLES=80 WARMUP=10 \
bash tools/perf/scheduler_phase6_benchmark.sh
```

| Escenario | p50 ms | p95 ms | p99 ms | allocations/tick | dispatches | proyecciones | estado |
|---|---:|---:|---:|---:|---:|---:|---|
| `full_single` | 0.0381 | 0.0561 | 0.0857 | 2,734.3 B | 1.000 | 0.000 | PASS |
| `full_fleet` | 0.1209 | 0.1412 | 0.1626 | 9,403.5 B | 4.000 | 0.000 | PASS |
| `rails_fleet` | 0.5344 | 0.6375 | 0.8432 | 5,931.5 B | 32.000 | 0.000 | PASS |
| `mixed_fleet` | 3.5519 | 4.3208 | 5.4457 | 182,965.6 B | 450.000 | 396.000 | PASS |
| `wake_catchup` | 0.9252 | 1.3597 | 1.4351 | 88,216.1 B | 50.013 | 12.375 | PASS |

Todos los estados fueron finitos y el contrato de catch-up pasó. El p95 fluctúa por el
backend y la carga del host; no se interpreta como una regresión frente a fase 28 porque
las allocations permanecen estables (`182,965.4` → `182,965.6 B/tick`).

## EventPipe

Comando:

```text
OUT_DIR=/tmp/exo_phase29_eventpipe SAMPLES=80 WARMUP=10 \
bash tools/perf/rails_eventpipe_phase24.sh
```

Resultado: `BLOCKED_EVENTPIPE baseline=PASS reason=BLOCKED_NOT_INSTALLED`.

## Entorno visual/GPU

| Gate | Evidencia | Decisión |
|---|---|---|
| GPU física | `/dev/dri` no existe | BLOCKED |
| EventPipe | `dotnet-trace` y `dotnet-counters` ausentes | BLOCKED |
| X11 nuevo | `/tmp/.X11-unix` `nobody:nogroup` | BLOCKED, sin cambiar permisos |
| CPU scheduler | benchmark finito y contrato verde | PASS |

No se modificó el sistema anfitrión ni se declaró PASS visual con renderer dummy. El siguiente
host debe aportar el adaptador físico, el display y los collectors antes de medir FPS, VRAM,
frame p95 o cerrar la reentrada orbital normal con captura framebuffer.
