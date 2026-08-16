# Fase 38 — diagnóstico de render/presentación y terreno marciano lazy

Fecha: 2026-08-15  
Área: `RenderPerformanceProbe`, `MarsTerrainController` y playtest framebuffer

## Hallazgo principal

La correlación de fase 37 se confirmó con el probe de Godot: el atasco de entrada no está en
`Universe.Tick`. En el host de validación el renderer es Mesa llvmpipe, y el coste de CPU/GPU
del viewport domina el frame.

## Medición reproducible

Smoke framebuffer con `EXOSPHERE_RENDER_PROBE=1`, 11 muestras después del calentamiento:

| Escenario | CPU render mediana | GPU mediana | objetos | primitivas | draw calls | VRAM |
|---|---:|---:|---:|---:|---:|---:|
| Earth pad normal | 1,098.077 ms | 1,102.228 ms | 9,774 | 1,218,406 | 15,772 | ~598 MB |
| A/B `hide_pad` | 991.570 ms | 996.235 ms | 9,774 | 1,218,406 | 15,772 | ~598 MB |
| A/B `no_directional_shadows` | 984.074 ms | 987.809 ms | 8,035 | 982,074 | 12,293 | ~598 MB |
| A/B `hide_sky` | 416.502 ms | 424.003 ms | 9,773 | 1,218,394 | 15,772 | ~598 MB |

Cada corrida terminó con `SMOKE_OK` y captura PNG válida. `hide_pad` no cambió los contadores
del renderer, así que no se promoverá una optimización del pad sin una medición por nodo o una
captura en hardware físico. Desactivar todas las sombras direccionales reduce aproximadamente
22% los draw calls y primitivas, pero cambia la lectura visual; queda como candidato de calidad
`Low`, no como cambio oficial predeterminado. `hide_sky` redujo aproximadamente 62% el tiempo
de render en este host, confirmando que el sky shader atmosférico es el cuello de botella
dominante; eliminarlo no es una solución visual aceptable.

## Cambio seguro promovido

`MarsTerrainController` ya no construye su malla 96×96 durante el arranque Earth. `_Ready` deja
el nodo invisible y `EnsureMesh()` crea la malla sólo cuando el cuerpo dominante es Mars y la
nave está dentro de `ShowAlt`. La construcción emite `PERF_RENDER stage=mars_terrain_build`
para medir el coste puntual en la primera aproximación a Mars. El smoke Earth no emitió ese
evento. La primera aproximación a Mars puede pagar todavía una construcción síncrona; la fase
siguiente debe decidir si se precalcula fuera del frame o se usa una malla cacheada.

## Instrumentación A/B

El probe opt-in acepta `EXOSPHERE_RENDER_AB` con estas variantes: `hide_pad`, `hide_sky`,
`no_directional_shadows`, `hide_launch_effects`, `hide_vessel`, `hide_hud`, `hide_starfield`
y `hide_earth_ground`. No tienen efecto si `EXOSPHERE_RENDER_PROBE` no está activado y no
alteran el juego normal.

## Verificación y límites

- Build Godot: `0 warnings, 0 errors`.
- `render_performance_probe_contract_test`: PASS.
- Contrato de rendimiento: `34 PASS, 1 dynamic skip` sin log.
- Los smoke A/B terminaron `SMOKE_OK` y conservaron PNGs válidas.
- El host no representa una GPU física; no se convierte la cifra llvmpipe en un objetivo de
  FPS para hardware del usuario.

## Decisión y siguiente paso

Promover únicamente el terreno Marciano lazy y la instrumentación opt-in. Mantener sombras
oficiales y el shader atmosférico sin cambios hasta ejecutar A/B en GPU física. La siguiente
fase debe medir por escenario las partículas de ignición, plumas al 100%, HUD y reentrada, y
comparar una opción de sombras de baja calidad con gates visuales explícitos.
