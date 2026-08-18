# Fase 55 — cache de invalidaciones del HUD

Fecha: 2026-08-17
Estado: **integrada como reducción de trabajo de presentación; FPS GPU pendiente**

## Hallazgo

Después de limitar la captura a 30 Hz, el HUD todavía reescribía propiedades visuales que no
habían cambiado:

- los seis modos de navegación reaplicaban color y tamaño de fuente;
- el phase track repintaba todos sus puntos;
- `ApplyViewMode` reescribía visibilidad, `ProcessMode` y layout en cada snapshot aunque
  vista y densidad fueran iguales.

Estas escrituras pueden invalidar CanvasItems y provocar trabajo de composición innecesario.

## Cambio

`HUDController` ahora conserva las últimas fronteras aplicadas:

- `_lastRenderedNavigationMode` evita reaplicar estilos si no cambió el modo;
- `_lastPhaseTrackPhase` y `_lastPhaseTrackAfterEntry` evitan repintar el track;
- `_lastAppliedViewMode` y `_lastAppliedHudDensity` evitan reconfigurar el árbol;
- `CycleHudDensity` invalida explícitamente la cache y fuerza la siguiente presentación.

No se omiten input, throttle, toast, snapshot físico ni invalidaciones de fase/vista/densidad.

## Verificación

- Build Godot: **0 warnings, 0 errors**.
- Suite xUnit: **696/696 PASS**, 0 skipped.
- Startup: **PASS**, Flight alcanzó 60 frames con LUT atmosférico asíncrono.
- Contratos de cadencia, telemetría, hot-path y optimización: **PASS**.
- `git diff --check`: **PASS**.

No se declara una ganancia de FPS: el A/B framebuffer sigue bloqueado porque Xvfb pierde el
display antes de que Godot cargue la escena. La próxima medición debe comparar invalidaciones
de CanvasItem y `PERF_GPU` con navegación estable, cambio de fase y ciclo F3 de densidad.
