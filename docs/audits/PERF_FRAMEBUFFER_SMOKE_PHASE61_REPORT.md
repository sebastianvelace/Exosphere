# Fase 61 — smoke de framebuffer y separación de costes

Fecha: 2026-08-17  
Estado: **SMOKE_PASS / PERF_AB_BLOCKED**

## Objetivo

Obtener una captura real del juego y correlacionar el tiempo total de frame con el
scheduler y el worker atmosférico, sin atribuir el coste de llvmpipe a la física.

## Corrida válida

Se ejecutó:

```bash
bash tools/visual_playtest.sh \
  --smoke --run-id phase61-framebuffer --skip-build
```

El proceso arrancó Godot 4.6.3 con OpenGL3/Xvfb a 1920×1080×24 usando Mesa llvmpipe.
El build del proyecto terminó con **0 warnings / 0 errors** y el gate visual terminó en
`SMOKE_OK` con un PNG de 189 KiB. Artefactos reproducibles:

- `/tmp/exo_play-phase61-framebuffer/exo_play_pad.png`;
- `/tmp/exo_play-phase61-framebuffer/run-summary.txt`;
- `/tmp/exo_play-phase61-framebuffer.log`.

## Telemetría observada

| Medida | Resultado |
|---|---:|
| frames del smoke | 50 |
| frame medio | 1,884.660 ms |
| frame p50 / p95 / p99 | 1,802 / 2,152 / 5,020 ms |
| scheduler medio | 1.791 ms |
| scheduler p50 / p95 / p99 | 1.008 / 1.891 / 37.864 ms |
| rama | `FullPhysics` |
| substeps por frame | 8–9 |
| trabajo observado | 8–9 `ground_held` |
| riesgo de catch-up | `false` |
| atmósfera Earth | worker asíncrono, orden 4 |
| worker CPU / cola | 12,877.7 / 17.6 ms |
| upload / bytes retenidos / pico | 229,376 / 344,064 / 362,496 bytes |
| imagen | PNG válido, `clippedFrac=0.00062`, `neonGreenFrac=0` |

El frame completo tarda aproximadamente tres órdenes de magnitud más que el scheduler en
este backend. Esto no es un FPS de hardware de jugador: es evidencia de que el coste de la
captura está fuera de `Universe.Tick` y debe seguirse midiendo en el renderer/presentación.
El worker LUT no bloqueó el hilo principal; la escena permaneció viva mientras progresaba de
`queued` a `completed`.

## A/B de GPU

El wrapper `tools/perf/renderer_benchmark.sh` no pudo completar una corrida: el Godot hijo
recibió `X11 Display is not available`. La causa del host quedó confirmada con `Xvfb`:
`/tmp/.X11-unix` pertenece a `nobody:nogroup`, y el servidor rechaza crear listeners locales
(`Owner of /tmp/.X11-unix should be set to root`). El intento de reparación sin privilegios
no pudo modificar esa propiedad y no se altera la configuración del sistema desde el repo.

Por la misma razón, las variantes `sky_quality_low` y la configuración oficial `0.60` no
forman un par A/B válido en esta fase. El override `0.25` permanece experimental y no se
promueve. Tampoco se publican FPS, VRAM ni una mejora visual cuantificada.

## Decisión

- **Promover:** ninguna modificación de runtime en esta fase; el smoke y la correlación de
  telemetría son válidos.
- **Mantener:** `InteractiveAtmosphereQuality=0.60`, LUT RGB de orden 4, plumas agregadas,
  caches de presentación y scheduler sin cambios de presupuesto.
- **Siguiente gate:** repetir el A/B con un host donde Xvfb pueda crear el socket y donde haya
  GPU física o, como mínimo, un framebuffer llvmpipe reproducible; ejecutar `pad`, `cockpit`,
  `ascent`, `edl` y la matriz Earth/Mars/Venus antes de promover una calidad atmosférica o
  una hibernación física.

