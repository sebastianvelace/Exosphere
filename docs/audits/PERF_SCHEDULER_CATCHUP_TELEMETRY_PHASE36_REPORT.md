# Fase 36 — telemetría de catch-up y entradas inválidas

Fecha: 2026-08-15  
Área: `Universe.Tick`, `PhysicsSchedulerTelemetry` y benchmark del scheduler

## Hallazgo

El scheduler multiplica `realDeltaTime * TimeScale` y procesa todo el intervalo mediante
substeps. Esto conserva la exactitud temporal, pero un hitch grande puede producir un número
de iteraciones que hace parecer congelado el juego. A `x1000`, por ejemplo, `0.5 s` de wall
clock representan `500 s` simulados; con el cap de `2 s`, son `250` substeps. Con contacto,
el cap es `0.005 s` y el coste potencial es mucho mayor.

No se aplicó un límite que descarte tiempo simulado: hacerlo sin una política de deuda temporal
y equivalencia de eventos podría saltar colisiones, entrada atmosférica, SOI o wake-ups.

## Cambio

Se amplió `PhysicsSchedulerTelemetry` con:

```csharp
double WallClockMilliseconds,
bool CatchUpRisk
```

`CatchUpRisk` se activa cuando `OuterSubsteps >= Universe.CatchUpWarningSubsteps`, cuyo valor
diagnóstico es `128`. La instrumentación usa `Stopwatch.GetTimestamp()` y no crea objetos por
tick.

También se validan entradas antes de entrar a los bucles: `NaN`, infinito, delta negativo,
`TimeScale` no finito o escala no positiva publican un tick vacío y dejan `CurrentTime`
intacto.

El benchmark de `tools/SchedulerBenchmark` incluye ahora `scheduler_wall_clock_ms` y
`catch_up_risk` en cada escenario.

## Evidencia

La regresión `SchedulerTelemetryFlagsLargeCatchUpWithoutChangingSimulatedTime` verifica:

```text
TimeScale=1000
realDeltaTime=0.5 s
SimulatedSeconds=500 s
OuterSubsteps=250
CatchUpRisk=True
```

`SchedulerRejectsInvalidDeltaWithoutCorruptingClock` cubre `NaN`, delta negativo y
`TimeScale=NaN`.

Resultados:

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
Build succeeded. 0 Warning(s). 0 Error(s).

Scheduler-focused: 21/21 PASS
Full suite: 605/605 PASS
performance_acceptance_contract_test: 24 PASS, 1 dynamic skip
scheduler_phase6_benchmark_contract_test: PASS
SchedulerBenchmark: summary_finite=true summary_valid=true
```

Muestra diagnóstica del benchmark (`8` muestras, no FPS de hardware):

| Escenario | p50 tick | allocations/tick |
|---|---:|---:|
| `full_single` | 0.0469 ms | 3,166 B |
| `full_fleet` | 0.1188 ms | 9,011 B |
| `rails_fleet` | 0.5056 ms | 6,179 B |
| `mixed_fleet` | 3.5415 ms | 177,267 B |
| `wake_catchup` | 1.1164 ms | 81,002 B |

Estas cifras son una línea CPU diagnóstica del host y no sustituyen una captura de framebuffer
ni un profiler con allocation stacks.

## Decisión y siguiente fase

Promover la telemetría y los guards de entrada. Mantener sin cambios la política de catch-up,
`MaxCoastStep` y la hibernación. La fase siguiente debe capturar hitches reales de entrada al
nivel y probar un acumulador/presupuesto sólo si puede demostrar equivalencia para impacto,
periapsis, SOI, atmósfera, docking, staging y cambio de vessel activo.
