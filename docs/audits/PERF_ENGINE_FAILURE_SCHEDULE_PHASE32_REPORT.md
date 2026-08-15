# Fase 32 — agenda de fallos de motor sin closures por tick

Fecha: 2026-08-14  
Área: `Part.ApplyScheduledFailure` y tick runtime de Flight 7

## Hallazgo

Después de eliminar las allocations del `performanceMap`, el Flight 7 runtime todavía
registraba `3,672 B/tick` con motores apagados, `3,712 B/tick` con motores encendidos y
`3,792 B/tick` con TVC. El coste existía aunque no hubiese inyecciones de fallo pendientes.

`ApplyScheduledFailure` usaba `List<EngineFailureInjection>.FindIndex` con un predicado que
capturaba el `EngineInstanceState`, el intento activo y `dt`. La closure/delegate se creaba
para cada instancia de motor en cada tick; una lista vacía no evitaba esa materialización.

## Cambio

La búsqueda ahora recorre `_scheduledFailures` por índice, compara el mismo identificador,
estado, intento y deadline, elimina el primer match y llama a `FailEngine` con el mismo código.
La prioridad de la primera inyección coincidente y el consumo de una sola inyección se
conservan. No se cambió la máquina de estados, el límite de reinicios ni la lógica térmica.

## Medición

Fixture: Flight 7 construido con `PartCatalog`, 32 ticks de warm-up y 128 ticks medidos por
escenario.

| Escenario | Antes | Después | Reducción |
|---|---:|---:|---:|
| Motores apagados | 3,672 B/tick | 240 B/tick | 93.46% |
| Motores encendidos | 3,712 B/tick | 280 B/tick | 92.46% |
| Motores encendidos + TVC | 3,792 B/tick | 360 B/tick | 90.51% |

El nuevo test `RuntimeFlight7AllocationBreakdownReportsControlHotPaths` exige menos de
`1,000 B/tick` en los tres escenarios. Los tests de fiabilidad existentes cubren fallos
programados, sobretemperatura, reinicios y persistencia.

## Verificación

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --no-restore \
  --filter 'FullyQualifiedName~RuntimeFlight7AllocationBreakdownReportsControlHotPaths|FullyQualifiedName~EngineReliabilityTests'
bash tools/tests/starship_hotpath_contract_test.sh
```

La ejecución focalizada pasó `7/7`, el contrato de hot-path pasó y la suite completa pasó
`602/602`. El build de tests quedó en `0 warnings / 0 errors`; `ci_check.sh` también pasó con
contratos `34/34`, builds sin warnings y startup quick-check PASS.

## Decisión

Promover el recorrido indexado. El residual runtime queda en `240–360 B/tick`, por debajo del
nuevo presupuesto de `1,000 B/tick`. No se cambia la cadencia del scheduler ni se apagan
subsistemas físicos; la siguiente decisión depende de profiling EventPipe/GPU en un host que
los exponga.
