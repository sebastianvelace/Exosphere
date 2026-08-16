# Fase 41 — Telemetría de scheduler y contrato de deuda física

Fecha: 2026-08-15  
Área: `Universe.Tick`, `PhysicsSchedulerTelemetry` y `visual_playtest.sh`

## Resultado

Se añadió telemetría aditiva para distinguir:

- `NotInitialized`: todavía no se llamó al scheduler;
- `Paused`: `TimeScale == 0`;
- `InvalidDelta`: delta nulo, negativo, no finito o producto simulado no finito;
- `InvalidTimeScale`: escala negativa, infinita o NaN;
- `None`: tick válido con una rama física ejecutada.

La línea histórica `PERF_FRAME` permanece intacta. Cada frame añade una línea
`PERF_SCHEDULER schema=1` con dispatches físicos, rails, anclados, destruidos, docking,
deadlines y `total_work`. El contrato verifica formato, enteros no negativos, suma de
categorías y coherencia entre rama y razón de skip.

## Evidencia real

Corrida:

```text
OUT_DIR=/tmp/exo_phase41_scheduler_smoke
LOG=/tmp/exo_phase41_scheduler_smoke.log
bash tools/visual_playtest.sh --smoke --run-id phase41-scheduler-smoke --skip-build
```

Resultado:

- `SMOKE_OK` y PNG válida.
- 50/50 `PERF_FRAME` válidas.
- 50/50 `PERF_SCHEDULER` válidas.
- En el pad, `branch=FullPhysics`, `full_physics=0`, `ground_held=8/9` según el frame;
  no se está simulando una nave retenida como dinámica completa.
- Contrato dinámico con `PERF_FRAME_BUDGET_MS=4000`: `47 PASS`, `0 FAIL`, `0 SKIP`.

## Hallazgo de diseño pendiente

`Hibernated` todavía es sólo una clasificación pública; no suspende trabajo. `Universe.Tick`
continúa procesando todo el intervalo solicitado en sus bucles Full/Mixed y Rails. El warning
`CatchUpRisk` informa de muchos subpasos, pero no limita ni conserva una deuda temporal.

No se implementa un cap ingenuo en esta fase: cortar y descartar tiempo podría saltarse
impactos, cruces de SOI, contacto, docking o wake-up. La siguiente fase debe introducir una
deuda exacta, detenerse sólo después de completar un paso global entero y publicar por separado
tiempo solicitado, procesado y pendiente. También debe dividir el camino Rails en slices y
consumir el tiempo procesado en los sistemas de gameplay que hoy reciben sólo `delta`.

## Tests

- `PhysicsSchedulerPerformanceTests`: `22/22 PASS`, incluyendo telemetría inicial, pausa,
  delta inválido, escala inválida y tick válido.
- `performance_acceptance_contract_test.sh`: `47 PASS`, `0 FAIL`, `0 SKIP` con log real.
- `bash -n tools/visual_playtest.sh tools/tests/performance_acceptance_contract_test.sh`:
  PASS.
- Builds C# previos a la corrida: `0 warnings`, `0 errors`.

## Decisión

La telemetría se promueve; no se promueve todavía la hibernación real ni un presupuesto de
catch-up. El gate de promoción será la conservación exacta de tiempo y equivalencia de estado
en impacto, SOI, contacto, docking, staging, cambio de nave activa, teleport y wake-up.
