# Exosphere documentation

This directory contains the technical documentation for Exosphere. The repository has a
large evidence trail from earlier audits and optimization waves; those reports remain useful
as provenance, but they are not the current product plan.

## Start here

| Need | Read |
|---|---|
| Play from source, controls, what the sim includes | [`../README.md`](../README.md) |
| Build, architecture, data, and current limits | [`DEVELOPMENT.md`](DEVELOPMENT.md) |
| Current priorities and delivery order | [`../ROADMAP.md`](../ROADMAP.md) |
| Physics equations, frames, assumptions and limits | [`physics/PHYSICS_MODEL.md`](physics/PHYSICS_MODEL.md) |
| Coupled 6-DoF migration and parity gates | [`physics/coupled_6dof_migration.md`](physics/coupled_6dof_migration.md) |
| Fidelity phases F0–F6: status and analytic/historical gates (source of truth) | [`physics/fidelity_gates.md`](physics/fidelity_gates.md) |
| Fidelity plan and per-phase findings | [`physics/rigid_body_fidelity_plan.md`](physics/rigid_body_fidelity_plan.md) |
| Variable-mass transport (F4) and propellant slosh (F5) | [`physics/variable_mass_torques.md`](physics/variable_mass_torques.md), [`physics/propellant_slosh.md`](physics/propellant_slosh.md) |
| Historical recalibration (F6, open) and source worksheets | [`physics/historical_recalibration.md`](physics/historical_recalibration.md), [`physics/as506_spacecraft_reconstruction.md`](physics/as506_spacecraft_reconstruction.md), [`physics/atlas_ma6_reconstruction.md`](physics/atlas_ma6_reconstruction.md) |
| Values that are not published data (generated) | [`physics/estimates_register.md`](physics/estimates_register.md) |
| Realism status and what is missing, by priority (2026-11-08) | [`audits/REALISM_STATUS_2026-11-08.md`](audits/REALISM_STATUS_2026-11-08.md) |
| Starship/Super Heavy data baseline | [`starship_physics_baseline.md`](starship_physics_baseline.md) |
| Visual and gameplay capture workflow | [`../PLAN_PLAYTEST.md`](../PLAN_PLAYTEST.md), [`../PLAN_VISUAL_REALISM.md`](../PLAN_VISUAL_REALISM.md) |
| Flight 14 visual references and reconstruction limits | [`research/STARSHIP_FLIGHT14_VIDEO_FEASIBILITY_2026-09-30.md`](research/STARSHIP_FLIGHT14_VIDEO_FEASIBILITY_2026-09-30.md) |
| Flight 14 orbital components, comparison evidence and remaining mission gates | [`audits/flight14_orbital_components_2026-10-01.md`](audits/flight14_orbital_components_2026-10-01.md) |
| Flight 14 loaded ascent, 26 payload splits and conservation evidence | [`audits/flight14_payload_deployment_2026-10-01.md`](audits/flight14_payload_deployment_2026-10-01.md) |
| Flight 14 continuous launch diagnostic and reconstruction assumptions | [`audits/flight14_continuous_launch_2026-10-01.md`](audits/flight14_continuous_launch_2026-10-01.md) |
| Independent Super Heavy coast comparison and retired-state HUD correction | [`audits/flight14_booster_coast_calibration_2026-10-04.md`](audits/flight14_booster_coast_calibration_2026-10-04.md) |
| Documentation status and archive policy | [`DOCUMENTATION_AUDIT.md`](DOCUMENTATION_AUDIT.md) |

## Information hierarchy

1. Code, tests and data are authoritative for what the simulator currently does.
2. `README.md` introduces the project. `docs/DEVELOPMENT.md`, `ROADMAP.md` and `physics/PHYSICS_MODEL.md` explain the current system.
3. `PLAN_*` files are living execution plans and must be updated when a gate closes.
4. `docs/audits/`, `docs/HITO*` and `.atl/` preserve dated evidence and decisions. Their
   statuses are historical unless a current document links to them as active evidence.
5. `.agents/`, `.claude/` and `CLAUDE.md`/`AGENTS.md` are operational instructions, not product
   documentation; mirrored skill files must remain synchronized.

## Writing standard

Every active technical document should make five things explicit:

- scope and date of the statement;
- implemented behavior versus approximation;
- evidence (test, telemetry, framebuffer, or data source);
- remaining uncertainty and its impact;
- the owner file or next gate.

Avoid copying a complete status table into multiple files. Link to the source of truth instead.
