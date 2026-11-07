# F4 checkpoint handoff — superseded

The WIP checkpoint `d130a70` was continued by `e2e1c27` / `ea94c64` and the current
`codex/f5-slosh-foundation` branch. Its old implementation checklist is historical.
The maintained continuation is `HANDOFF_F4_F5.md`; authoritative phase status is
`docs/physics/fidelity_gates.md`. Investigation evidence is retained in
`docs/physics/f4_completion_worklog.md`.

The resource/transport adapter is implemented. This continuation repaired momentum/velocity, fragmentation
and finite-startup regressions; complete CI now passes 1504/1504 tests without skips,
with builds/contracts/Godot green. The F5 slosh foundation is established. Do not infer F4 closure from an old targeted test count. F5 production
coupling and persistence remain pending. Preserve unrelated `.atl/` edits and require
complete CI before ordinary commits. No merge to main is authorized by this handoff.
