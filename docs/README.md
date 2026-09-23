# Exosphere documentation

This directory contains the technical documentation for Exosphere. The repository has a
large evidence trail from earlier audits and optimization waves; those reports remain useful
as provenance, but they are not the current product plan.

## Start here

| Need | Read |
|---|---|
| Product overview, build, controls, architecture | [`../README.md`](../README.md) |
| Current priorities and delivery order | [`../ROADMAP.md`](../ROADMAP.md) |
| Physics equations, frames, assumptions and limits | [`physics/PHYSICS_MODEL.md`](physics/PHYSICS_MODEL.md) |
| Coupled 6-DoF migration and parity gates | [`physics/coupled_6dof_migration.md`](physics/coupled_6dof_migration.md) |
| Starship/Super Heavy data baseline | [`starship_physics_baseline.md`](starship_physics_baseline.md) |
| Visual and gameplay capture workflow | [`../PLAN_PLAYTEST.md`](../PLAN_PLAYTEST.md), [`../PLAN_VISUAL_REALISM.md`](../PLAN_VISUAL_REALISM.md) |
| Documentation status and archive policy | [`DOCUMENTATION_AUDIT.md`](DOCUMENTATION_AUDIT.md) |

## Information hierarchy

1. Code, tests and data are authoritative for what the simulator currently does.
2. `README.md`, `ROADMAP.md` and `physics/PHYSICS_MODEL.md` explain the current system.
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
