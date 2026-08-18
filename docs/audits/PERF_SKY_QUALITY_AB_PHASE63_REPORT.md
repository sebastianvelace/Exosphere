# Fase 63 — A/B de calidad atmosférica en framebuffer

Fecha: 2026-08-18  
Área: `space_sky.gdshader`, `visual_playtest.sh` y telemetría de render

## Objetivo

Cerrar el A/B que había quedado bloqueado en la fase 61: comparar el perfil oficial de
calidad atmosférica `0.60` (A) con el override diagnóstico `0.25` (B), usando la misma
escena física, resolución y backend. El A/B mide coste de render y estabilidad visual; no
cambia la física, el LUT RGB oficial de orden 4 ni el coste por frame del juego normal.

## Entorno y reproducción

Las corridas se hicieron en Godot 4.6.3, OpenGL3 sobre Mesa llvmpipe en 1920×1080. Es un
backend de rasterización por software: los milisegundos son comparables dentro de este
host, pero no son una promesa de FPS para una GPU física.

```bash
EXOSPHERE_RENDER_PROBE=1 EXOSPHERE_RENDER_AB=sky_quality_low \
  bash tools/visual_playtest.sh --atmosphere --run-id phase63-atmo-low \
  --out-dir /tmp/exo_play-phase63-atmo-low \
  --log /tmp/exo_play-phase63-atmo-low.log --max-runtime 1800 --skip-build

EXOSPHERE_RENDER_PROBE=1 EXOSPHERE_RENDER_AB=sky_quality_official \
  bash tools/visual_playtest.sh --atmosphere --run-id phase63-atmo-official \
  --out-dir /tmp/exo_play-phase63-atmo-official \
  --log /tmp/exo_play-phase63-atmo-official.log --max-runtime 1800 --skip-build
```

La matriz Earth produjo 20/20 capturas en cada perfil y terminó `ATMOSPHERE_OK`. La matriz
Mars/Venus produjo 6/6 capturas en cada perfil y terminó `ATMOSPHERE_BODIES_OK`. El gate del
harness se corrigió para aceptar el campo métrico `frames=N` que `Finish()` añade al resumen,
sin dejar de exigir exactamente una marca terminal.

## Coste observado

Tras descartar los primeros 10 samples de calentamiento de cada matriz Earth:

| Perfil | Samples | Render medio | p50 | p95 |
|---|---:|---:|---:|---:|
| A oficial `0.60` | 400 | 617.102 ms | 672.722 ms | 791.407 ms |
| B diagnóstico `0.25` | 402 | 408.725 ms | 448.780 ms | 519.975 ms |

B reduce el tiempo de render observado aproximadamente 33.8% de media, 33.3% en p50 y
34.3% en p95 en este host. En el smoke inicial, más dominado por la puesta en marcha, la
reducción GPU fue 21.4% de media. Objetos, primitivas, draw calls y memoria de vídeo se
mantuvieron en la misma escala; la mejora procede de las muestras del shader, no de ocultar
la escena ni de descargar la física.

La generación atmosférica asíncrona también finalizó sin bloqueo: Earth fue 11.1 s en A y
12.1 s en B en las corridas completas; Mars fue 7.7 s y 7.1 s; Venus fue 72.0 s y 69.1 s.
Estos tiempos incluyen el worker/LUT en llvmpipe y no deben interpretarse como latencia por
frame.

## Estabilidad visual

La tabla resume métricas del mismo slug físico. Las pequeñas diferencias de exposición y
redondeo se mantienen en los gates existentes; las estrellas y los casos de eclipse
conservan el mismo comportamiento.

| Escena | A mean / clip | B mean / clip | Señal relevante |
|---|---:|---:|---|
| Earth ground day | 0.46225 / 0.02397 | 0.45631 / 0.02295 | terminador estable |
| Earth sunrise | 0.00846 / 0.00000 | 0.00847 / 0.00000 | calidez 0.1448 vs 0.1448 |
| Earth sunset | 0.02160 / 0.00501 | 0.02128 / 0.00495 | calidez 0.3335 vs 0.3329 |
| Earth night | 0.00004 / 0.00000 | 0.00004 / 0.00000 | estrellas 0.000320 en ambos |
| Earth partial central | 0.12100 / 0.00000 | 0.12109 / 0.00000 | estrellas 0.000137 en ambos |
| Earth total | 0.02066 / 0.00003 | 0.02070 / 0.00003 | estrellas 0.000503 en ambos |

En los casos atmosféricos Earth y Mars, `neonGreenFrac` y `limbGreenExcess` permanecieron
en cero. El máximo Earth aparece sólo en el cockpit y fue `0.000595`/`0.000152` en B,
frente a `0.000521`/`0.000150` en A; no constituye un halo verde amplio.

Mars mantuvo la misma lectura física y visual en 10 km día, 400 km día y 10 km noche. Venus
también pasó el gate físico y de imagen, pero su atmósfera densa es un límite importante:
en 10 km día el clipping existente fue aproximadamente 47% en ambos perfiles y el mean
varió de `0.57105` en A a `0.62261` en B. Esto demuestra que B es sensible en el perfil más
opaco y que la matriz no justifica ocultar ese problema bajo una reducción de calidad.

## Decisión

- Mantener `InteractiveAtmosphereQuality=0.60` como ruta oficial y valor predeterminado.
- Mantener `sky_quality_low=0.25` como override de probe/diagnóstico; no se activa
  automáticamente en el juego.
- No promover todavía un preset Low global: aunque la ganancia relativa es clara y Earth/Mars
  pasan sin regresiones, Venus a baja altitud presenta una diferencia cromática medible y
  clipping alto, y la evidencia de coste procede de llvmpipe.
- El siguiente gate de promoción debe repetir los seis casos en una GPU física y exigir un
  presupuesto explícito de diferencia cromática para Venus, además de conservar los gates de
  clipping, exposición, estrellas, terminador y eclipse.
- La calidad del sky no altera la decisión espectral: el renderer sigue usando RGB y orden 4;
  el oráculo espectral y el orden 5 permanecen herramientas de validación/precompute.

## Verificación

- `bash -n tools/visual_playtest.sh`: PASS.
- `bash tools/tests/optimization_phase23_contract_test.sh`: PASS, 42/42.
- Matriz Earth B: `ATMOSPHERE_OK`, 20/20 PNG válidas.
- Matriz Earth A: `ATMOSPHERE_OK`, 20/20 PNG válidas.
- Matriz Mars/Venus B: `ATMOSPHERE_BODIES_OK`, 6/6 PNG válidas; `--verify-only`: PASS.
- Matriz Mars/Venus A: `ATMOSPHERE_BODIES_OK`, 6/6 PNG válidas.
- Los builds lanzados por las matrices: 0 warnings, 0 errores.

