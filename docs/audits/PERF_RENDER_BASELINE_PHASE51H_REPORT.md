# Phase 51H — baseline de render/presentación

Fecha: 2026-08-17  
Estado: **diagnóstico, sin cambios de runtime**

## Evidencia reproducible

Se ejecutó el smoke directo del harness con `--run-id phase51-h-smoke-recheck` sobre
Godot 4.6.3 mono, OpenGL3, Xvfb 1920×1080 y Mesa llvmpipe.

- Resultado: `SMOKE_OK`, 50 frames, captura `pad` válida.
- Startup: `simulation_loaded=4036.9 ms`; el LUT Earth se generó en worker, con
  `cpuMs=11192.3`, sin error de escena.
- Frame completo: media `1745.520 ms`, p95 `1898 ms`, p99/máximo `6009 ms`.
- Scheduler: media `2.269 ms`, máximo `65.693 ms`, sólo `0.13%` del frame medio.
- Candidate: 50 líneas válidas, `enabled=false`, `deferred_skips=0`.
- Log: `/tmp/exo_play-phase51-h-smoke-recheck.log`.

La captura y el log son artefactos temporales de validación; no se incorporan al repositorio.

## Resultado de la prueba de tooling

`tools/perf/renderer_benchmark.sh` pasó su contrato (`2` fixtures válidos y `4` inválidos),
pero dos ejecuciones reales fallaron antes de cargar la escena cuando el wrapper lanzó el
harness bajo `/usr/bin/time -v`: Godot recibió `X11 Display is not available`. La misma escena
pasó dos veces con `tools/visual_playtest.sh` directo, por lo que esos reportes no son una
medición de rendimiento válida.

El wrapper debe corregirse o reemplazarse antes de usar RSS/FPS como gate automático. Hasta
entonces, no se publica una mejora de FPS ni se cambia la resolución oficial.

## Decisión de la siguiente oleada

El cuello observado no está en `Universe` ni en la ruta física. El orden de trabajo queda:

1. corregir el aislamiento Xvfb del benchmark y añadir un test de regresión que verifique que
   el proceso medido conserva `DISPLAY` válido;
2. medir por separado render exterior, cockpit/subviewports, HUD/telemetría y composición;
3. perfilar allocations sólo dentro de esos dominios y reducir cadencias únicamente para
   paneles no críticos;
4. repetir la matriz Flight 7, J, staging, docking, EDL y catch antes de promover cualquier
   cambio de calidad o de scheduler.

La hibernación física permanece desactivada: el diagnóstico no autoriza saltar ticks de
`Vessel.Tick` ni diferir systems.
