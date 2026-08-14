# Fase 15 — probe de render in-process

Estado: instrumentación aplicada; no activa en gameplay normal
Fecha: 2026-08-13
Alcance: `RenderPerformanceProbe`, `phase4_gpu_probe.sh`, Flight sandbox

## Objetivo

La fase 13 dejó identificado que el shell no podía medir GPU. Esta fase añade una fuente
real dentro del proceso Godot, pero la mantiene opt-in para no introducir trabajo de
medición en una sesión normal.

Se activa sólo con:

```bash
EXOSPHERE_RENDER_PROBE=1
```

El probe habilita `RenderingServer.ViewportSetMeasureRenderTime` para el viewport principal
y muestrea cada 0.5 s, después de tres frames de calentamiento. Emite:

- tiempo de render CPU y GPU del viewport;
- objetos, primitivas y draw calls del renderer;
- `VideoMemUsed` de `RenderingServer`;
- driver, método, vendor y adaptador observados.

Al salir deshabilita la medición. No llama a `OS.GetVideoAdapterDriverInfo`, que Godot
documenta como potencialmente bloqueante, y no toca `Universe`, física, materiales ni
calidad visual.

## Corrida reproducible

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/phase4_gpu_probe.sh --run --display xvfb --driver opengl3 \
  --method gl_compatibility --frames 60 --timeout 180 \
  --out-dir /tmp/exo_phase15_render_probe
```

Resultado del reporte `/tmp/exo_phase15_render_probe/gpu_probe.tsv`:

| Métrica | p50 | p95 | p99 | Fuente |
|---|---:|---:|---:|---|
| muestras | 14 | 14 | 14 | `PERF_GPU` in-process |
| render CPU | 917.675 ms | 1041.950 ms | 1041.950 ms | `ViewportGetMeasuredRenderTimeCpu` |
| render GPU/backend | 921.261 ms | 1027.096 ms | 1027.096 ms | `ViewportGetMeasuredRenderTimeGpu` |
| draw calls | 15,772 | 15,775 | 15,775 | `GetRenderingInfo` |
| primitivas | 1,218,406 | 1,218,442 | 1,218,442 | `GetRenderingInfo` |
| objetos | 9,774 | 9,777 | 9,777 | `GetRenderingInfo` |
| `VideoMemUsed` | 599,302,046 B | 599,302,046 B | 599,302,046 B | `GetRenderingInfo` |

El adaptador observado fue `Mesa llvmpipe (LLVM 20.1.2, 256 bits)`, con
`real_gpu_observed=false` y `software_renderer_detected=true`. Por eso los tiempos GPU
numéricos son válidos como telemetría del backend in-process, pero **no** son un objetivo de
hardware ni prueban FPS de una GPU real. `gpu_vram_bytes` y FPS permanecen
`NOT_MEASURED`; `VideoMemUsed` tampoco se presenta como VRAM física.

## Gates

- `render_performance_probe_contract_test.sh`: PASS.
- `phase4_gpu_probe_contract_test.sh`: PASS con fixture audit-only, fixture medido y
  rechazos de fuente ausente, muestras inconsistentes, FPS inventado y campos faltantes.
- `phase4_gpu_probe.sh --validate /tmp/exo_phase15_render_probe/gpu_probe.tsv`: PASS.
- `Exosphere.csproj`: 0 warnings, 0 errors tras enlazar la API Godot 4.6.3.

## Decisión

No se cambia calidad de render con esta corrida. El probe queda listo para repetir la misma
matriz en una GPU real; sólo entonces se pueden comparar Forward+/Vulkan, Compatibility,
sombras, texturas, partículas y LOD con un criterio de promoción.
