# Paridad del scheduler con deuda temporal — fase 44

## Objetivo

Añadir una batería CPU-only que compare `Universe` con
`SchedulerBudgetEnabled = true` y `MaxSchedulerSubstepsPerTick = 1` contra una
referencia con el presupuesto desactivado. La prueba no cambia la política oficial del
juego ni habilita el presupuesto en Godot.

## Método de comparación

Cada fixture usa dos universos independientes con los mismos cuerpos, partes y estado
cinemático. La llamada inicial solicita 4 s simulados al universo presupuestado; éste
compromete un paso global de 2 s y conserva 2 s en `PendingSimulationSeconds`. La
referencia se lleva directamente a la misma época de 2 s sin deuda.

Después se aplica el evento de la escena en esa época común. La deuda se drena con el
presupuesto todavía activo y se lleva la referencia al `CurrentTime` final exacto del
universo presupuestado. Así la comparación es por época física, no por número de llamadas
ni por tiempo de pared.

## Cobertura

`ExosphereSimulation.Tests/SchedulerBudgetParityTests.cs` cubre:

- rails: tiempo solicitado/procesado/pendiente, límite de subpasos, estado de posición,
  velocidad y conic, y drenaje completo sin pérdida de tiempo;
- staging: separación de un stack Flight 7, invalidación de rails en ambos fragmentos,
  masa/recursos y estado final frente a la referencia;
- docking/undocking: conexión persistente, skip del secundario, pose rígida, separación
  con velocidad y limpieza de `IsOnRails`/`OrbitalState`;
- wake-up: throttle aplicado en el mismo epoch, salida de rails, física propulsada y
  paridad del ciclo de vida/recursos del motor usando el catálogo real de motores.

Las comprobaciones comunes también exigen estados finitos y ausencia de deuda negativa.
La prueba de rails verifica explícitamente que el primer tick presupuestado reporta
`RequestedSimulationSeconds = 4`, `ProcessedSimulationSeconds = 2` y
`PendingSimulationSeconds = 2`, con razón `SubstepLimit`.

## Resultado dirigido

- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --filter FullyQualifiedName~SchedulerBudgetParityTests`
- Resultado: **4/4 passing**, 0 fallos.
- Casos: rails/deuda, staging, docking/undocking y rail wake-up con runtime de motores.

La suite se ejecutó después de corregir dos falsos supuestos de la prueba: los contadores
de skips se agregan entre ticks presupuestados, mientras que la referencia puede procesar
varios subpasos en una sola llamada; además, la carga de partes del fixture de motores usa
`PartCatalog`, que resuelve los modelos y clusters antes de crear `EngineStates`.

## Límites y riesgos

- La batería valida `Universe` y modelos CPU; no cubre `SimulationBridge`,
  `SystemsController`, HUD, render ni el harness Godot.
- No valida todavía callbacks por subpaso para sistemas de gameplay como soporte vital,
  potencia, comunicaciones, térmica, EDL o maniobras. Esos sistemas requieren una fase de
  paridad separada si el presupuesto se activa en runtime.
- Staging y docking se aplican en una época común antes de drenar la deuda. No se está
  afirmando aún una política de comandos concurrentes durante un backlog real; esa política
  debe decidir si encola, pausa o materializa eventos estructurales.
- Las tolerancias son físicas y pequeñas (`1e-5 m` de posición y `1e-9 m/s` de velocidad
  en rails). No deben relajarse para ocultar una divergencia; si cambia el integrador o la
  resolución de eventos, habrá que documentar el nuevo presupuesto de error.
- El presupuesto continúa desactivado por defecto y no se promueve a la ruta oficial por
  este informe.

## Alcance de cambios

Esta fase añade sólo el archivo de pruebas y esta auditoría. No modifica runtime C# de
`ExosphereSimulation/`, scripts Godot, escenas ni configuración del juego.
