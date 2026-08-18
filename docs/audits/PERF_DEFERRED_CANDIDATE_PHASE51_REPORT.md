# Phase 51 — deferred physics candidate gate

Fecha: 2026-08-17  
Commit: `9504874` (`perf: add opt-in deferred physics candidate`)  
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
existentes.

## Verificación

- Candidate disabled: conserva la ruta rails existente y sus dispatches.
- Candidate enabled: omite slices seguros y pasa el test de catch-up en el deadline.
- Guard inexistente o con excepción: vuelve a la ruta existente.
- Estado de systems: `Phase` ya forma parte del snapshot; los saves antiguos sin ese campo
  deserializan a `Active`, elección conservadora.
- Suite completa: **693/693 PASS**.
- Build `ExosphereSimulation.csproj`: **0 warnings / 0 errors**.
- Build `Exosphere.csproj`: **0 warnings / 0 errors**.
- Smoke Godot headless con `--log-file`: **PASS**.

El smoke con framebuffer real no pudo ejecutarse en esta VM porque `/tmp/.X11-unix` está
montado con propietario `nobody` y Xvfb rechaza crear sus sockets; no es un fallo del código
del juego. La corrección requiere cambiar el entorno de ejecución, no el repositorio.

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
