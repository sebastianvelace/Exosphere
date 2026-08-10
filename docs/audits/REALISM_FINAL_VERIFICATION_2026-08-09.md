# Verificación final de integración — 2026-08-09

Esta auditoría registra el estado que queda publicado en `codex/realism-program` después de
la última iteración EDL. Los comandos se ejecutaron con el árbol limpio y sin el autoload de
captura temporal.

## Código y física CPU

| Comprobación | Resultado |
| --- | --- |
| `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore` | **549/549**, 0 fallos, 0 omitidos |
| `dotnet test ... --filter FullyQualifiedName~LandingContactIntegrationTests` | **8/8** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `git diff --check` | limpio |

La suite incluye invariantes orbitales, densidad/transmitancia, aerosoles, motores, staging,
VAB/payload y contacto EDL. El sesgo descendente de un motor (`ac4a08e`) está acotado a
`FINAL` y cubierto por una aserción de contrato; no se considera evidencia de aterrizaje por sí
solo.

## Contrato y render Godot

`bash tools/tests/visual_playtest_contract_test.sh` pasó **2 fixtures válidos y 12 inválidos**:
rechaza órbita suborbital, estado no finito, teletransporte, falta de inserción, stall,
vehículo destruido, conteo de motores incorrecto, corrupción concurrente y duplicación de
fronteras.

El smoke framebuffer se ejecutó como `final-smoke-20260809c`:

```text
status=PASS
mode=smoke
milestones=pad
SUMMARY reason=SMOKE_OK frames=50
artifact=/tmp/exo_final_smoke3/exo_play_pad.png (185 KiB)
```

La matriz atmosférica `aerosol-v2` permanece aprobada con `ATMOSPHERE_OK`, 16/16 hitos,
1.157 frames y `skyWhiteClipFrac=0.08642`. El ascenso Flight 7 tiene `ASCENT_ORBIT_OK` y
telemetría de etapas `33→39→6`.

## Gate EDL explícitamente abierto

Las corridas `edl-v6`, `edl-final-axis-v7`, `edl-lateral-cap-v8` y `edl-one-engine-v9` están
documentadas en `REALISM_BASELINE_2026-08-09.md`: reducen fallos sucesivos, pero no producen
`TOUCHDOWN`, contacto multipunto estable, carga de patas segura y `exo_play_touchdown.png`.
El siguiente agente debe repetir el E2E con un `run-id` nuevo después de `ac4a08e`, conservar el
log y cerrar el gate únicamente si el runner emite `SUMMARY reason=TOUCHDOWN_OK` sin overload,
velocidad lateral excesiva ni rebote sostenido.

## Estado reproducible

- Rama: `codex/realism-program`.
- Artefactos de runner y `project.godot` temporal: eliminados/no versionados.
- WIP previo del usuario: preservado en `stash@{0}` (`user-wip-before-realism-program`).
- Plan de diez horas y división de tramos: `docs/REALISM_10H_EXECUTION_PLAN.md`.
