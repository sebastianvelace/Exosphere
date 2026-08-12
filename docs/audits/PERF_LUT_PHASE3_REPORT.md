# Phase 3 — Atmospheric LUT worker lifecycle audit

## Scope

This phase changes only the lifecycle around the CPU atmospheric LUT worker in
`scripts/SkyController.cs`. The renderer remains on the official RGB order-four LUT. No
LUT integration was moved back to the main thread and no shader order was changed.

## Finding: why `worker_complete` was absent

The previous implementation emitted `stage=queued` immediately after `Task.Run`, but it
did not expose a running phase, duration, allocation estimate, cancellation state, or the
body/profile generation associated with the task. The worker performed three expensive CPU
operations in sequence (transmittance, global multiple scattering, and angular atlas
packing). Cancellation was not requested when Flight exited or when the requested body
changed, so a stale task could continue consuming a worker thread while the main thread had
no evidence distinguishing “still integrating” from “stuck”.

The 300/6000-frame observations therefore proved only that the queue was asynchronous; they
did not prove completion. The new telemetry identifies the exact phase and reports periodic
elapsed time while the task is alive. The integration methods remain CPU-side and are still
called from the worker.

## Lifecycle changes

- Every request has a monotonically increasing generation and an immutable cache key.
- The key includes body/profile geometry, atmospheric layers and optical coefficients, the
  official LUT version, and whether experimental order five was requested. Textures are no
  longer keyed only by `bodyId`.
- Worker states are `queued`, `running`, `cancel_requested`, `completed`, `canceled`, and
  `faulted`; phases are transmittance, global order four, optional order five, and angular
  atlas.
- Telemetry records queue/running progress, elapsed milliseconds, produced bytes and the
  estimated peak bytes. Completion additionally records CPU, queue and upload costs.
- A profile change requests cancellation of the stale task. `_ExitTree` requests
  cancellation on Flight teardown and disposes the token source asynchronously; the main
  thread never waits for the worker.
- Cancellation is cooperative between the existing LUT builders. The current builders do
  not accept a token inside their inner numerical loops, so cancellation may complete the
  current LUT stage before the result is discarded. It can never upload a stale result after
  cancellation.
- Completed CPU results are retained behind a bounded three-entry cache. GPU textures and
  CPU tables use the same key and are evicted together, preventing profile churn from
  creating an unbounded cache.

## Byte accounting

The worker reports double-precision vector storage for the transmittance, global seed,
angular atlas and optional order-five diagnostic pass. It also reports the RGBA32F texture
upload size. These are allocation estimates, not a replacement for process RSS or driver
VRAM telemetry; the latter remains a separate validation gate.

## Verification

- `dotnet build Exosphere.csproj --no-restore`: passed with 0 warnings and 0 errors.
- Directed atmosphere tests through VSTest: **89/89 passed** before the final
  profile-only change; the complete suite is rerun as the release gate.
- `git diff --check`: passed.
- Framebuffer-backed validation is available through Xvfb/llvmpipe. Pad smoke,
  cockpit smoke and focused Flight 7 ascent completed; the latter reached
  `ASCENT_ORBIT_OK` with a 158×145 km orbit and `e=0.001`.

## Interactive profile decision

The renderer remains on RGB order 4. The runtime profile is explicitly named
`rgb-ms-order4-interactive-v21` and uses smaller renderer-quality tables while
the CPU spectral/reference oracle remains independent. This is a responsiveness
mitigation, not a change to physics coefficients or official scattering order.
The old high-resolution profile could keep the worker busy for approximately
133 s in the same ascent harness; v21 completed the Earth worker in approximately
8–9 s on llvmpipe and allowed the flight to reach orbit.

The profile is accepted for the next phase with a hardware gate still open:
the VM reports callback p50/p95/p99 of 982/1219/2659 ms, so it cannot serve as
evidence of a 60 FPS target. GPU frame time and VRAM remain unmeasured.

## Decision

Keep the official renderer at RGB order four. The worker remains asynchronous, now has
observable lifecycle and memory state, cancels stale work at scene teardown/profile changes,
and reuses completed profile-specific results safely. A future phase may add cancellation
checkpoints inside the numerical LUT builders if telemetry shows that a single integration
stage remains too long to abandon promptly.
