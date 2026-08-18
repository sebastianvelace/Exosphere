# Phase 51 — deferred physics candidate gate

Fecha: 2026-08-17  
Commits: `9504874` (`perf: add opt-in deferred physics candidate`), `ca9481c` (`test: close phase51 candidate telemetry gates`)
Decisión: **HOLD para producción**

## Resultado

Se añadió una ruta candidate experimental en `Universe`, pero permanece apagada por
defecto mediante:

```ini
[simulation]
deferred_physics_candidate_enabled=false
```

Cuando se habilita explícitamente, sólo puede omitir slices de una nave no activa que:

- esté lejos del vessel activo y sea `Hibernated` según la política existente;
- tenga órbita válida y un deadline físico de rails diferible;
- tenga un guard externo que confirme materialización en el epoch solicitado;
- no tenga un guard disponible o que lance una excepción.

El estado omitido conserva el último epoch seguro. Al alcanzar el deadline, el scheduler
reconstruye la posición/velocidad desde el conic anclado antes de continuar. El contador
`CandidateDeferredSkips` permite distinguir este candidate de los skips de proyección rails
existentes. El harness emite una línea `PERF_SCHEDULER_CANDIDATE schema=1` por frame, con
`enabled` y `deferred_skips`; el contrato de rendimiento rechaza campos mal formados y
cualquier skip cuando la flag oficial está desactivada.

## Verificación

- Candidate disabled: conserva la ruta rails existente y sus dispatches.
- Candidate enabled: omite slices seguros y pasa el test de catch-up en el deadline.
- Guard inexistente o con excepción: vuelve a la ruta existente.
- Estado de systems: `Phase` ya forma parte del snapshot; los saves antiguos sin ese campo
  deserializan a `Active`, elección conservadora.
- Suite completa: **696/696 PASS**.
- Tests del candidate: **6/6 PASS**, incluyendo guard desactivado, épocas finitas/no futuras,
  guard con excepción y reset de telemetría ante delta inválido.
- Contrato visual: **PASS**, con fixtures que aceptan reentrada orbital física y rechazan
  ejecución demo o ausencia de catch; también quedan cubiertos los modos J, staging, docking
  y EDL por el contrato fuente/harness existente.
- Contrato de rendimiento: **53 PASS / 1 SKIP** (el único skip es telemetría dinámica sin
  log de framebuffer suministrado).
- Build `ExosphereSimulation.csproj`: **0 warnings / 0 errors**.
- Build `Exosphere.csproj`: **0 warnings / 0 errors**.
- Smoke Godot headless con `--log-file`: **PASS**.

El smoke framebuffer inicial no pudo ejecutarse en esta VM porque `/tmp/.X11-unix` estaba
montado con propietario `nobody` y Xvfb rechazaba crear sus sockets; no era un fallo del
código del juego. La matriz G siguiente sí consiguió abrir Xvfb, pero quedó limitada por el
presupuesto de tiempo del render software.

## Matriz G — ascenso framebuffer y diagnóstico de coste

La repetición aislada `--ascent --flight7 --run-id phase51-g-vp1` sí consiguió abrir Xvfb y
produjo capturas de `pad`, `liftoff`, `maxq`, `hotstage` y `separation`. La corrida no se
etiqueta como órbita válida: terminó `TIMEOUT` al cumplir 1200 s, con `1121` frames y el
último estado físico finito en `t=209.9 s`, `alt=117149.4 m`, `apo=147809.2 m`, seis motores
de Ship activos y `failedEngines=0`. No hubo `NaN`, `FAIL` físico ni pérdida estructural.

El contrato dinámico sobre el log parcial dio `64 PASS / 1 FAIL / 0 SKIP`: las 1120 líneas
`PERF_FRAME` y las 1120 líneas `PERF_SCHEDULER_CANDIDATE` fueron válidas, con el candidate
apagado y cero skips. El único fallo fue el presupuesto de frame, medido en esta VM:

| Métrica | Resultado |
|---|---:|
| `frame_ms` medio / p95 / p99 / máximo | `1070.966 / 1741 / 1999 / 5344 ms` |
| `scheduler_ms` medio / máximo | `9.040 / 47.494 ms` |
| scheduler como fracción del frame medio | `0.84%` |

Conclusión: esta evidencia no justifica activar hibernación física ni cambiar el scheduler;
el coste dominante está en presentación, render, UI/telemetría o composición bajo llvmpipe.
La siguiente oleada debe perfilar esos dominios por separado y repetir el gate en hardware
GPU real. Las capturas de liftoff y separation muestran además `33/33` y `39/39` motores
respectivamente, por lo que la discrepancia de HUD de motores no se reproduce en esta corrida.

## Por qué no se promueve

El game layer sólo puede autorizar el candidate cuando el runtime de systems está en el
epoch exacto, sin alertas, callbacks pendientes ni deadline de consumo. Si una nave
materializada queda dormida, todavía no existe un dispatcher que avance sus consumibles y
comunicaciones durante el intervalo diferido. Promoverlo ahora podría congelar life support,
energía, térmica o blackout mientras la física continúa avanzando.

Por tanto, el candidate es una herramienta de medición y no una optimización activa del juego.

## Requisitos para `PROMOTE`

1. Añadir catch-up de `VesselSystemsRuntime` por nave, con epoch exacto y sin consumir
   wall-clock.
2. Integrar deadlines de systems en la misma cola temporal que SOI, periapsis, docking,
   staging, callbacks y EDL.
3. Emitir telemetría por vessel: tier, wake reason, último epoch materializado, deadline,
   skips, catch-ups y fallback.
4. Repetir la matriz de paridad con 32 naves en coast y 4 force-sensitive, comparando contra
   `FullPhysics` en posición, velocidad, recursos, callbacks y SOI.
5. Ejecutar la matriz visual de Flight 7, salto `J`, staging, docking, reentrada y catch con
   framebuffer real. Cualquier giro, pérdida de motores, clipping o regresión de HUD implica
   `HOLD`.
