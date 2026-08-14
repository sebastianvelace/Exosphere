# Fase 11 — buffers de telemetría para HUD y renderer

Estado: implementado y validado
Fecha: 2026-08-12
Alcance: `PartGraph`, `Vessel`, `EngineGridHUD`, `VesselRenderer`

## Objetivo

Reducir trabajo redundante en los consumidores visuales de motores sin cambiar la física,
el staging, el spool, el engine-out ni la frecuencia del `Vessel.Tick`. El HUD muestrea a
10 Hz y las plumas a 30 Hz; ambos deben leer el mismo contrato de telemetría sin construir
una colección temporal para cada muestra.

## Cambios

- `PartGraph.FillEngineReadouts(double, List<EngineReadout>)` llena un buffer propiedad del
  consumidor y lo limpia sin reemplazarlo.
- `Vessel.FillEngineReadouts(CelestialBody?, List<EngineReadout>)` expone la ruta al juego sin
  hacer que los scripts conozcan la presión ambiental ni el `PartGraph`.
- `GetEngineReadouts` se conserva para compatibilidad, pero ya no usa `SelectMany`, `Select` ni
  un array temporal para el caso sin runtime.
- `EngineGridHUD` reutiliza `_readoutScratch` en su muestra de 10 Hz.
- `VesselRenderer` reutiliza `_engineReadoutScratch` en su actualización de plumas de 30 Hz.
- El contrato `visual_telemetry_contract_test.sh` impide que estos dos consumidores vuelvan a
  enumerar la API compatible o pierdan sus límites de cadencia.

La ruta sólo afecta presentación. No se modificaron ecuaciones, `Vessel.Tick`, selección de
motores, presión, empuje, masa, consumo, gimbal, staging ni propagación de `solar_visibility`.
La ruta aún puede pagar el coste interno de producir telemetría por motor; medir y eliminar
esas asignaciones requiere un perfil de Godot/managed separado y no se presume aquí.

## Validación

- Contrato de telemetría: PASS.
- Prueba focal Starship Flight 7: 8/8 PASS.
- Suite completa: 559/559 PASS.
- Build de simulación y juego: 0 warnings, 0 errors.
- Startup async y smoke Godot: PASS.
- Benchmark diagnóstico de `Vessel.Tick`, 500 ticks: 3,968.08 B/tick y 0.014815 ms/tick;
  estable frente al presupuesto de fase 10 (`<5,000 B/tick`). Esta métrica es del hot path de
  simulación y no es una medición de FPS del renderer.
- Playtest visual final con `run-id=phase11-telemetry-ascent-final`: `ASCENT_ORBIT_OK`, órbita
  `151×143 km`, `e=0.001`; capturas `pad`, `liftoff`, `maxq`, `hotstage`, `separation` y
  `orbit` válidas. La traza mantuvo 33 motores antes de separación, 6 después, estados
  finitos y cero GAP/FALLBACK. El worker LUT completó en 8,019.7 ms sin bloquear la física.
  Artefactos: `/tmp/exo_visual_telemetry_phase11_final/`.

## Lectura del coste visual

El framebuffer de esta máquina usa llvmpipe. Durante el playtest se observaron frames de
aproximadamente 0.58–0.75 s, por lo que el resultado no se puede convertir en una afirmación
de 60 FPS de hardware. Ese dato sí justifica la siguiente fase de profiling de render/GPU,
pero no demuestra que el cambio de buffers sea la causa del coste: el cambio elimina trabajo
de enumeración/copia de telemetría y debe compararse con un baseline bajo el mismo renderer.

## Decisión y siguiente etapa

La fase se acepta como una mejora de bajo riesgo para consumidores visuales. No se promueve
ningún LOD físico ni se modifica la cadencia de simulación. La siguiente etapa se divide en
dos mediciones independientes:

1. perfil de render CPU/GPU y número de draw calls/material updates por vista;
2. perfil managed de `GetEngineTelemetry` y `PlumeSystem.UpdateGeneric` para decidir si hace
   falta un snapshot de runtime aún más profundo.

Sólo después de esas mediciones se podrá cambiar geometría, partículas, sombras o frecuencia
de actualización; cada cambio debe conservar el contrato de engine-out, hot-stage y separación.
