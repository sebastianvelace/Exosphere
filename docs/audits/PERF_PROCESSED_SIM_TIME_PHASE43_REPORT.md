# Tiempo simulado procesado en gameplay — fase 43

## Objetivo

Eliminar la dependencia de `delta` de render y `delta * TimeScale` para los sistemas que
representan consecuencias físicas u operativas. Esto prepara la activación controlada del
presupuesto de scheduler sin consumir deuda futura ni duplicar tiempo.

## Implementación

- `SimulationBridge.LastProcessedSimulationSeconds` expone el intervalo realmente
  comprometido por el último `Universe.Tick`.
- `SimulationBridge` llama `SystemsController.AdvanceProcessedSimulation()` justo después
  de `Universe.Tick` y antes de los controladores de ascenso/EDL.
- `SystemsController` conserva el pre-pase de prioridad `-50` para relay y consecuencias,
  pero mueve Life Support, Power, Thermal y Comms al post-pase explícito.
- Life support, batería, térmica y blackout reciben `ProcessedSimulationSeconds`, nunca
  `RequestedSimulationSeconds`, `PendingSimulationSeconds` ni `delta * TimeScale`.
- La contabilidad de Δv de `AutopilotController` y `ManeuverExecutor` usa el mismo intervalo
  comprometido.
- El acumulador de carga G de EDL y el temporizador de flip usan tiempo simulado procesado;
  las animaciones y la presentación siguen usando wall-clock.
- La pausa produce `ProcessedSimulationSeconds = 0`, por lo que no consume consumibles ni
  avanza blackout/Δv/EDL.

## Contrato de orden

```text
pre-pase input/relay/consecuencias anteriores
→ Universe.Tick
→ AdvanceProcessedSimulation
→ ascent guidance / EDL post-process
→ presentación, HUD y efectos wall-clock
```

No se movió el pre-pase de `SystemsController` porque allí se aplican dead-stick, abortos,
SAS y comandos de ground link antes del siguiente tick. El punto post-scheduler evita que
los sistemas consumibles lean una nave en un epoch distinto del `dt` que integran.

## Límites conocidos

Thermal y Comms reciben una muestra ambiental final por intervalo procesado. En una deuda
grande, una futura fase deberá ofrecer snapshots por subpaso para detectar con precisión
transiciones de atmósfera, eclipse y blackout. El presupuesto de scheduler permanece
desactivado por defecto y no se promueve en esta fase.

## Validación

- Build Godot: `0 warnings`, `0 errors`.
- Smoke real llvmpipe: `SMOKE_OK`, captura del pad, LUT asíncrono completado.
- Contrato dinámico sobre `/tmp/exo_phase43_smoke.log`: `60 PASS`, `0 FAIL`, `0 SKIP`.
- Telemetría: `50/50` líneas `PERF_SCHEDULER schema=2` válidas y sin NaN/inf.

## Próximo gate

Crear pruebas de paridad con presupuesto habilitado: consumibles, Δv, blackout, EDL,
staging, docking y cambio de warp deben coincidir con una referencia sin deuda por
`Universe.CurrentTime`, no por número de frames.
