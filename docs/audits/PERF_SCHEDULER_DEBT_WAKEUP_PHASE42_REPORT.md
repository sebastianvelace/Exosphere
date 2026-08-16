# Scheduler: deuda temporal exacta y wake-up seguro — fase 42

## Alcance

Esta fase prepara un presupuesto de catch-up sin activar todavía una hibernación real en
el juego. El objetivo es que un futuro límite de trabajo haga el juego más fluido sin
descartar segundos simulados ni saltarse impactos, cruces de SOI, docking o staging.

## Cambios implementados

- `Universe` conserva `_pendingSimulationSeconds` como deuda global expresada en segundos
  simulados.
- `SchedulerBudgetEnabled` es `false` por defecto, por lo que la ruta oficial conserva la
  cadencia anterior durante esta fase.
- Con el presupuesto experimental activo, `MaxSchedulerSubstepsPerTick` limita pasos
  globales completos. Full Physics, Mixed y Rails se detienen sólo entre pasos terminados;
  la deuda restante se conserva para la siguiente llamada.
- `CurrentTime` sólo avanza por `ProcessedSimulationSeconds`. El tiempo pendiente no se
  proyecta a la posición pública de una nave.
- `SetCurrentTime` y `SetSimulationTime` validan el epoch, limpian deuda/deadlines de rails
  y sincronizan los cuerpos celestes.
- La telemetría añade `RequestedSimulationSeconds`, `ProcessedSimulationSeconds`,
  `PendingSimulationSeconds`, `BudgetLimited` y `BudgetReason`. `SimulatedSeconds` se
  conserva como campo legacy del tiempo solicitado para no romper el contrato anterior.
- El harness emite `PERF_SCHEDULER schema=2`; el contrato acepta todavía líneas `schema=1`
  históricas y valida las nuevas invariantes cuando aparecen.
- El wake-up está centralizado: `Undock`, docking, transición a fuerzas y contacto no
  pueden conservar una conic obsoleta.
- `Stage`, `BreakAtJoint` y `DeployPayload` invalidan la conic de ambos fragmentos después
  de cambiar posición/velocidad.
- El wake-up de una nave asentada usa un único umbral y también considera motores activos;
  un throttle pequeño no deja el vehículo anclado mientras intenta spool/thrust.
- Estados cinemáticos no finitos nunca se clasifican como trabajo analítico de rails.

## Invariante de conservación

Para cada llamada válida, en segundos simulados:

```text
deuda_al_final = deuda_al_inicio + tiempo_solicitado - tiempo_procesado
```

La prueba de deuda también verifica que cambiar `TimeScale` no vuelva a multiplicar la
deuda ya acumulada. Pausar no agrega ni consume deuda; un delta o una escala inválidos no
modifican el reloj ni el backlog.

## Decisión de activación

El presupuesto no se conecta aún al runtime Godot. `SystemsController`, EDL, potencia,
soporte vital, térmica, comunicaciones y contabilidad de maniobras todavía tienen
consumidores ligados al `delta` de frame o a `delta * TimeScale`. Antes de activar deuda
en el juego, esos sistemas deben consumir `ProcessedSimulationSeconds` o callbacks por
subpaso; de lo contrario una nave podría avanzar físicamente menos tiempo mientras sus
sistemas internos avanzan todo el tiempo solicitado.

La hibernación tampoco se promueve: la clasificación `Hibernated` sigue siendo una señal
diagnóstica, no una suspensión de física.

## Validación reproducible

- Suite xUnit: `613/613` passing.
- Tests dirigidos del scheduler: `28/28` passing.
- Build `Exosphere.csproj`: `0 warnings`, `0 errors`.
- Contrato estático: `44 PASS`, `0 FAIL`.
- Smoke real Godot/llvmpipe: `SMOKE_OK`, captura del pad y harness temporal limpiado.
- Contrato dinámico sobre `/tmp/exo_phase42_smoke.log`: `54 PASS`, `0 FAIL`, `0 SKIP`.
- Telemetría real: `50/50` líneas `PERF_SCHEDULER schema=2` válidas; en el smoke normal
  `pending_simulated=0.000000` y `budget_reason=Disabled`, como corresponde a una ruta
  oficial sin deuda.

## Trabajo pendiente para la siguiente fase

1. Reordenar los sistemas de gameplay para recibir segundos realmente procesados.
2. Añadir una política de comandos estructurales cuando existe deuda (encolar o drenar
   antes de staging/docking).
3. Comparar Full/Mixed budgeted contra referencia sin presupuesto en burns, contacto,
   SOI y docking.
4. Ejecutar la matriz visual completa en hardware físico antes de habilitar el cap en el
   juego normal.
