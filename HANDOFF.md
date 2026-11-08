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

Lo último de F6 (2026-11-08): masas y giraciones secas del CM/SM desde SP-238 tabla B-I;
asignación de RCS respecto al centro de masa del conjunto acoplado (antes los mandos de
cabeceo/guiñado giraban al revés el CSM+LM y un giro grande divergía); `3a5657f` (ventana de encendido del SPS con la masa del
conjunto acoplado) y `637e6d5` (carga SPS medida, SP-238 tabla 8-X). Antes: interfaces
del spacecraft AS-506, corte del S-IC por agotamiento de un constituyente, contorno
aerodinámico exterior con solapes e identidad del alcohol del Redstone.

## Siguiente trabajo de F6, en orden

1. Hecho: centro y giración secos del CM y del SM derivados de SP-238 tabla B-I
   (`ApolloCsmMassPropertiesHistoricalTests`). Falta: geometría de tanques y líneas del SPS
   (hoy un almacén uniforme en el centro del SM), residuos no expulsables y consumo de
   criogénicos (el remanente lógico del SM incluye 669,1 lb de criogénicos de lanzamiento).
2. Plano de salida de la tobera del SPS (la fuente de 1966 es de otra configuración; hoy hay
   un punto de salida explícito sin fuente).
3. Objetivos lunares propios de Apollo 11 (el selector de LOI aún usa el proxy de Apollo 8).
   En curso, sin integrar: `docs/physics/drafts/apollo11_lunar_targets_wip.patch` (aplicar
   con `git apply`; incluye también la corrección de RCS acoplado ya commiteada, así que
   aplicar solo los archivos de Apollo 11 y `ApsidesTargetBurn.cs`). Contiene las constantes
   de SP-238 (LOI 889,2 m/s a 60,0 × 169,7 mi náuticas; circularización 48,6 m/s a un objetivo no
   circular de 53,7 × 65,7 mi náuticas, 15 706,4 s después de la LOI) y `ApsidesTargetBurn`, un
   quemado que apunta a las dos ápsides. Resultado: encendiendo en retrógrado alcanza
   53,78 × 62,47 mi náuticas en 16,54 s (volado: 53,8 × 65,7 en 16,7 s), porque el conjunto no
   termina de girar hacia la componente radial. Con pre-alineamiento sobre la dirección inercial
   prevista, el conjunto se queda a ~13° con el guiñado oscilando (±0,45) y gastando RCS: hay
   que investigar esa oscilación de `AttitudeGuidance.ComputeAxisPointingCommand` en el conjunto
   acoplado antes de integrar el quemado. El controlador de Godot (`RunApolloApsidesBurn`) y los
   tests están en el parche.
4. Reconstrucción nativa del sustainer y del PU del Atlas para MA-6
   (`docs/physics/atlas_ma6_reconstruction.md`); hoy es un sustainer efectivo declarado como
   brecha de calibración.
5. Aletas del S-IC, grid fins y modelo newtoniano para cápsulas romas.

Residuos y fuentes por vehículo: `docs/physics/historical_recalibration.md`.
