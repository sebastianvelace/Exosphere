# Fase 30 — auditoría de allocations del hot-stage de Starship

Fecha: 2026-08-14  
Área: `ExosphereSimulation/Parts/PartGraph.cs` y regresiones Flight 7

## Hallazgo

Durante `HotStageOverlapActive`, `PartGraph.ConsumePropellant` reconstruía en cada tick:

- un `HashSet<Part>` con las partes de la etapa inferior;
- una `List<Part>` con las partes superiores;
- enumeraciones LINQ auxiliares para formar la segunda colección.

La operación era funcionalmente correcta, pero ocurría mientras la Starship seguía consumiendo
propelente durante el solapamiento de staging. En un vuelo real esto añade garbage a un camino
de alta frecuencia y puede contribuir a pausas del recolector.

## Cambio aplicado

Se añadieron `_hotStageBottomSet` y `_hotStageUpperParts` como scratch buffers privados de
`PartGraph`. Ambos se limpian y rellenan por índice en cada tick. La separación de partes y las
dos llamadas a `ConsumePropellantFromPool` conservan el mismo conjunto inferior/superior y el
mismo orden de consumo; no se cambió ninguna fórmula, presión, masa, deadline ni transición
de staging.

## Pruebas y medición

Comandos ejecutados:

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --no-restore \
  --filter 'FullyQualifiedName~StarshipPerformanceRegressionTests' \
  --logger 'console;verbosity=detailed'
OUT_DIR=/tmp/exo_phase30_alloc_after_hotstage SAMPLES=80 WARMUP=10 \
  bash tools/perf/allocations_tick_phase23_benchmark.sh
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --no-restore \
  --verbosity minimal
bash tools/ci_check.sh
```

Resultados:

| Gate | Resultado |
|---|---|
| Build de tests | PASS, 0 warnings, 0 errors |
| Regresiones Starship | PASS, 6/6 |
| Flight 7, 500 ticks | 360.08 B/tick; 33 motores; sin fallo funcional |
| Hot-stage, 128 ticks | 320.00 B/tick; dentro del límite de 800 B/tick |
| Benchmark `mixed_fleet` | PASS; p95 4.1550 ms; 182,965.6 B/tick |
| Suite xUnit | PASS, 599/599 |
| CI completo | PASS; 0 warnings, 0 errores |

El benchmark de asignaciones posterior mantuvo finitud y los contratos de eventos en los cinco
escenarios. La reducción está aislada al camino de hot-stage; no se presenta como una promesa de
FPS porque este host sigue usando llvmpipe y no expone `/dev/dri`.

## Limitación del harness

`tools/perf/starship_hotpath_benchmark.sh` no pudo iniciar su proceso de pruebas por
`System.Net.Sockets.SocketException (13): Permission denied` al abrir el socket de VSTest. El
fallo se reprodujo con y sin build. La ejecución directa del filtro de seis regresiones pasó,
por lo que el bloqueo se clasifica como infraestructura del harness y no como regresión de
Starship. No se modificaron permisos del sistema.

## Decisión

Promover el cambio de buffers reutilizables. Mantener sin cambios el scheduler físico, las
fórmulas de propulsión y el renderer. La siguiente optimización debe perfilar el coste residual
del tick en un host con EventPipe y GPU física antes de modificar cadencias, hibernación o
frecuencia de simulación.
