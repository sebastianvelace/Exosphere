# Fase 13 — baseline de render y límite de medición GPU

Estado: baseline registrado; sin cambio de runtime
Fecha: 2026-08-13
Alcance: `tools/perf/phase4_gpu_probe.sh`, Flight sandbox, OpenGL3/Xvfb

## Resultado reproducible

Comando:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/phase4_gpu_probe.sh --run --display xvfb --driver opengl3 \
  --method gl_compatibility --frames 60 --timeout 180 \
  --out-dir /tmp/exo_phase13_gpu_probe_xvfb
```

| Campo | Resultado |
|---|---:|
| backend | OpenGL 4.5 Compatibility |
| adapter | Mesa llvmpipe LLVM 20.1.2 |
| GPU real | `false` |
| `SimulationLoaded` | 1649.4 ms |
| worker LUT CPU | 7578.7 ms |
| cola worker | 2.6 ms |
| upload reportado | 229376 bytes |
| pico de buffers CPU | 362496 bytes |
| benchmark startup/shutdown | 35 fases |
| GPU frame time / VRAM / FPS | `NOT_MEASURED` |

El probe terminó con Godot exit 0 y contrato PASS. Su estado `NOT_MEASURED` es deliberado:
ni el intervalo del proceso, ni el nombre del adapter, ni llvmpipe se convierten en una
afirmación de GPU. Las APIs que deben usarse en una máquina compatible quedan identificadas:
`RenderingServer.ViewportGetMeasuredRenderTimeGpu`, `RenderingServer.GetRenderingInfo` y,
para Vulkan, `RenderingDevice.GetDriverAndDeviceMemoryReport`.

## Decisiones

- No se cambia la calidad del sky, sombras, partículas, resolución ni materiales a partir de
  este host.
- El callback observado en llvmpipe (~0.7–1.1 s en el smoke) sirve para detectar stalls y
  regresiones funcionales, no como objetivo de FPS.
- El dirty cache de plumas queda rechazado por la comparación controlada de fase 12; no se
  reintroduce sin profiler de setters y GPU real.
- El runtime mantiene lazy planets, LUT worker y calidad atmosférica oficial de orden 4.

## Siguiente despliegue multiagente

1. Agente de laboratorio ejecuta dos baselines en hardware GPU objetivo: Forward+/Vulkan y
   Compatibility/OpenGL si aplica; registra p50/p95/p99 de GPU, CPU render, draw calls,
   material updates, VRAM y resolución.
2. Agente de sky/recursos aísla cubemap, LUT, planetas fuera de cámara, sombras y texturas;
   cada candidato se compara contra el baseline sin tocar física.
3. Agente de plumas/partículas instrumenta setters y `ParticleProcessMaterial`; sólo propone
   dirty state si el coste medido baja y el p95 no empeora.
4. Agente QA repite pad, ascenso, órbita, eclipse, noche y EDL; cualquier candidato conserva
   `ASCENT_ORBIT_OK`, engine-out, staging y ausencia de NaN/clipping amplio.

Hasta que exista ese hardware o una fuente in-process válida, la conclusión rigurosa es
“baseline de software registrado”, no “el juego alcanza X FPS”.
