# Fase 3 — instrumentación renderer-backed

Fecha de la instrumentación: 2026-08-11

## Alcance y método

La fase añade `tools/perf/renderer_benchmark.sh`. El script delega la carga de
escena, el renderer OpenGL3 bajo Xvfb, la captura PNG y la limpieza temporal a
`tools/visual_playtest.sh`. Sus artefactos persistentes son un informe
`key=value`, un manifiesto de capturas y copias de los logs en el directorio de
salida indicado.

Modos soportados:

- `pad` → `tools/visual_playtest.sh --smoke`.
- `cockpit` → `tools/visual_playtest.sh --cockpit`.
- `ascent` → `tools/visual_playtest.sh --ascent`.

El proceso completo se mide con `/usr/bin/time -v`. Por ello `rss_max_kib` es
el máximo de memoria residente del proceso y sus hijos observado por GNU time;
no es VRAM. `wall_seconds` incluye arranque, build si no se usa `--skip-build`,
carga de escena y captura. `wall_frames_per_sec` es sólo
`SUMMARY.frames / wall_seconds`, una señal de throughput del proceso, no una
medición de FPS de GPU.

## Disponibilidad de percentiles

El Godot 4.6.3 instalado acepta `--benchmark-file`, pero el JSON producido en
una prueba de cinco iteraciones sólo contiene fases de arranque/shutdown como
`[Startup] Main::Setup` y `[Scene] Load Game`; no contiene muestras de cada
frame. No se usa ese JSON para inventar p50/p95/p99.

El harness calcula `frame_time_p50_ms`, `frame_time_p95_ms`, `frame_time_p99_ms`
y los FPS derivados únicamente cuando el log contiene líneas explícitas con
esta forma:

```text
PERF_FRAME frame_ms=<positive-finite-number>
```

También acepta el marcador equivalente `PERF_RENDER ... frame_ms=...`. El
harness temporal emite ahora el intervalo de pared del callback explícitamente.
Estas muestras detectan stalls y regresiones del proceso Godot completo, pero no
son tiempo de GPU ni incluyen una consulta de timestamp del driver. La ausencia
de telemetría GPU no se convierte en una aprobación de 60 FPS.

Las métricas GPU permanecen explícitamente sin medir:

```text
gpu_frame_time_p50_ms=NOT_MEASURED
gpu_frame_time_p95_ms=NOT_MEASURED
gpu_frame_time_p99_ms=NOT_MEASURED
gpu_vram_bytes=NOT_MEASURED
```

El backend utilizado en esta máquina es `opengl3_xvfb` con framebuffer
1920×1080×24 y rasterización CPU/llvmpipe. No se afirma tiempo GPU, VRAM,
draw calls, overdraw ni residencia del driver.

### Resultado validado del perfil interactivo v21

La ejecución reproducible más reciente produjo:

```text
status=PASS
mode=pad
frame_samples=50
frame_time_p50_ms=982.000
frame_time_p95_ms=1219.000
frame_time_p99_ms=2659.000
wall_seconds=60.550000
rss_max_kib=1246864
capture_valid=true
```

El LUT RGB oficial sigue siendo orden 4, pero el perfil interactivo v21 reduce
las dimensiones de las tablas y muestras de integración. En esta máquina el
worker pasó de la observación previa de aproximadamente 133000 ms a 8195 ms de
CPU y 9228 ms de wall durante el smoke; la subida RGBA ocupa 229376 bytes y el
presupuesto estimado del worker es 362496 bytes. La imagen de pad conservó
`mean=0.02847`, `clippedFrac=0.00062` y `neonGreenFrac=0.000000`.

Estos números prueban una reducción de la espera de generación y ausencia de
regresión cromática evidente en el smoke, pero no prueban fluidez en hardware
real: llvmpipe registró intervalos de callback del orden de cientos de
milisegundos. El gate de 60 FPS queda pendiente de una corrida con GPU y
timestamps de render.

La ruta crítica de Starship también fue verificada con el harness de ascenso:

```text
finish: ASCENT_ORBIT_OK
orbit: 158×145 km, e=0.001
```

El worker terminó antes de `LIFTOFF`, `ASCENT_SH`, `SEPARATION` y
`ASCENT_SHIP`; la prueba no mostró el bloqueo de carga observado con el perfil
anterior.

## Smoke real ejecutado

Comando:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  bash tools/perf/renderer_benchmark.sh \
  --mode pad --run-id phase3-smoke --skip-build \
  --out-dir /tmp/exo_renderer_phase3_smoke
```

El resultado observado debe conservarse en
`/tmp/exo_renderer_phase3_smoke/renderer_metrics.tsv`; el manifiesto exacto
de PNG y bytes queda en `capture_manifest.tsv`. El esquema esperado es:

| Campo | Interpretación |
|---|---|
| `status` | `PASS` sólo si el smoke terminó y la captura es PNG válida |
| `frame_count` | frames del `SUMMARY` del harness visual |
| `frame_samples` | muestras explícitas `PERF_FRAME`/`PERF_RENDER` |
| `frame_time_p50/p95/p99_ms` | percentiles reales, o `NA` si no fueron emitidos |
| `wall_seconds` | wall time del proceso completo |
| `wall_frames_per_sec` | throughput con startup incluido; no es FPS GPU |
| `rss_max_kib` | máximo RSS observado por GNU time |
| `capture_count`, `capture_bytes` | inventario exacto de capturas |
| `gpu_frame_time_*`, `gpu_vram_bytes` | `NOT_MEASURED` |

## Contrato y gates

`tools/perf/renderer_benchmark_contract_test.sh` valida:

1. Sintaxis Bash del harness.
2. Formato estricto `key=value`, campos obligatorios y modos admitidos.
3. `status=PASS`, salida visual cero, captura PNG válida y RSS numérico.
4. Percentiles `NA` cuando no hay muestras; valores positivos cuando sí las hay.
5. GPU/VRAM no se etiquetan como medidas sin una fuente explícita.
6. Rechazo de fixtures con `FAIL`, `NAN`, líneas malformadas o campos ausentes.

Comandos de verificación:

```bash
bash -n tools/perf/renderer_benchmark.sh \
  tools/perf/renderer_benchmark_contract_test.sh
bash tools/perf/renderer_benchmark_contract_test.sh
```

## Limitación y siguiente gate

La siguiente fase debe medir una build con GPU real usando el profiler de Godot
o timestamps de render, y repetir pad, cockpit y ascenso con p50/p95/p99 por
debajo del presupuesto acordado. También debe separar tiempo de simulación,
tiempo de render y tiempo GPU. No se debe promover orden 5 ni apagar ticks de
física basándose únicamente en estas mediciones de llvmpipe.
