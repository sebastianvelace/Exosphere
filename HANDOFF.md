# Handoff vigente — fidelidad física (F6)

Este archivo reemplaza a `HANDOFF_F4_engine_exhaustion.md`, `HANDOFF_F4_completion.md` y
`HANDOFF_F4_F5.md`; su historia queda en Git. El estado autoritativo de cada fase está en
`docs/physics/fidelity_gates.md`.

## Rama y reglas

- Rama de trabajo: `codex/f6-historical-recalibration`. No hacer merge a `main` sin autorización.
- Autor y committer: `Sebas <sebastianvelace@gmail.com>`, sin trailers de atribución.
- No tocar ni hacer stage de `.atl/`. No commitear `_Diag*`, harnesses, `bin/`, `obj/`,
  `.godot/` ni `*.uid`.
- Antes de cada commit verificado: `bash tools/ci_check.sh` completo (0 warnings, 0 errores).
  Si cambian perfiles históricos, correr la auditoría de perfiles sobre el TRX de esa misma CI
  (`tools/physics_profile_audit.py --verify-only`).
- Nunca aflojar tolerancias históricas. Todo dato no publicado necesita su registro en
  `data/provenance/*.json`; regenerar `docs/physics/estimates_register.md` con
  `python3 tools/provenance_register.py`.

## Estado

| Fase | Estado | Documento |
| --- | --- | --- |
| F0–F3 | cerradas | `rigid_body_fidelity_plan.md` |
| F4 transporte de masa variable | cerrada 2026-11-06 | `variable_mass_torques.md` |
| F5 slosh por tanque | cerrada 2026-11-07 | `propellant_slosh.md` |
| F6 recalibración histórica | abierta | `historical_recalibration.md` |

Lo último de F6 (2026-11-08): `3a5657f` (ventana de encendido del SPS con la masa del
conjunto acoplado) y `637e6d5` (carga SPS medida, SP-238 tabla 8-X). Antes: interfaces
del spacecraft AS-506, corte del S-IC por agotamiento de un constituyente, contorno
aerodinámico exterior con solapes e identidad del alcohol del Redstone.

## Siguiente trabajo de F6, en orden

1. Masa seca nativa del SM: centro de masa e inercia; geometría de líneas del SPS y residuos
   no expulsables. Hoy el SM tiene un remanente lógico de 3825,50 kg que conserva la masa
   húmeda; no es masa estructural medida.
2. Plano de salida de la tobera del SPS (la fuente de 1966 es de otra configuración; hoy hay
   un punto de salida explícito sin fuente).
3. Objetivos lunares propios de Apollo 11 (el selector de LOI aún usa el proxy de Apollo 8).
4. Reconstrucción nativa del sustainer y del PU del Atlas para MA-6
   (`docs/physics/atlas_ma6_reconstruction.md`); hoy es un sustainer efectivo declarado como
   brecha de calibración.
5. Aletas del S-IC, grid fins y modelo newtoniano para cápsulas romas.

Residuos y fuentes por vehículo: `docs/physics/historical_recalibration.md`.
