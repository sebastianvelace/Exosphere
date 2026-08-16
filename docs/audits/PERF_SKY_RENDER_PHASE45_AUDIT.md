# Sky render performance audit — phase 45

Date: 2026-08-15
Commit under audit: `a41f528` (`docs: define phase45 multi-agent performance plan`)

## Bounded result

No phase-45 A/B result is promotable. The renderer-backed A run failed before
the scene started because this host could not provide an X11 framebuffer; B was
not started. There is therefore no new phase-45 quality/cadence delta, no
same-run-ID pair, and no basis for a promotion claim.

The existing diagnostic override `sky_quality_low` remains experimental only.
No cadence override is exposed by the current probe, so changing the 12 Hz
update gate or `RadianceSize=128`/incremental mode is untested and is not a safe
opt-in change on this evidence.

## Current implementation and measurement boundary

`scripts/RenderPerformanceProbe.cs` is opt-in through
`EXOSPHERE_RENDER_PROBE=1`. It accepts `EXOSPHERE_RENDER_AB=sky_quality_low`
(`atmosphere_quality=0.25`) and `sky_quality_min` (`0.0`), and emits:

- `PERF_GPU cpu_render_ms=...` from `RenderingServer.ViewportGetMeasuredRenderTimeCpu`;
- `PERF_GPU gpu_ms=...` from the in-process GPU timer when positive;
- renderer counters for objects, primitives, draw calls, and `VideoMemUsed`.

The production sky configuration in `SkyController` is
`atmosphere_quality=0.60`, `RadianceSize=128`, `ProcessMode=Incremental`, and a
12 Hz atmosphere-parameter update gate. The probe has no cadence A/B selector.

The existing visual harness reports the separate scheduler signal in each
`PERF_FRAME` record:

```text
frame_ms=<callback interval> scheduler_ms=<Universe.Tick wall time>
```

`scheduler_ms` is CPU scheduler time. `frame_ms` is the whole Godot process
callback interval and includes scheduler work, render work, scripting and other
callback overhead; it is not a GPU timestamp. `PERF_GPU cpu_render_ms` is the
renderer CPU timer and is the appropriate render-time signal for this split.

## Commands and results

Preflight passed:

```bash
bash -n tools/perf/phase4_gpu_probe.sh tools/visual_playtest.sh \
  tools/tests/render_performance_probe_contract_test.sh \
  tools/tests/sky_runtime_performance_contract_test.sh \
  tools/tests/performance_acceptance_contract_test.sh
# exit 0

bash tools/tests/render_performance_probe_contract_test.sh
# render_performance_probe_contract_test: PASS

bash tools/tests/sky_runtime_performance_contract_test.sh
# sky_runtime_performance_contract_test: PASS

dotnet build Exosphere.csproj --no-restore --nologo -v quiet
# Build succeeded. 0 Warning(s). 0 Error(s).
```

The bounded A attempt used an explicit run ID and fixed renderer settings:

```bash
GODOT_BIN=/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
EXOSPHERE_RENDER_PROBE=1 EXOSPHERE_RENDER_AB= \
bash tools/visual_playtest.sh --smoke \
  --run-id phase45-sky-a-official \
  --out-dir /tmp/exo_phase45_sky_a \
  --log /tmp/exo_phase45_sky_a.log \
  --max-runtime 300 --skip-build
# exit 1 before scene startup
# ERROR: X11 Display is not available
# no PERF_FRAME, PERF_GPU, capture, or SMOKE_OK evidence
```

The framebuffer diagnostic was independently reproduced:

```bash
xvfb-run --server-num 2000 -e /tmp/exo_xvfb_tcp.err \
  -s '-screen 0 1920x1080x24 -nolisten unix -listen tcp' sh -c \
  'DISPLAY=localhost:2000; export DISPLAY; xdpyinfo'
# Xvfb failed to start
# Cannot establish any listening sockets
```

The host had unreachable stale `/tmp/.X11-unix/X1024` and `X1025` entries owned
by `nobody`; no Xvfb or Godot process remained afterward. The temporary
`_PlaytestShot` harness was absent and `project.godot` was restored. No B command
was run after the A precondition failed.

## Existing evidence, not a new phase-45 run

These values are carried forward from the checked-in phase-39/40 audits and are
not re-used as a new same-run-ID A/B:

| Source | Profile | CPU render median | GPU timer median | Samples | Host limitation |
|---|---|---:|---:|---:|---|
| phase 39 | official `0.60` | 1,098.077 ms | 1,102.228 ms | 11 | Mesa llvmpipe |
| phase 39 | diagnostic `0.25` | 788.115 ms | 795.604 ms | 11 | Mesa llvmpipe |
| phase 40 | official `0.60` | 1,101.086 ms | 1,105.361 ms | 8 | Mesa llvmpipe |
| phase 40 | diagnostic `0.25` | 940.271 ms | 944.074 ms | 8 | Mesa llvmpipe |

Phase 40 also records that the Earth visual matrix stopped at `7/20` captures.
Those smoke values may compare profiles on the same software host, but they do
not establish hardware-GPU performance, FPS, driver VRAM residency, or a visual
quality-preserving preset.

## Visual gate and decision

The available pad smoke is insufficient to approve sky quality: it does not
cover the Earth day/terminator/night altitude matrix, eclipse, stars, exposure,
red/blue twilight separation, clipping, `neonGreenFrac`, or Mars/Venus. The
phase-40 matrix was partial, and the current host could not produce a new
framebuffer capture. A real GPU/timestamp run remains required before any
quality or cadence change can be promoted.

Decision: `KEEP_EXPERIMENTAL` for the existing `sky_quality_low` probe override;
`NO PROMOTION` for product quality or cadence. No runtime Godot script, shader,
project setting, CI file, existing document, or new tool was changed by this
audit.
