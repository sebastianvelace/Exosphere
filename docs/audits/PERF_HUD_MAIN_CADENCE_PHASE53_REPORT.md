# Fase 53 — captura acotada del HUD principal

Fecha: 2026-08-17
Estado: **promovida como optimización de presentación; FPS GPU pendiente de framebuffer válido**

## Baseline CPU

El benchmark `tools/perf/allocations_tick_phase23_benchmark.sh`, con `SAMPLES=80` y
`WARMUP=10`, midió `FlightHudPresenter.Capture` sobre la nave Flight 7:

- p50: `0.019567 ms` por captura;
- p95: `0.020638 ms`;
- allocations administradas: `922.2 B` por captura;
- `gc_gen0/1/2=0` durante la ventana medida;
- estado finito/válido: `true`.

El HUD principal invocaba esa captura a frecuencia de render. La captura no ejecuta física,
pero sí calcula orbitales, combustible, motores, alertas y formato de navegación; por eso es
trabajo de presentación que no necesita 60 muestras por segundo.

## Cambio

`HUDController` conserva input, throttle y relay cada frame. La captura y actualización de
los paneles pasan a `30 Hz`, con invalidación inmediata cuando cambia:

- nave activa;
- fase de misión;
- vista exterior/cockpit/mapa.

El temporizador de toast sigue usando wall-clock cada frame. `LatestSnapshot` continúa siendo
la única fuente para cockpit y controles visuales; no se recalcula ni se modifica ningún estado
físico durante la omisión de un frame de presentación.

## Verificación

- Build Godot: **0 warnings, 0 errors**.
- `FlightHudPresenter` focalizado: **5/5 PASS**.
- Suite xUnit completa: **696/696 PASS**, 0 skipped.
- `flight_startup_quick_check.sh`: **PASS**, 60 frames y LUT atmosférico asíncrono completado.
- Godot headless: **PASS**, log sin `SCRIPT ERROR`, `Parse Error`, `Unhandled` ni `ERROR:`.
- Contrato `render_cadence_phase23_contract_test.sh`: **PASS**.

La reducción de allocations por segundo de captura es una inferencia directa del benchmark:
30 Hz implica aproximadamente la mitad de llamadas que 60 Hz (`~55.3 KiB/s` frente a
`~27.7 KiB/s` para este camino). No se etiqueta como mejora de FPS porque el A/B framebuffer
del host sigue fallando antes de crear el display X11.

## Decisión

Promover la cadencia CPU/presentación. Mantener sin cambios la calidad atmosférica oficial,
el scheduler y la hibernación física. Repetir el A/B en X11/GPU válido con HUD actual, HUD
oculto y captura a 30/60 Hz antes de declarar una ganancia visual.
