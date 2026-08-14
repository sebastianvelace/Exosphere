# Scheduler físico — Fase 23 P1

## Alcance y ownership

Esta subfase sólo modifica:

- `ExosphereSimulation.Tests/PhysicsSchedulerPerformanceTests.cs`
- `tools/SchedulerBenchmark/Program.cs`
- este informe

No se modificó `Universe.cs` ni ninguna ruta física de runtime. El objetivo fue hacer
observable la carga del scheduler y cubrir wake-ups deterministas sin introducir una
optimización no validada.

## Cobertura añadida

El filtro oficial `PhysicsSchedulerPerformanceTests` pasó 14/14. Durante la auditoría
hubo un bloqueo transitorio por dos archivos ajenos que estaban siendo editados en el
worktree; el filtro volvió a compilar y pasar cuando esos cambios externos terminaron.
Los cuatro casos añadidos son:

- una nave en rails dentro de atmósfera que rechaza el deadline y despierta RK4;
- una nave con tren de aterrizaje en contacto que fuerza la cadencia de 5 ms;
- una pareja dockeada cuyo secundario se omite y cuya restricción se aplica una vez;
- un fragmento separado por staging cuyo throttle lo despierta en el scheduler mixto.

La cobertura existente conserva los contratos de proyección de rails, catch-up antes de
un wake-up por throttle y rechazo de conics con periapsis atmosférico.

## Benchmark

El benchmark cambió a `scheduler_phase23_v1` y ahora conserva dos niveles de telemetría:

- `last_tick_*`: snapshot del último tick, útil para diagnóstico puntual;
- `sample_window_*`: suma de todos los ticks medidos, que separa dispatches, proyecciones,
  skips, rails slices y catch-up.

También se añadió `wake_catchup`, un escenario determinista que proyecta una nave en
rails y aplica throttle a mitad de la ventana. El benchmark falla si ese escenario no
registra al menos un catch-up o si cualquier estado medido deja de ser finito.

Corrida local, 80 muestras y 10 warm-up:

| Escenario | p50 ms | p95 ms | dispatches/muestra | proyecciones/muestra | catch-up/muestra | alloc/tick |
|---|---:|---:|---:|---:|---:|---:|
| `full_single` | 0.0890 | 0.1075 | 1.000 | 0.000 | 0.000 | 5,982.6 |
| `full_fleet` | 0.2661 | 3.2870 | 4.000 | 0.000 | 0.000 | 19,971.8 |
| `rails_fleet` | 1.2922 | 1.9412 | 32.000 | 0.000 | 0.000 | 190,078.6 |
| `mixed_fleet` | 6.2722 | 14.4753 | 450.000 | 396.000 | 0.000 | 718,567.3 |
| `wake_catchup` | 2.3119 | 5.2582 | 50.013 | 12.375 | 0.013 | 211,844.0 |

Contadores exactos relevantes de la ventana:

- `mixed_fleet`: 36,000 dispatches, 31,680 proyecciones, 0 catch-up;
- `wake_catchup`: 4,001 dispatches, 990 proyecciones, 1 catch-up;
- `summary_finite=true` y `summary_valid=true`.

Los tiempos y asignaciones son diagnósticos de esta máquina; no constituyen una promesa
de FPS ni una prueba de ahorro de CPU por sí solos.

## Gaps y decisión

No hay una optimización segura adicional en esta subfase sin tocar `Universe.cs`: la
telemetría actual no expone una razón de wake-up específica para atmósfera, contacto,
staging, SOI o catch-up; sólo permite inferir el camino mediante los contadores y el
estado público. La cobertura nueva valida esos caminos, pero no inventa contadores de
runtime.

El benchmark todavía no simula docking, staging, contacto o SOI como eventos dentro de
la ventana. Añadir esos estímulos sería útil en una siguiente fase, pero requeriría
contratos de fixture más grandes y, para medir razones específicas, una ampliación
autorizada de la telemetría de `Universe`.

La suite global se inició, pero se interrumpió deliberadamente por la solicitud P1 de no
mantener una corrida larga abierta; por ello no se reporta un resultado global como PASS.
No se tocaron los archivos ajenos. El proyecto de simulación y el benchmark compilaron
con 0 warnings y 0 errors, el filtro del scheduler pasó 14/14 y `git diff --check` pasó.
