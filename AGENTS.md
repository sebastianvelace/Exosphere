# AGENTS.md

Operational guidance for agents working in the Exosphere repo. See `CLAUDE.md` for the
detailed engineering/simulation rules, `README.md` for the product overview, and
`ROADMAP.md` for the live product plan.

## Cursor Cloud specific instructions

Exosphere is a single desktop product: a Godot 4.6.3 (mono) + .NET 8 / C# space-mission
simulator. There are no servers, databases, or network services — everything runs locally.
Three C# projects: pure-sim library (`ExosphereSimulation/`), Godot game layer
(`Exosphere.csproj`, sources under `scripts/`), and xUnit tests
(`ExosphereSimulation.Tests/`).

### Environment already provided (installed once, persisted in the VM snapshot)

- **.NET 8 SDK** (`dotnet`, apt `dotnet-sdk-8.0`) — builds/tests all three projects.
- **Godot 4.6.3 mono (linux x86_64)** at
  `~/godot/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64`.
  `GODOT_BIN` is exported to that path in `~/.bashrc`. The repo scripts default to a
  hardcoded `/home/sebasvelace/...` path that does NOT exist here, so they rely on
  `GODOT_BIN`; if a script reports "Godot not found", re-export `GODOT_BIN` (it lives in
  `~/.bashrc`, which non-interactive shells may not source).
- **Xvfb** — required for real-framebuffer viewport capture (Godot `--headless` uses a
  dummy renderer that cannot save PNGs). Rendering here is CPU/llvmpipe (no GPU); it works
  but is slow, so full launch→orbit→EDL playtests take several minutes.

The update script only runs `dotnet restore`; the SDK/Godot/Xvfb are pre-installed.

### Build / test / run

Standard commands are in `README.md` and `CLAUDE.md` (build both csproj, run xUnit, or
`bash tools/ci_check.sh`). All 324 xUnit tests pass. Expected standard: 0 warnings, 0 errors.

- Headless smoke (boot a scene + quit): see `README.md` "Godot smoke test". Use
  `"$GODOT_BIN"` in place of the hardcoded path.
- First Godot invocation after a clean checkout must import assets once:
  `"$GODOT_BIN" --headless --path . --import` (a few seconds). The `.godot/` import cache
  is gitignored, so this recurs after `git clean`.
- Visual / gameplay validation: `bash tools/visual_playtest.sh [--smoke|--launch|--ship|--cockpit|--edl]`
  (default = full pad→orbit→EDL). It builds, spins a temporary autoload harness under
  `xvfb-run`, writes PNG milestones to `/tmp/exo_play/` + telemetry to `/tmp/exo_play.log`,
  and always cleans up the harness + restores `project.godot` on exit. Never commit the
  harness (`scripts/_*Shot.cs`, capture autoloads in `project.godot`); a CI guard and
  `.claude/hooks/build-check.sh` enforce this.
- Concurrent agents must isolate artifacts with a caller-stable id, for example
  `bash tools/visual_playtest.sh --flight7 --run-id agent-vp1 --skip-build`. This writes
  `/tmp/exo_play-agent-vp1/`, `/tmp/exo_play-agent-vp1.log`, and a compact
  `run-summary.txt`. On failure the tool prints the last state-gated telemetry, any
  GAP/FALLBACK/failure evidence, and the captures that survived; inspect the summary before
  rerunning a several-minute full mission. Full mode has a 3600 s wall budget; other modes
  default to 1800 s. Override with `--max-runtime SEC` only when telemetry shows continued
  physical progress, not to conceal a stalled state machine. Do not wrap the command in
  another `xvfb-run`: the harness owns its display lifecycle. If a run completed but its
  gate or the agent session was interrupted, re-evaluate preserved evidence with the same
  mode/id plus `--verify-only`; this never builds, launches Godot, or deletes captures.

### Gotchas

- The `uid://...` "invalid UID" warnings on scene load are benign (Godot falls back to text
  paths); the missing-`0` ALSA errors are just the audio dummy driver under Xvfb.
- The main scene is `scenes/ui/MainMenu.tscn` (per `project.godot`); the playable flight and
  VAB scenes are `scenes/flight/Flight.tscn` and `scenes/construction/Construction.tscn`.
