# Fase 9 — hot path de Starship y PartGraph

Estado: implementado, validado y listo para integración
Fecha: 2026-08-12
Alcance: `Vessel.Tick`, `PartGraph`, `Part`, Flight 7 (33 motores)

## Diagnóstico

El scheduler de la fase 8 ya evitaba snapshots de la flota, pero una nave Starship seguía
reservando memoria dentro de cada tick. El camino observado era el de la física activa, no el
HUD: cuatro evaluaciones RK4 consultan empuje, drag, masa y torque, y cada consulta repetía
reducciones LINQ sobre los estados de motores, geometría y tanques.

Línea base reproducible:

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --no-build --no-restore --nologo \
  --filter 'FullyQualifiedName~StarshipPerformanceRegressionTests' \
  --logger 'console;verbosity=detailed'
```

También quedó disponible el wrapper:

```bash
OUT_DIR=/tmp/exo_starship_hotpath \
  bash tools/perf/starship_hotpath_benchmark.sh
```

## Cambios

- `Part.RefreshAggregateThrottle`, `GetMassFlow`, `GetThrustMagnitude`,
  `GetFullThrottleThrustMagnitude` y `GetRatedFullThrottleThrustMagnitude` usan bucles sobre
  las mismas instancias y conservan el orden de suma.
- `PartGraph.VehicleLength`, `MaximumDiameter`, `NoseRadius` y el fallback de
  `AxialDragCoefficient` ya no materializan secuencias temporales.
- El reparto de LF/Oxidizer/Solid/Monopropellant suma tanques por índice, sin enumeradores
  LINQ en el tick.
- La identificación de combustible usa `StringComparison.OrdinalIgnoreCase`; evita crear una
  copia con `ToLowerInvariant` en cada motor y tick.
- La aceleración aero de actitud busca el primer offset y la presencia de body flaps en un
  único recorrido de partes, sin `Select/FirstOrDefault/Any`.
- Se añadió un presupuesto xUnit de `<5,000 B/tick` y el contrato
  `starship_hotpath_contract_test.sh` para bloquear la regresión de estas reducciones.

No se alteraron las fórmulas de thrust, Isp, gimbal, consumo, drag, RK4, staging, engine-out
ni los valores de datos. La suma conserva el orden de cada lista; sólo se cambió el mecanismo
de enumeración.

## Resultado medido

| Métrica | Antes | Después | Variación |
|---|---:|---:|---:|
| Tiempo diagnóstico por tick | 0.031225 ms | 0.018401 ms | -41.1% |
| Asignación administrada por tick | 5,320.08 B | 4,504.08 B | -15.3% |
| Estado físico | finito | finito | sin cambio |

Los tiempos dependen del host y no son una promesa de FPS; la asignación es la señal estable
que se usa como gate. El resultado posterior quedó por debajo del presupuesto en la corrida
local y el benchmark reporta `33` motores activos.

## Validación

- Build de `ExosphereSimulation.Tests`: 0 warnings, 0 errors.
- Build de `Exosphere`: 0 warnings, 0 errors.
- Benchmark filtrado: 3/3 tests PASS.
- Paridad determinista de dos Flight 7 idénticos: PASS.
- Caches de hot-stage y transición mecánica: PASS.
- Contrato `starship_hotpath_contract_test.sh`: PASS.
- Suite completa: 558 passed, 0 failed, 0 skipped.
- Playtest visual post-cambio `--ascent --flight7`: PASS, hitos `pad/liftoff/maxq/hotstage/
  separation/orbit`, `ASCENT_ORBIT_OK`, órbita `184×147 km`, `e=0.003`.
- La corrida anterior de fase 8 dio `150×145 km`, `e=0.000`; ambas cruzan el gate de órbita
  estable. La diferencia pequeña se conserva como señal para repetir en hardware objetivo:
  el harness Godot usa llvmpipe y el control de ascenso depende del tiempo de proceso. La
  prueba pura de dos naves idénticas sigue siendo bitwise/determinista dentro de tolerancia.

## Límites y siguiente fase

Todavía quedan iteradores en APIs de consulta fuera del núcleo de `Vessel.Tick`, especialmente
`GetEngineReadouts` para UI y los generadores de geometría por motor usados por el solver de
gimbal. No se eliminan a ciegas: la siguiente medición debe separar el coste de esos iteradores
del coste de `EnginePerformanceEvaluator`, y cubrir engine-out asimétrico durante ascenso y
reentrada. La próxima fase puede introducir buffers de geometría por tick sólo si conserva la
paridad de torque y los estados de los 33+6 motores.
