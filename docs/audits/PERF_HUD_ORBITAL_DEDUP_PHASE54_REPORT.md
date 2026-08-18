# Fase 54 — deduplicación de elementos orbitales del HUD

Fecha: 2026-08-17
Estado: **integrada como corrección de arquitectura de presentación; ganancia GPU pendiente**

## Hallazgo

En cada actualización del HUD, `FlightHudPresenter` ya calculaba apoapsis y periapsis a
partir del estado vectorial. Después, `HUDController` volvía a llamar
`OrbitalElements.FromStateVector` para calcular exclusivamente el tiempo a periapsis. Era
una segunda resolución de los mismos elementos dentro del mismo snapshot visual.

## Cambio

- `FlightHudSnapshot` ahora expone `TimeToPeriapsisS` junto a apoapsis/periapsis.
- El presenter calcula el tiempo sólo si el conic es válido, no radial y no hiperbólico;
  los casos degenerados conservan `NaN` y el comportamiento de cue existente.
- `HUDController` consume el valor del snapshot y ya no importa ni recalcula elementos
  orbitales.
- Se mantiene la captura a 30 Hz de la fase anterior; input, throttle y toast no se difieren.

## Evidencia

- El benchmark CPU del presenter aislado pasa de `0.019567 ms` p50 / `922.2 B` a
  `0.020609 ms` p50 / `929.8 B`, porque ahora incluye el valor que antes se calculaba en
  otra capa. Esta comparación aislada no se presenta como mejora.
- La mejora objetivo es eliminar la segunda llamada orbital en la ruta Godot completa;
  el host no permite medir esa ruta con framebuffer porque Xvfb falla antes de iniciar la
  escena.
- Prueba de paridad: el snapshot coincide con la fórmula de `MissionPhaseTrack` y mantiene
  un tiempo finito en la órbita de prueba.
- Suite xUnit: **696/696 PASS**, 0 skipped.
- Builds secuenciales: **0 warnings, 0 errors**.
- Startup: **PASS**, 60 frames y LUT atmosférico asíncrono completado.
- Headless Godot: **PASS**, log sin errores.
- Contratos de cadencia, telemetría y hot-path: **PASS**.

## Decisión

Se conserva la deduplicación por eliminar trabajo duplicado confirmado y centralizar la
autoridad orbital en el snapshot. No se declara una ganancia de FPS ni se promueve una
calidad atmosférica distinta. El siguiente gate es un benchmark de framebuffer/GPU válido
que compare el HUD completo antes/después y confirme que el cue de entrada permanece idéntico.
