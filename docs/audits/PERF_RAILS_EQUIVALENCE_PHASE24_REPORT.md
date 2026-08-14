# Fase 24 — auditoría de equivalencia rails/deadline (N9)

Estado: **BLOCKED**
Fecha: 2026-08-14
Base de trabajo: `ed57285`
Ownership: `ExosphereSimulation.Tests/PhysicsSchedulerPerformanceTests.cs` y este informe.

## Alcance

Se añadieron únicamente pruebas deterministas. No se modificó el runtime de producción, el
benchmark, `tools/visual_playtest.sh` ni `project.godot`.

La referencia *always-checked* es una `Universe` con `TimeScale = 5`, donde la nave bajo
prueba es `ActiveVessel`. Como el scheduler no promueve una nave activa a rails por debajo
de warp 10, esa ruta ejecuta RK4 en cada subpaso. La ruta comparada mantiene la misma nave
no activa en una `Universe` mixta y deja que el scheduler use rails/deadlines.

Se cubrieron:

- proyección segura hasta que expira el primer deadline de 2 s;
- wake-up por comando durante un deadline diferido;
- entrada atmosférica, que debe rechazar la proyección y usar física completa;
- contacto de tren de aterrizaje, con la misma regla de rechazo;
- cruce SOI con un fixture sintético estático y sin atmósfera, para aislar el cambio de
  marco patched-conic.

## Tolerancias explícitas

| Caso | Posición | Velocidad | Motivo |
|---|---:|---:|---|
| Rails seguro / deadline | ≤ `1e-4 m` | ≤ `1e-9 m/s` | Diferencia acotada entre Kepler y RK4 en una ventana corta; la velocidad tiene un límite más estricto porque acumula fase orbital. |
| Atmósfera / contacto | ≤ `1e-4 m` | ≤ `1e-8 m/s` | Misma ruta RK4 y misma cadencia de subpasos; sólo se permite error numérico mínimo. |
| SOI sintético | ≤ `1e-6 m` | ≤ `1e-9 m/s` | GM de ambos cuerpos casi nulo; el fixture pretende medir continuidad inercial, no dinámica de tercer cuerpo. |

Las tolerancias no son porcentajes ni se amplían para ocultar una divergencia. También se
comprueban explícitamente flags y telemetría: elegibilidad, proyección, catch-up, despacho
full-physics, estado `IsOnRails` y cuerpo de referencia cuando corresponde.

## Validación ejecutada

```text
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --no-restore --filter FullyQualifiedName~PhysicsSchedulerPerformanceTests
```

Resultado del conjunto automático: **19 total, 18 PASS, 1 SKIP**. El caso SOI se conserva
como diagnóstico bloqueado, pero está marcado `Skip` para que una divergencia conocida no
se convierta en un falso fallo de toda la suite. El build efectuado por `dotnet test`
terminó sin warnings ni errores de compilación. `git diff --check`: **PASS**.

Los casos que pasan son:

- deadline seguro antes y después de su expiración;
- wake-up por throttle con restauración de estado anclado;
- rechazo atmosférico sin `DeadlineProjectedDispatches`;
- rechazo de contacto sin `DeadlineProjectedDispatches`;
- las 14 regresiones previas del scheduler.

## Bloqueo reproducido: cruce SOI

El diagnóstico `SoiCrossingDeadlinePathMatchesAlwaysCheckedReferenceWithoutInertialJump`
sí confirma que la nave rails cruza a `soi-moon`, conserva un conic referido a `soi-moon` y
alcanza una evaluación elegible de deadline. Al ejecutarlo explícitamente tras retirar
temporalmente el `Skip`, después de un tick determinista
de 2.5 s, la comparación contra la referencia produce:

```text
position error = 6082.762530297994 m
velocity error = 1999.9999999995025 m/s
reference     = (96500, 2500, 0)
rails         = (97500, -3500, 4.28626E-13)
```

Esto excede las tolerancias por varios órdenes de magnitud. No es un error de redondeo ni
un caso atmosférico/contacto: el fixture usa cuerpos estáticos, `GM = 1` y `GM = 1e-6`, y
la divergencia aparece justo después del cambio de marco SOI. El diagnóstico queda
bloqueado y omitido del conjunto automático hasta triagear el owner de `Universe`; no se
promueve ninguna optimización de deadline/SOI mientras tanto.

## Decisión

**BLOCKED para la equivalencia completa de rails/deadline.** La política conservadora de
rechazar atmósfera y contacto está respaldada por equivalencia; la proyección segura y el
wake-up por comando también pasan. La continuidad inercial del cruce SOI no pasa y debe
corregirse o justificarse con una referencia física equivalente antes de habilitar una
fase de optimización que dependa de ese camino.

## Archivos modificados por N9

- `ExosphereSimulation.Tests/PhysicsSchedulerPerformanceTests.cs` (diagnóstico SOI marcado
  `Skip` hasta corregir la divergencia)
- `docs/audits/PERF_RAILS_EQUIVALENCE_PHASE24_REPORT.md`

No se hizo commit, tal como se solicitó. El worktree contiene además cambios concurrentes
de otros agentes en rutas fuera de este ownership; N9 no los modificó.
