# Fase 31 — allocations del rendimiento runtime de motores

Fecha: 2026-08-14  
Área: consumo de propelente, interpolación de `performanceMap` y Flight 7 runtime

## Diagnóstico

La medición anterior de Flight 7 usaba `PartDefinition.LoadAllFromDirectory`, que no resolvía
los modelos de motor y no representaba el camino runtime completo. El nuevo diagnóstico usa
`PartCatalog.LoadFromDirectory`, activa los modelos Raptor y mide el `Vessel.Tick` con 33
instancias runtime.

Con ese fixture, antes de esta fase se medían `141,520 B/tick`. El coste dominante provenía de
`EnginePerformanceEvaluator.Evaluate`: cada llamada agrupaba y ordenaba el mapa mediante LINQ,
materializando arrays temporales para interpolar dos presiones y dos niveles de throttle.
Además, el consumo de propelente recorría `GetEngineTelemetry`, aunque sólo necesitaba el flujo
de masa; ese camino materializaba telemetría de presentación durante la física.

## Implementación

`EnginePerformanceEvaluator` ahora interpola directamente sobre la lista validada mediante
índices y scans acotados. Se mantienen las reglas previas para:

- presión debajo del mínimo y encima del máximo;
- throttle debajo del primer grupo y encima del último;
- interpolación entre grupos y presión;
- mapas con un único punto por grupo.

`Part.GetEngineInstancePerformance` expone internamente el `EnginePerformanceSample` sin crear
`EngineTelemetry`. `PartGraph.ConsumePropellantFromPool` usa esa muestra para los límites de
feed y las demandas líquidas; la API pública `GetEngineTelemetry` no cambia.

## Evidencia

Comandos principales:

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --verbosity minimal
OUT_DIR=/tmp/exo_phase31_alloc_after_engine_map SAMPLES=80 WARMUP=10 \
  bash tools/perf/allocations_tick_phase23_benchmark.sh
bash tools/ci_check.sh
```

Resultados:

| Gate | Resultado |
|---|---|
| Mapa sintético, equivalencia numérica | PASS |
| Mapa sintético, 1,000 muestras | 0 B asignados después de warm-up |
| Flight 7 runtime, 128 ticks | 3,712 B/tick |
| Reducción frente a baseline del fixture | 97.38% |
| Benchmark scheduler global | PASS; `mixed_fleet` 182,965.6 B/tick |
| Suite xUnit | PASS, 601/601 |
| Contratos de optimización | PASS, 34/34 |
| Builds y startup | PASS; 0 warnings, 0 errores |

La regresión runtime exigía inicialmente `<= 10,000 B/tick`; fase 32 la endurece a
`<= 1,000 B/tick`, masa finita y 33 motores activos.
El límite deja margen frente a la variación del host sin permitir que reaparezcan allocations de
órdenes de magnitud mayores.

## Límites y decisión

La medición es CPU administrada y no equivale a FPS: el host no expone `/dev/dri` y Godot usa
llvmpipe. El wrapper histórico de hot-path puede quedar bloqueado por el socket de VSTest del
host; la evidencia de esta fase proviene de la ejecución directa del filtro y de `ci_check.sh`.

Se promueve el cambio porque reduce trabajo medido, preserva la suite física y no cambia la
API de telemetría. No se modifica todavía la frecuencia del scheduler, la hibernación ni la
calidad visual; esos cambios requieren EventPipe y GPU física.
