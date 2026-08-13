# Fase 10 — buffers de geometría y autoridad de gimbal

Estado: implementado, validado y listo para integración
Fecha: 2026-08-12
Alcance: `Part`, `PartGraph`, torque de motores, solver diferencial TVC

## Objetivo

Reducir las asignaciones restantes del `Vessel.Tick` sin degradar la física de torque. Las
APIs públicas de geometría continúan disponibles; el solver interno usa snapshots reutilizados
por cada `Part` y recorridos indexados.

## Cambios

- `Part` mantiene buffers estables para geometría de thrust y autoridad de gimbal.
- Se añadieron rutas internas `GetEngineInstanceThrustGeometrySnapshot` y
  `GetEngineInstanceGimbalAuthoritySnapshot`; reconstruyen el buffer y no crean iteradores por
  llamada.
- `GetThrustVector` usa el snapshot de thrust cuando hay runtime por motor.
- `PartGraph.GetTotalTorque`, `GetDifferentialTVCAngularAccelerationEnvelope` y
  `SolveDifferentialGimbal` recorren los snapshots por índice.
- La API pública `GetEngineInstanceThrustGeometry`/`GetEngineInstanceGimbalAuthority` se
  conserva y sigue siendo enumerable para las herramientas/consumidores existentes.
- El contrato de Starship verifica que el solver no vuelva a consumir estas APIs mediante
  `foreach` y que los snapshots estén presentes.

No se cambiaron las ecuaciones de cross product, autoridad, regularización, límites de gimbal,
selección de motores, orientación ni doble precisión.

## Resultado

Benchmark `Flight7TickHotPathStaysFiniteAndWithinDiagnosticBudget`, 500 ticks:

| Métrica | Fase 9 | Fase 10 | Cambio adicional | Desde línea base |
|---|---:|---:|---:|---:|
| Asignación administrada por tick | 4,504.08 B | 3,968.08 B | -11.9% | -25.4% vs 5,320.08 B |
| Tiempo diagnóstico por tick | 0.018401 ms | 0.015400 ms | -16.3% | -50.7% vs 0.031225 ms |

El tiempo es diagnóstico y depende del host. El presupuesto estable continúa siendo `<5,000
B/tick`; el benchmark posterior queda en `3,968 B/tick`.

## Validación

- Build de simulación y juego: 0 warnings, 0 errors.
- Batería focalizada: 19/19 PASS para Starship, torque, clúster mixto y diferencial TVC.
- Engine-out asimétrico: PASS; torque best-effort alineado.
- Clúster simétrico: torque neto cero dentro de tolerancia.
- Geometría por montaje y dirección no vertical: PASS.
- Paridad determinista de Flight 7: PASS.
- Suite completa, startup y visual ascent se ejecutan como gate de publicación.

## Siguiente fase

No quedan más cambios internos de geometría sin medir. El siguiente trabajo debe atacar los
consumidores visuales de `GetEngineReadouts` y revisar si el HUD, renderer y exposición respetan
cadencias sin tocar la simulación. Cualquier cache adicional del estado de motor debe invalidarse
en engine-out, hot-stage, staging y cambios de presión.
