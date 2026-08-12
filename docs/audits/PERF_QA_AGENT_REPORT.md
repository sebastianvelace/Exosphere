# Agente 6 — informe de gates QA/performance

## Alcance

Este commit añade gates de aceptación para el bloqueo observado al iniciar el vuelo
sandbox con Starship. El ownership está limitado a:

- `ExosphereSimulation.Tests/PerformanceAcceptanceTests.cs`: invariantes y watchdogs
  sobre la simulación CPU.
- `tools/tests/performance_acceptance_contract_test.sh`: contrato estático y validación
  opcional de telemetría Godot.
- este informe.

No se modificó runtime, shaders, escenas ni tests existentes. El contrato queda diseñado
para ejecutarse después de integrar el pipeline asíncrono/telemetría del agente de runtime.

## Gates implementados

| Gate | Qué detecta | Evidencia |
|---|---|---|
| Estado finito | `NaN`, `+inf`, `-inf` en tiempo, cuerpos, nave, orientación, masa, térmica y motores | `AssertFinite` en cada tick medido |
| Progreso | reloj de simulación sin avance, posición congelada y streak de ticks sin progreso | `ProgressWatchdogDetectsNoStallAndStateRemainsFinite` |
| Startup CPU | carga del mundo puro dentro de un watchdog de 10 s | `SimulationStartupLoadsFiniteWorldWithinWatchdog` |
| Frame CPU | tick de una Starship de seis partes dentro de 1 s; además conserva muestra p95 | `StarshipSizedPhysicsBurstStaysFiniteAndWithinFrameWatchdog` |
| Política de física | clasificación exclusiva `Active`, `Nearby`, `OnRails` | `ActiveNearbyAndOnRailsTiersAreMutuallyExclusive` |
| Warp seguro | nave atmosférica fuera de rails y nave en vacío en rails | `WarpPolicyLeavesAtmosphericVesselOffRailsAndVacuumVesselOnRails` |
| Regresión física | descenso por gravedad, estado térmico finito y progreso en vuelo off-rails | `ShortOffRailsFlightPreservesPhysicalDirectionAndFiniteThermalState` |
| Runtime Godot | markers de startup, LUT fuera del hilo principal y cadencia de exposición | contrato shell |
| Telemetría | NaN/inf, startup, LUT asíncrona y frame budget si existen líneas `PERF_*` | `PERF_ACCEPTANCE_LOG` |

Los límites de 10 s y 1 s son watchdogs para detectar bloqueos catastróficos en CI; no
son el objetivo de producto. El objetivo de producto queda separado para no hacer que
una prueba CPU dependa de la velocidad de una GPU o de Xvfb:

- startup de escena hasta `simulation_loaded`: ≤ 5.0 s como límite de aceptación inicial;
- ningún frame sostenido por encima de 50 ms en la muestra de telemetría;
- ningún tick de física sin aumento de `CurrentTime`;
- cero valores no finitos;
- el vuelo activo siempre conserva física off-rails cuando una fuerza o evento lo exige;
- naves lejanas sin fuerzas/eventos permanecen en propagación analítica.

## Contrato de integración Godot

El script valida de forma fail-closed los siguientes puntos del runtime que debe entregar
el agente de pipeline:

1. `SimulationBridge` debe emitir `PERF_STARTUP phase=starship_spawned` y
   `PERF_STARTUP phase=simulation_loaded ms=<finite>`.
2. `SkyController` debe encolar el trabajo CPU con `Task.Run` y consumirlo en el hilo
   principal mediante `PollAtmosphereLutBuild`.
3. `VisualExposureController` debe declarar una cadencia explícita para la integración
   de transmitancia directa.
4. Si se proporciona `PERF_ACCEPTANCE_LOG`, el contrato rechaza NaN/inf, startup ausente
   o startup por encima del presupuesto, y comprueba la cola asíncrona de LUT. El frame
   budget se valida cuando el runtime emita líneas `PERF_FRAME frame_ms=<finite>`.

La ausencia de `PERF_FRAME` se reporta como `SKIP`, no como `PASS`: el agente de QA no
inventa una medición de frame que el runtime no registró.

## Ejecución

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --filter FullyQualifiedName~PerformanceAcceptanceTests --nologo

bash tools/tests/performance_acceptance_contract_test.sh

PERF_ACCEPTANCE_LOG=/tmp/exo_play-agent6.log \
  bash tools/tests/performance_acceptance_contract_test.sh
```

La última variante necesita un log real de Godot. No se debe convertir la ausencia del
log en éxito silencioso: el contrato registra `SKIP` y el informe de la ejecución debe
indicar que falta la evidencia dinámica.

## Resultado de esta ejecución del agente

Estado de los gates propios: **PASS**.

Resultados reproducidos en la rama aislada `agent6/qa-perf-gates`:

```text
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
  Build succeeded — 0 Warning(s), 0 Error(s)

dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo -v quiet
  Build succeeded — 0 Warning(s), 0 Error(s)

dotnet test ... --filter FullyQualifiedName~PerformanceAcceptanceTests --no-build
  Passed: 6, Failed: 0, Skipped: 0 — Duration: 509 ms

bash -n tools/tests/performance_acceptance_contract_test.sh
  PASS
git diff --check
  PASS
```

El contrato shell obtuvo `7 PASS`, `5 FAIL` y `1 SKIP` en esta rama. Los cinco FAIL son
intencionales y corresponden a markers del runtime (`PERF_STARTUP`, `Task.Run`, polling
de LUT y cadencia de exposición) que pertenecen al agente de runtime y no se copiaron
por ownership. No deben interpretarse como un fallo de estos tests: impiden integrar la
optimización sin su instrumentación.

La suite completa no pudo producir un resultado válido dentro del entorno:

1. Dentro del sandbox, `dotnet test` abortó antes de descubrir tests con
   `System.Net.Sockets.SocketException (13): Permission denied` al crear el
   `TcpListener` interno de VSTest. El comando terminó con `Test Run Aborted`.
2. Fuera del sandbox se autorizó una segunda ejecución con
   `timeout 120s dotnet test ... --no-build`; VSTest inició correctamente, pero no
   emitió resultados parciales y terminó con código `124` al alcanzar el timeout.

La segunda ejecución no se declara PASS ni FAIL de asserts: es un bloqueo/timeout de la
suite completa. No quedaron procesos de `dotnet test`, `testhost` o `vstest` activos al
cerrar la auditoría.

## Revisión posterior a la integración

Antes de marcar el gate como verde, el coordinador debe adjuntar:

- salida completa del filtro xUnit y del contrato shell;
- un log Godot con `PERF_STARTUP`, `PERF_ATMOS` y `PERF_FRAME`;
- captura de `max`, p95 y número de frames muestreados;
- una ejecución de smoke de `Flight` sin harness residual ni lock;
- resultado de la suite xUnit completa y build con 0 warnings/0 errores.

Si un test falla, no se debe relajar el umbral sin registrar el valor observado, la
causa raíz, el impacto en Starship sandbox y la decisión de rollback/promoción.
