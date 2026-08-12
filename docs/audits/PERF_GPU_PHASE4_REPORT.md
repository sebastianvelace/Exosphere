# Fase 4 — Límite de medición GPU

Fecha: 2026-08-12
Commit base: `1d74de0`
Regla: este documento no convierte tiempos de proceso ni un nombre de adaptador en
tiempo GPU o VRAM.

## Resultado

El probe fail-closed es [`phase4_gpu_probe.sh`](../../tools/perf/phase4_gpu_probe.sh).
La corrida ejecutada fue:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/phase4_gpu_probe.sh \
  --display headless --run --frames 8 --timeout 60 \
  --out-dir /tmp/exo_phase4_gpu_probe_real
```

Resultado observado:

```text
status=NOT_MEASURED
godot_process_exit_code=0
rendering_driver_observed=dummy
real_gpu_observed=false
software_renderer_detected=true
benchmark_phase_count=35
benchmark_scope=startup_shutdown_only
gpu_frame_time_p50_ms=NOT_MEASURED
gpu_frame_time_p95_ms=NOT_MEASURED
gpu_frame_time_p99_ms=NOT_MEASURED
gpu_vram_bytes=NOT_MEASURED
fps_p50=NOT_MEASURED
fps_p95=NOT_MEASURED
fps_p99=NOT_MEASURED
```

El archivo de benchmark de Godot sólo aportó fases de arranque/apagado. El probe
rechaza numéricos GPU/FPS y estados `PASS` mientras no exista una fuente in-process.
Esto es intencional: el renderer dummy/headless sirve para smoke de carga, no para
aprobar fluidez.

## Fuente que falta para aprobar hardware

La instrumentación futura debe vivir en C# y medir por viewport con:

- `RenderingServer.ViewportSetMeasureRenderTime` y
  `RenderingServer.ViewportGetMeasuredRenderTimeGpu` para tiempo GPU.
- `RenderingServer.GetRenderingInfo` para draw calls/primitivas y contadores del
  renderer.
- `RenderingDevice.GetDriverAndDeviceMemoryReport` sólo en Vulkan y con el tracking
  adicional habilitado para memoria del driver.

El shell no puede producir esas mediciones. La siguiente prueba debe ejecutarse en una
GPU real, registrar p50/p95/p99 separados de `PERF_FRAME` del proceso y conservar una
captura pad/cockpit/orbit para el gate visual.

## Contrato

[`phase4_gpu_probe_contract_test.sh`](../../tools/perf/phase4_gpu_probe_contract_test.sh)
acepta un único fixture `NOT_MEASURED` y rechaza métricas numéricas sin fuente,
`PASS` prematuro, campos faltantes y líneas malformadas.
