# Fase 37 — telemetría del scheduler en el playtest real

Fecha: 2026-08-15  
Área: `SimulationBridge`, `tools/visual_playtest.sh` y contratos de rendimiento

## Objetivo

La fase 36 podía demostrar el coste del scheduler en la biblioteca CPU, pero todavía no
relacionaba ese coste con el callback de frame que arranca el nivel Godot. Esta fase añade
esa correlación al harness de framebuffer sin cambiar la física ni el límite oficial de
warp.

## Cambios

- El harness escribe una línea `PERF_FRAME` por frame con:
  - `frame_ms`: wall-clock del callback completo del harness.
  - `scheduler_ms`: wall-clock medido dentro de `Universe.Tick`.
  - `scheduler_branch`: `FullPhysics`, `Mixed`, `Rails` o `None`.
  - `scheduler_substeps`: substeps externos procesados en el tick.
  - `scheduler_cap`: cap efectivo de integración.
  - `scheduler_simulated`: segundos simulados en el tick.
  - `catch_up_risk`: la marca de fase 36 para `>=128` substeps.
- `SimulationBridge` usa `Universe.GetWarpPhysicsRequirements` para obtener la sensibilidad
  a fuerzas y la entrada atmosférica acotada en una sola consulta. No se cachea la decisión:
  posición, presión, heating, throttle y motores pueden cambiar entre frames.
- `RequiresBoundedWarpPropagation(Vessel)` conserva su API pública y delega al mismo helper;
  por tanto, los callers existentes no cambian de semántica.
- El contrato de aceptación exige que el harness contenga los campos y, cuando se le entrega
  un log real mediante `PERF_ACCEPTANCE_LOG`, rechaza un `PERF_FRAME` sin esos valores finitos.

## Evidencia CPU

```text
dotnet test ... --filter WarpPhysicsParityTests|PhysicsSchedulerPerformanceTests
25/25 PASS
dotnet test (suite completa): 606/606 PASS
dotnet build Exosphere.csproj --no-restore: 0 warnings, 0 errors
performance_acceptance_contract_test (sin log): 32 PASS, 1 dynamic skip
bash -n tools/visual_playtest.sh: PASS
```

La regresión `CombinedWarpRequirementsMatchIndividualQueries` demuestra que la API agrupada
produce exactamente los mismos dos booleanos que las consultas públicas individuales.

## Captura real de arranque

El playtest `--ascent --flight7` se ejecutó con framebuffer Xvfb/OpenGL llvmpipe y se detuvo
de forma controlada después de alcanzar `ASCENT_SH`, para no convertir una máquina de
validación de ~1 FPS en un falso gate orbital. El log conserva `234/234` líneas `PERF_FRAME`
válidas:

| Señal | Resultado |
|---|---:|
| `simulation_loaded` | 1,971.2 ms |
| LUT Earth en worker | 8,082.6 ms CPU; `worker=true` |
| `frame_ms` mínimo / mediana / máximo | 710 / 1,011 / 2,873 ms |
| `scheduler_ms` mínimo / mediana / máximo | 0.636 / 4.824 / 19.534 ms |
| `catch_up_risk=true` | 0 frames |
| estado al último trace | `t=37.5`, altitud 2.487 km, `33/33` motores, `failedEngines=0`, finito |

El contrato dinámico pasó `40 PASS, 0 FAIL` con el log base y también con el `.console`
compañero, usando un presupuesto de framebuffer de 4,000 ms específico para este host
llvmpipe. El presupuesto oficial de 50 ms no se presenta como cumplido: el host de
validación es demasiado lento para usarlo como objetivo de FPS. La relación entre las
señales sí es concluyente para esta captura: el scheduler consume milisegundos bajos,
mientras el frame consume aproximadamente un segundo; el atasco observado no está en
`Universe.Tick` ni en catch-up físico durante el arranque. El gate `ASCENT_ORBIT_OK` queda
pendiente de una captura completa en hardware/renderer representativo.

## Interpretación y límites

Esta instrumentación hace observable el lugar donde aparece un freeze, pero no resuelve aún
el catch-up ilimitado. No se descarta tiempo, no se capan substeps y no se activa hibernación
física. Cualquier política de presupuesto debe probar equivalencia para contacto, atmósfera,
periapsis, SOI, docking, staging, cambio de vessel activo y wake-up de rails.

`scheduler_ms` no es FPS: es el coste de `Universe.Tick`. `frame_ms` incluye el resto del
callback del harness y puede incluir trabajo de presentación o consulta de estado. La
validación de hardware/GPU continúa separada del benchmark CPU.

## Decisión y siguiente paso

Promover la telemetría al playtest y conservar la API combinada. La captura ya separa el
coste de física del coste del frame; la siguiente fase debe perfilar render/GPU, consultas de
presentación y trabajo de UI/LUT durante el primer minuto. Sólo después de esa captura se elegirá
entre presupuesto de catch-up, reducción de cadencia de sistemas no críticos o hibernación
con wake-up explícito.
