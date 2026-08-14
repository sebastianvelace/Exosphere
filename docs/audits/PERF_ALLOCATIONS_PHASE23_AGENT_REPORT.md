# Fase 23 P2 — allocations en telemetría de motores

Estado: implementado y validado en alcance acotado

Fecha: 2026-08-14

Alcance: `PartGraph.FillEngineReadouts` y pruebas CPU de Flight 7

## Alcance y límite

Esta iteración sólo modifica `PartGraph` y sus pruebas. No cambia `Vessel`, `Universe`,
Godot, las fórmulas de empuje/flujo ni el scheduler. La API compatible
`GetEngineReadouts` conserva su comportamiento; la optimización se aplica al camino de
buffer que consumen los lectores de telemetría.

## Evidencia antes/después

Se midió con `GC.GetAllocatedBytesForCurrentThread` en 1.000 llamadas repetidas sobre un
booster Flight 7 con 33 estados de motor, buffer precapacitado a 33 filas y presión de
101.325 Pa.

| Métrica | Antes | Después | Cambio |
|---|---:|---:|---:|
| Allocations por `FillEngineReadouts` estable | 73.656 B | 104 B | -99,86 % |
| Allocations totales, 1.000 muestras | 73.656.000 B | 104.000 B | -73.552.000 B |
| `Vessel.Tick`, Flight 7, 500 ticks | 3.968,08 B/tick | 3.968,08 B/tick | sin cambio |

El valor anterior corresponde a la ejecución inmediatamente previa al cambio, donde cada
muestra reconstruía los 33 objetos `EngineTelemetry` y las evaluaciones asociadas. El valor
posterior conserva una asignación pequeña del camino de consulta/copia, pero evita repetir
la construcción cuando presión, motores, estados, presión de cámara y códigos de fallo no
han cambiado.

La primera lectura después de un cambio físico sigue pagando la reconstrucción necesaria. El
cache no pretende eliminar ese coste: permite que lecturas repetidas del mismo estado —por
ejemplo, dos consumidores visuales dentro de la misma muestra— reutilicen las filas sin
recalcular telemetría.

## Cambio aplicado

`PartGraph` mantiene un snapshot privado de filas, piezas y referencias a estados. Antes de
reutilizarlo verifica, sin LINQ ni nuevas colecciones:

- presión ambiental finita e idéntica;
- orden e identidad de las piezas y estados de motor;
- `State`, `ChamberPressureFraction` y `FailureCode` de cada estado;
- throttle y estado de fallo para motores legacy.

Un cambio de engine-out se comprobó explícitamente: el motor 8 pasa a `Failed`, conserva el
código `ALLOCATION_AUDIT_ENGINE_OUT` y el contador pasa de 33 a 32. Los cambios topológicos
y hot-stage invalidan el snapshot; los cambios de estado que no requieren invalidación manual
provocan un miss por la validación de entradas.

## Validación ejecutada

- `StarshipPerformanceRegressionTests`: 5/5 PASS.
- `StarshipFlight7DataTests`: 8/8 PASS.
- `starship_hotpath_contract_test.sh`: PASS.
- Benchmark scheduler existente: PASS; los escenarios permanecen finitos.
- Build realizado por la batería focal: 0 warnings, 0 errors.
- `git diff --check`: PASS.

El wrapper `tools/perf/starship_hotpath_benchmark.sh` fue intentado y compiló sin warnings,
pero su invocación de VSTest fue abortada por `SocketException (13): Permission denied` del
servidor de comunicación del runner. La ejecución directa equivalente del filtro focal sí
pasó y produjo las cifras anteriores.

## Decisión

Se acepta el cambio porque la reducción es grande, reproducible y está limitada a la ruta de
telemetría cacheable. No se modificó el tick físico: su allocation budget permanece en
3.968,08 B/tick. No se eliminó LINQ a ciegas ni se cambió el orden de las filas.

Limitación pendiente: una muestra posterior a cada cambio de estado continúa evaluando el
modelo de motor; eliminar ese coste requeriría una API de evaluación sin allocations en
`Part`, que queda fuera del alcance autorizado de P2.
