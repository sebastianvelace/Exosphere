# Auditoría de scheduler y deadlines — fase 27

Fecha: 2026-08-14
Área: `ExosphereSimulation/Universe.cs`, `PhysicsSchedulerTelemetry` y `SchedulerBenchmark`
Estado: `PASS` de equivalencia; no se promueve una reducción adicional de dispatches.

## Resultado del agente P1

La auditoría se ejecutó en un worktree aislado (`phase27-p1-scheduler`) sin cambios
persistentes ni commit. La suite focalizada pasó **19/19**, los builds terminaron con
**0 warnings y 0 errores**, y todos los escenarios del benchmark quedaron finitos y
válidos.

El baseline reproducido fue:

- `mixed_fleet`: 450 dispatches/tick, 396 proyecciones/tick y aproximadamente 718.6 KiB/tick;
- `wake_catchup`: contrato de recuperación PASS;
- snapshot de telemetría: coste prácticamente nulo;
- la reserva aproximada de 386 B/tick pertenece a la propagación de cuerpos, no al snapshot.

## Decisiones

- Conservar `sample_window` y sus contadores: son deterministas y no constituyen el coste
  dominante.
- Conservar deadlines: evitan comprobaciones de eventos innecesarias y mantienen la
  equivalencia de wake-up.
- No eliminar proyecciones ni dispatches para perseguir una cifra menor: podría omitir
  docking, SOI o eventos atmosféricos.
- Mantener como candidato futuro la reutilización de la clasificación `OnRails` entre
  `ClassifyMixedPhysicsWorkload` y `GetPhysicsSchedulerDeadlinePlan`. Una prueba temporal
  informó aproximadamente 41.6 KiB/tick y 0.8–1.0 ms/tick menos, pero requiere una matriz
  de equivalencia propia antes de tocar `Universe.cs`. La réplica del coordinador con la
  misma implementación no reprodujo esa mejora: `mixed_fleet` permaneció en 598,761 B/tick
  y el p95 fue 3.876 ms frente a 3.698 ms del cambio de `PartGraph`. La modificación fue
  revertida y queda `DESCARTADA` para esta fase.

También se identificaron como líneas futuras, no aprobadas: coste O(n²) potencial en
`StressSolver.ComputeLoads`, coste partes×substeps en `ThermalModel` y reservas por punto
en `SurfaceContactSolver`.

## Criterio de integración

Esta auditoría no cambia la política física. La optimización promovida en la fase 27 es
únicamente la reutilización de buffers de `PartGraph`; el candidato de scheduler queda
descartado hasta que exista una medición estable y una matriz de posición, velocidad, SOI,
contacto, staging y catch-up.
