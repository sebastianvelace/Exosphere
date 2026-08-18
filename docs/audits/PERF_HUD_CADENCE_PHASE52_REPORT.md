# Fase 52 — cadencia segura de HUD y navball

Fecha: 2026-08-17
Estado: **promovida como optimización CPU/presentación; FPS GPU pendiente de host válido**

## Problema aislado

La telemetría del scheduler ya representaba menos del 1% del frame en llvmpipe. La
auditoría del código de presentación encontró tres `CanvasItem` que seguían forzando trabajo
en cada frame:

- `SystemsHUD` llamaba `QueueRedraw()` a frecuencia de render aunque sus datos son
  consumibles/comunicaciones de referencia.
- `AttitudeDataStrip` se redibujaba a cada frame aunque `HUDController` sólo necesita exponer
  el último snapshot.
- `AttitudeNavball` recalculaba orientación, marcadores y guía de pitch a cada frame.

## Cambio implementado

- `SystemsHUD` conserva el cambio inmediato de visibilidad y redibuja a `10 Hz`, alineado con
  la cadencia de telemetría de sistemas.
- `AttitudeDataStrip` conserva el snapshot más reciente y redibuja a `30 Hz`, con un primer
  redraw inmediato.
- `AttitudeNavball` actualiza su muestra visual a `30 Hz`; el filtro de rumbo consume el
  tiempo acumulado entre muestras para mantener el mismo comportamiento ante frames lentos.
- No se modificaron `Universe.Tick`, `Vessel.Tick`, comandos, térmica, deadlines, warp,
  staging, docking ni la autoridad de control.

## Verificación

- `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore`: **0 warnings,
  0 errors**.
- `dotnet build Exosphere.csproj --no-restore`: **0 warnings, 0 errors**.
- Suite xUnit directa: **696/696 PASS**, 0 skipped.
- Contrato de cadencia, telemetría visual y hot-path: **PASS**.
- `flight_startup_quick_check.sh`: **PASS**, Flight alcanzó 60 frames y el worker de atmósfera
  terminó correctamente.
- Godot headless con log explícito: **PASS**, sin `SCRIPT ERROR`, `Parse Error`, `Unhandled`
  ni `ERROR:`.

## Límite de la evidencia

El A/B framebuffer de esta iteración no pudo iniciar Godot: `xvfb-run` perdió el display X11
antes de cargar la escena (`X11 Display is not available`). No se presenta una reducción de
FPS como medida. La métrica comparable publicada sigue siendo la de Fase 51H: el scheduler
era `0.13%` del frame medio y el frame completo estaba dominado por llvmpipe.

La siguiente medición debe ejecutarse con X11/Xvfb reproducible o GPU física y comparar
`PERF_GPU` con HUD actual, HUD oculto y navball/strip ocultos. Hasta entonces, no se cambia la
calidad oficial del sky ni se promueve hibernación física.
