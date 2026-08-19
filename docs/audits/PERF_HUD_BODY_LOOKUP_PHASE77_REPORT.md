# Fase 77 — lookup de cuerpo del HUD detrás del gate de presentación

Fecha: 2026-08-18  
Área: `scripts/HUDController.cs`, `tools/tests/render_cadence_phase23_contract_test.sh`

## Hallazgo

El HUD principal separa correctamente comandos/input por frame de la captura pesada de
telemetría a 30 Hz. Sin embargo, resolvía el cuerpo dominante antes del gate, aunque sólo lo
usaba después para obtener `Atmosphere.MaxAltitude` durante la actualización visual.

## Cambio implementado

`GetDominantBody(vessel.Position)` ahora ocurre después de que el gate de snapshot permite la
actualización de presentación. Se mantienen por frame:

- lectura de teclas y escritura de actitud;
- throttle manual y relay de comandos;
- expiración de toasts;
- detección de cambios de nave/fase/modo que fuerza el snapshot.

La telemetría física sigue siendo calculada por `FlightHudPresenter`; no se duplica orbitalidad,
thrust, presión ni navegación en el HUD.

## Reducción estructural

En un frame rate de 60 Hz y sin cambio de frontera visual:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| lookup de cuerpo dominante del HUD | 60/s | 30/s |
| captura principal de telemetría | hasta 60/s | 30/s |
| input/comandos | 60/s | 60/s |

El cambio es únicamente de presentación y no retrasa comandos ni el solver.

## Verificación

- contrato `render_cadence_phase23_contract_test.sh`: PASS;
- la suite completa, builds, xUnit y smoke tests de Godot son el gate de integración.

No se publica una ganancia de FPS: el framebuffer reproducible sigue bloqueado por X11/Xvfb.
