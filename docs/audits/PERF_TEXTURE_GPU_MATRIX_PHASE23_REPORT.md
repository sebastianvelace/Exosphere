# Auditoría de matriz GPU física — fase 23

Estado: **BLOCKED (gate fail-closed correcto)**
Fecha: 2026-08-14
Host: Linux del entorno de desarrollo, Godot 4.6.3 mono, Mesa/llvmpipe

## Alcance

Esta oleada valida únicamente si el adaptador observado es una GPU física. No cambia
runtime, `project.godot`, shaders ni configuración del juego. La matriz no convierte
tiempos de proceso, contadores de memoria o datos de llvmpipe en afirmaciones de FPS,
VRAM o rendimiento de GPU física.

## Comando reproducible

El checkout compartido tenía un autoload temporal de otro harness y no se modificó. La
corrida se ejecutó desde un clon limpio efímero en `/tmp`, con sus propios worktrees:

```bash
xvfb-run -a env \
  GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/texture_gpu_matrix.sh --run \
  --display xvfb --driver opengl3 --method gl_compatibility \
  --frames 20 --timeout 60 --import-timeout 120 \
  --offline --run-id n3-phase23 \
  --out-dir /tmp/exo_phase23_gpu_n3_xvfb
```

`--offline` sólo permite que `dotnet restore` ignore un feed NuGet inaccesible y use el
caché local. No altera el gate gráfico. La matriz se ejecutó sin `--allow-software`.

## Resultado

`matrix.meta` quedó así:

```text
status=BLOCKED
physical_gpu_gate=BLOCKED
software_renderer_observed=true
rendering_driver_request=opengl3
rendering_method_request=gl_compatibility
restore_mode=ignore_failed_sources
```

La primera variante completó restore, build e importación:

| Variante | Estado | Probe | Caché importado | Adaptador no software |
|---|---:|---:|---:|---:|
| `8k_nomip` | `PASS` de la variante | `MEASURED` | `65,076,252` bytes | `false` |
| `8k_mip` | `NOT_RUN` | `NOT_RUN` | `NOT_MEASURED` | `unknown` |
| `4k_mip` | `NOT_RUN` | `NOT_RUN` | `NOT_MEASURED` | `unknown` |
| `2k_mip` | `NOT_RUN` | `NOT_RUN` | `NOT_MEASURED` | `unknown` |

El `PASS` de `8k_nomip` sólo significa que la variante y el probe terminaron; no es un
PASS del gate físico.

## Evidencia del adaptador

El probe in-process de Godot reportó:

```text
rendering_method_observed=gl_compatibility
rendering_driver_observed=opengl3
adapter_observed=Mesa_-_llvmpipe_(LLVM_20.1.2,_256_bits)
adapter_source=godot_startup_header
real_gpu_observed=false
software_renderer_detected=true
```

El log de arranque contiene literalmente:

```text
OpenGL API 4.5 (...) - Compatibility - Using Device:
Mesa - llvmpipe (LLVM 20.1.2, 256 bits)
```

Además, el preflight del host no encontró `nvidia-smi`, `vulkaninfo` ni `glxinfo`. Esto es
coherente con la clasificación software, pero la decisión se basa principalmente en la
evidencia emitida por Godot, no en la ausencia de esas herramientas.

## Política de métricas

El probe produjo telemetría in-process de RenderingServer para verificar que el harness
funciona bajo llvmpipe. Esos valores no se publican como rendimiento de GPU física. En
particular:

- `fps_*` quedó `NOT_MEASURED`.
- `gpu_vram_*` quedó `NOT_MEASURED`.
- El contador `render_video_mem_*` no se interpreta como VRAM física.
- Los tiempos observados bajo llvmpipe no sirven para comparar hardware.

## Requisito externo para desbloquear

Repetir la misma matriz en una máquina con una GPU física y un driver instalado (NVIDIA,
AMD o Intel), preferentemente con una sesión gráfica nativa y la configuración adecuada
para ese driver:

```bash
tools/perf/texture_gpu_matrix.sh --run \
  --display native --driver vulkan --method forward_plus \
  --resolution 1920x1080 --frames 120 \
  --out-dir /tmp/exo_texture_gpu_matrix_physical
```

El desbloqueo requiere que el probe de cada variante observe un adaptador que no sea
`llvmpipe`/software, con `software_renderer_detected=false`, y que la matriz termine las
cuatro variantes con `nonsoftware_adapter=true`. Hasta entonces, `BLOCKED` es el único
resultado aceptable; no se debe promover 4K, publicar FPS/VRAM ni tomar una decisión de
calidad basada en esta corrida.

## Validación de contratos

Ejecutado en el checkout de trabajo:

```bash
bash -n tools/perf/texture_gpu_matrix.sh
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/texture_gpu_matrix.sh --validate /tmp/exo_phase23_gpu_n3_xvfb
bash tools/tests/optimization_phase23_contract_test.sh
```

Resultado: sintaxis PASS, matriz válida y contrato `pass=25 fail=0`.

## Artefactos

- Matriz: `/tmp/exo_phase23_gpu_n3_xvfb/matrix.meta`
- Filas: `/tmp/exo_phase23_gpu_n3_xvfb/matrix.rows.tsv`
- Probe: `/tmp/exo_phase23_gpu_n3_xvfb/8k_nomip/probe/gpu_probe.tsv`
- Log de Godot: `/tmp/exo_phase23_gpu_n3_xvfb/8k_nomip/probe/godot.log`
- Log de arranque: `/tmp/exo_phase23_gpu_n3_xvfb/8k_nomip/probe/godot.stdout`
