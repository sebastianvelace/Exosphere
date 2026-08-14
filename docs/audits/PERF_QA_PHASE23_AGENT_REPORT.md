# Fase 23 P6 — QA de equivalencia y gates visuales

Estado: gates focalizados cerrados; promoción visual de hardware pendiente  
Commit base observado: `3e1e577` (`perf: cache stable engine telemetry snapshots`)  
Fecha: 2026-08-14

Este trabajo quedó limitado a:

- `ExosphereSimulation.Tests/OptimizationPhase23QaTests.cs`
- `tools/tests/optimization_phase23_contract_test.sh`
- este informe

No se modificaron runtime de producción, `project.godot`, `PhysicsSchedulerPerformanceTests.cs`
ni el autoload temporal del playtest. Había cambios concurrentes de otros agentes en el
worktree; se conservaron fuera de este alcance.

## Resultado de gates

| Gate | Estado | Evidencia reproducible |
|---|---|---|
| Ascenso Flight 7 | **PASS funcional** | `visual_playtest.sh --ascent --verify-only`: `ASCENT_ORBIT_OK`, 48 muestras, `Coast→Insert`, `pe=149918.0 m`, `atmoTop=140000.0 m`, `failedEngines=0`. |
| EDL / catch | **PASS funcional** | Revalidación de `/tmp/exo_phase22_edl_catch`: `entry`, `peak_heating`, `retro_burn`, `flip_complete`, `CHECK tower_catch caught=True pins=2 relativeSpeed=0.030 angularSpeed=0.0000`, `SUMMARY reason=CAUGHT`. |
| Salto J / Saturn | **PASS funcional** | `JumpToBody` conserva `CancelGuidanceForTeleport` y `PrepareForTeleport`; `Key.J` exige target visible. Artefacto revalidado con `SUMMARY reason=SATURN_OK`, captura de Saturno de 594009 bytes. |
| Atmosphere matriz completa | **BLOCKED / INCOMPLETE** | El artefacto disponible sólo llegó hasta `120km_day` (8 casos); `--verify-only` rechaza correctamente la ausencia de `400km_day`. No se declara PASS. |
| Spectral CPU | **PASS físico / promoción condicionada** | Earth, Mars y Venus: bandas finitas, energía monotónica y RGB finito. Earth/Mars tienen `order4NoWorse=True`; Venus tiene `order4NoWorse=False`. Orden 4 sigue oficial y orden 5 diagnóstico; no se promueve orden 5. |
| GPU física / texturas | **BLOCKED** | `matrix.meta`: `status=BLOCKED`, `physical_gpu_gate=BLOCKED`, `software_renderer_observed=true`; sólo `8k_nomip` alcanzó probe antes del cierre fail-closed. |

`PASS funcional` significa que el contrato físico/telemetría y los artefactos se pueden
revalidar en este host. No significa 60 FPS, VRAM ni calidad de imagen en una GPU objetivo:
el backend observado es software (`llvmpipe`).

La ejecución de una captura EDL/Saturn nueva no se forzó porque otro proceso tenía el lock
global de `visual_playtest.sh`. Se usaron artefactos existentes y `--verify-only`, que no
lanza Godot ni adquiere el lock. Esto evita mezclar sesiones o atribuir un PASS a una corrida
no aislada.

## Pruebas CPU focalizadas

Comando:

```text
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --filter 'FullyQualifiedName~OptimizationPhase23QaTests|FullyQualifiedName~TeleportRegressionTests|FullyQualifiedName~CatchContactTests|FullyQualifiedName~SpectralAtmosphereOracleTests' \
  --nologo
```

Resultado: **22/22 PASS**, 0 fallos, 0 omitidas.

Los gates nuevos cubren:

- energía espectral de órdenes 2–5 finita, no negativa y convergente;
- conversión espectral a RGB finita para Earth, Mars y Venus;
- reset de rails, órbita, throttle, momento angular y estado de catch antes de continuar
  después de un salto;
- captura nominal con dos pines y rechazo de una aproximación lateral a 50 m;
- presencia de todos los límites declarados para ascenso, EDL, Saturn/J, atmosphere,
  spectral y GPU fail-closed.

## Contrato P6 y estados inválidos

Comando:

```text
bash tools/tests/optimization_phase23_contract_test.sh
```

Resultado: **25 checks estáticos/positivos PASS**, `fail=0`; además rechazó explícitamente
estos 7 fixtures inválidos:

1. ascenso sin transición `Insert`;
2. catch con un solo pin;
3. Saturno con señal de imagen insuficiente;
4. atmosphere con `actualAlt` distinto del request;
5. spectral no finito;
6. salto J/Saturn sin cancelar guidance;
7. matriz GPU que afirma `PASS` con renderer software.

Contratos existentes reproducidos en la misma sesión:

- `visual_playtest_contract_test.sh`: 1 fixture válido y 11 inválidos;
- `gameplay_regression_contract_test.sh`: PASS;
- `phase4_gpu_probe_contract_test.sh`: 1 válido y 6 inválidos;
- `texture_gpu_matrix_contract_test.sh`: 1 válido y 3 inválidos;
- `render_cadence_phase23_contract_test.sh`: PASS.

El contrato P6 se mantiene como comando independiente porque el alcance permitido no
incluye `tools/ci_check.sh`. Integrarlo al CI es el siguiente paso del coordinador, sin
alterar los archivos de otros agentes.

## Decisiones

- El HUD/telemetría de motores no presenta evidencia de una falla física en el gate de
  ascenso: los artefactos observan motores activos y `failedEngines=0`; el rojo del HUD debe
  seguir interpretándose como estado de fallo, no como color normal de encendido.
- El reset de salto J/Saturn queda protegido por prueba CPU y contrato estático; no se
  reabre runtime sin una reproducción nueva del giro.
- Los chopsticks tienen evidencia de captura física real (`pins=2`) y captura visual
  `caught`; el gate no acepta sólo una etiqueta de fase.
- La matriz atmosphere completa queda abierta hasta disponer de una corrida que alcance
  todos los casos requeridos y, para promoción visual de recursos, una GPU física.
- Venus impide declarar una equivalencia global de orden 4 frente a orden 3. Esto es un
  resultado de validación, no un motivo para conectar orden 5 al runtime.

## Higiene del worktree

Los únicos archivos nuevos de esta fase son los tres listados al inicio. Los cambios
existentes de otros agentes permanecen sin staging; no se hizo `reset`, `checkout` ni
limpieza destructiva.
