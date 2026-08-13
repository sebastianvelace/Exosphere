# Fase 8 — scheduler físico y telemetría de motores

Estado: implementado, validado y listo para integración
Fecha: 2026-08-12
Alcance: `Universe` CPU, scheduler full/rails/mixed, HUD de motores, Starship Flight 7

## Objetivo

Eliminar asignaciones repetidas del camino físico y del HUD sin reducir la fidelidad de la
nave activa. La separación estructural debe seguir siendo segura: los restos creados durante
un substep no se integran en el mismo recorrido, igual que con el snapshot anterior, sino en
el siguiente tick del scheduler.

## Cambios

- `Universe.GetDominantBody` usa un máximo manual en el fallback sin SOI; se elimina una
  ordenación LINQ en una consulta compartida por consumidores de física/render.
- `FlushDeferredRailsToCurrentTime`, `TickPhysics` y `TickPhysicsMixed` recorren `_vessels`
  por índice y capturan el conteo inicial. Esto mantiene la semántica de snapshot frente a
  `AddVessel` por breakup y elimina una lista temporal por substep.
- `EngineGridHUD` separa presentación de física: actualiza telemetría a 10 Hz, reutiliza un
  buffer de `EngineReadout` y elimina `ToList`, `Sum` y `Select` de `_Process`.
- Se añadió `tools/tests/physics_hotpath_contract_test.sh` y se incorporó al gate CI para
  impedir la reintroducción accidental de snapshots LINQ del scheduler o materialización de
  listas en el HUD.

No se cambió el timestep, la propagación, el modelo de thrust, los contactos, el staging ni
la asignación de LOD físico. La nave activa conserva física completa.

## Benchmark del scheduler

Comando posterior:

```bash
SAMPLES=100 WARMUP=20 OUT_DIR=/tmp/exo_next_scheduler_post \
  bash tools/perf/scheduler_phase6_benchmark.sh
```

Comparación con la corrida previa equivalente (`/tmp/exo_opt_scheduler`):

| Escenario | p95 previo → posterior | Alloc/tick previo → posterior | Estado |
|---|---:|---:|---|
| `full_single` | 0.0663 → 0.0671 ms | 8575.8 → 8447.4 B | finite |
| `full_fleet` | 0.1463 → 0.2403 ms | 25325.5 → 25149.4 B | finite |
| `rails_fleet` | 0.8192 → 0.9390 ms | 190383.6 → 190071.4 B | finite |
| `mixed_fleet` | 4.2118 → 5.0262 ms | 834343.0 → 829342.8 B | finite |

Las asignaciones bajan de forma consistente, hasta aproximadamente 5 KiB por tick en el
escenario mixto. Los tiempos p95 no se presentan como una mejora: el benchmark se ejecuta en
una máquina compartida y el ruido del entorno domina esta magnitud. La eliminación de
`ToList` sí queda protegida por contrato; una optimización adicional del coste CPU requiere
perfilar `Vessel.Tick` y no debe inferirse de este cambio.

## Validación funcional

- Build `ExosphereSimulation.Tests`: 0 warnings, 0 errors.
- Build `Exosphere`: 0 warnings, 0 errors.
- xUnit: 558 passed, 0 failed, 0 skipped.
- Contrato `physics_hotpath_contract_test.sh`: PASS.
- Playtest `--ascent --flight7 --run-id next-scheduler-ascent`: PASS.
- Hitos visuales: `pad`, `liftoff`, `maxq`, `hotstage`, `separation`, `orbit`.
- Gate: `ASCENT_ORBIT_OK`, órbita final 150×145 km, `e=0.000`.
- Durante ascenso: Ship 33 con `runningEngines=33` antes de separación y `6` después;
  `failedEngines=0`, `finite=True`, `destroyed=False`, `structuralLost=False`.
- El worker atmosférico permaneció separado del árbol Godot y la simulación avanzó durante
  el precálculo; no apareció un stall físico ni pérdida de progreso.

## Límites y siguiente fase

Este cambio no resuelve todavía el coste dominante de `Vessel.Tick`, aerodinámica, gimbal,
lecturas derivadas de partes ni la memoria de recursos visuales. Tampoco declara 60 FPS: el
playtest de referencia usa llvmpipe y sus tiempos de callback no son FPS de GPU objetivo.

La siguiente fase debe perfilar `Vessel.Tick` y `PartGraph` con escenarios de 33+6 motores,
engine-out asimétrico, hot-stage, gimbal y breakup; sólo después se podrán introducir caches
por tick o buffers de partes con pruebas de paridad determinista.
