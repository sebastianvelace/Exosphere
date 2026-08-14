# Experimento aislado de texturas Earth/estrellas — fase 20

Estado: variante 4K con mipmaps no promovida  
Fecha: 2026-08-14  
Baseline: `77b2f09`  
Backend observado: Mesa llvmpipe (LLVM 20.1.2, 256 bits)

## Decisión

Se probó una variante aislada que activa mipmaps y limita a 4096 px los cuatro recursos
de mayor tamaño:

- `earth_day.jpg`
- `earth_night.jpg`
- `earth_clouds.jpg`
- `starmap_milkyway_8k.jpg`

Los cambios sólo existieron en un worktree temporal y fueron retirados. No hay cambios de
producción en los `.import`, no cambia la resolución fuente de 8192×4096 y no se afirma que
el tamaño del caché de importación sea equivalente a VRAM residente.

La variante queda **rechazada para promoción** en esta fase. La reducción de caché es
prometedora, pero la matriz atmosférica completa no terminó en este host: llvmpipe hizo que
la corrida excediera el tiempo práctico de validación y se detuvo después de los cuatro
primeros escenarios Earth. El harness no emitió `ATMOSPHERE_OK`; por tanto, esos cuatro
casos no se presentan como una validación completa.

## Tamaño del caché de importación

Medición posterior a `Godot --headless --path . --import` en el worktree candidato:

| Recurso | Baseline sin mipmaps | Candidato 4K+mipmaps | Cambio |
|---|---:|---:|---:|
| `earth_clouds` | 19.32 MiB | 7.32 MiB | −62.1% |
| `earth_day` | 13.05 MiB | 5.85 MiB | −55.2% |
| `earth_night` | 5.88 MiB | 2.66 MiB | −54.8% |
| `starmap_milkyway_8k` | 3.39 MiB | 2.09 MiB | −38.3% |
| Caché `.godot/imported` completo | 62.06 MiB | 38.34 MiB | −38.2% |

La última fila incluye recursos adicionales del proyecto. El resultado mide bytes de
artefactos importados en disco; no mide asignación de textura, residencia de VRAM ni RSS
del proceso.

## Validación visual ejecutada

Los escenarios independientes sí completaron sus gates:

| Escenario | Resultado | Captura | Estado físico observado |
|---|---|---|---|
| `--smoke` | `SMOKE_OK`, 50 frames | pad | arranque y carga diferida correctos |
| `--cockpit` | `COCKPIT_OK`, 115 frames | órbita Earth | tres instrumentos legibles a 30 Hz |
| `--saturn` | `SATURN_OK`, 170 frames | órbita Saturn | cuerpo, anillos y estrellas visibles |

La corrida atmosférica produjo capturas válidas de los cuatro primeros escenarios antes de
ser detenida:

| Escenario | Media | P95 | Clipping | Observación |
|---|---:|---:|---:|---|
| Earth día | 0.45806 | 0.82745 | 2.245% | superficie y cielo coherentes |
| Earth amanecer | 0.00847 | 0.04314 | 0% | transición oscura, sin clipping |
| Earth atardecer | 0.02164 | 0.11373 | 0.497% | gradiente cálido visible |
| Earth noche | 0.00007 | 0 | 0% | estrellas visibles, sin saturación |

La inspección de esas imágenes y de cockpit/Saturn no mostró artefactos cromáticos obvios,
estrellas ausentes ni aliasing evidente. Eso es una comprobación de apariencia, no una
prueba de equivalencia pixel a pixel: faltan el resto de altitudes, órbita, terminador
completo y la matriz de planetas requerida.

## Límite de la medición

Todos los runs gráficos observaron `real_gpu_observed=false` y llvmpipe. En consecuencia:

1. no se publican FPS ni VRAM física;
2. no se compara el coste de muestreo de mipmaps contra una GPU objetivo;
3. no se puede decidir si 4K mantiene la separación rojo/azul del terminador bajo carga;
4. el tiempo de arranque del candidato no se compara como mejora, porque depende de caché,
   importación y del backend software.

## Gate para retomar la promoción

Un agente con GPU física debe repetir, con el mismo commit y resolución de ventana, estas
variantes: 8K sin mipmaps, 8K con mipmaps, 4K con mipmaps y 2K con mipmaps. Debe registrar
RSS, VRAM del driver, tiempo de frame p50/p95/p99, draw calls, clipping y métricas de día,
terminador, noche, estrellas y Saturn. Sólo se puede promover 4K si completa la matriz,
conserva los gates visuales y no excede los límites de calidad definidos en el baseline.

