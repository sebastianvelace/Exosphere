# Documentation audit

> **Reviewed:** 2026-09-22
>
> This audit reconciles the repository's Markdown inventory with the current code, tests and
> roadmap. Historical evidence is retained, but active documentation now has an explicit home.

## Inventory

The pre-audit inventory contained 189 tracked Markdown files. This audit adds two navigation/
status documents and one current physics model, bringing the maintained inventory to 192:

| Area | Count | Treatment |
|---|---:|---|
| Root product/agent docs | 8 | Keep; refresh current pointers and test counts |
| `.agents/skills/` | 5 | Keep; operational skill source |
| `.claude/` skills/agents | 6 | Keep; operational mirror/configuration |
| `.atl/` | 10 | Keep as append-only delegation and evidence history |
| `docs/` milestone/workflow docs | 20 | Keep; historical mission scope or active workflow |
| `docs/audits/` | 139 | Keep as dated evidence; not the current roadmap |
| `docs/physics/` | 4 | Keep and extend as the physics source of truth |

## Active source of truth

- [`README.md`](../README.md) — product, setup and architecture entry point.
- [`ROADMAP.md`](../ROADMAP.md) — current delivery order.
- [`docs/physics/PHYSICS_MODEL.md`](physics/PHYSICS_MODEL.md) — equations, frames,
  approximations and physics gates.
- [`docs/physics/coupled_6dof_migration.md`](physics/coupled_6dof_migration.md) — 6-DoF
  migration history and parity status.
- `PLAN_PLAYTEST.md` and `PLAN_VISUAL_REALISM.md` — active visual validation plans.
- `CLAUDE.md` and `AGENTS.md` — contributor/agent operating rules.

## Historical material

The dated audit and milestone files are not deleted merely because their status is old. They
contain the evidence, assumptions and rejected hypotheses that explain why the current model
looks the way it does. They are now explicitly subordinate to the active documents above.

In particular:

- July/August physics audits are historical findings and must not be read as the current open
  backlog without checking the code and `ROADMAP.md`.
- `docs/audits/PERF_*` reports are benchmark snapshots, not universal performance guarantees.
- `.atl/*.md` files are collaboration logs, not user-facing product documentation.
- Apollo, Gemini, Mercury, Falcon and New Glenn milestone files describe bounded historical
  slices and remain relevant to those mission variants.

## Replaced or consolidated content

`docs/physics_audit.md` is retained as a compatibility pointer to the current physics model
and historical audit index rather than continuing to present its July parameter table as live
status. The detailed historical record remains available in Git history and the dated audit
documents.

No other Markdown file was removed: the remaining files either define an operational contract,
describe a still-supported mission/workflow, or preserve dated evidence that is useful for
regression analysis.

## Maintenance rule

When a physics or visual gate closes, update the active source-of-truth document in the same
work unit. Add dated evidence under `docs/audits/` only when it contains a reproducible test,
telemetry trace, framebuffer comparison or decision record. Do not create another competing
roadmap or copy a complete status table into an audit report.
