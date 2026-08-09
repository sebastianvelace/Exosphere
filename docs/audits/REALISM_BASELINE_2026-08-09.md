# Baseline de realismo y jugabilidad — 2026-08-09

**Rama:** `codex/realism-program`
**Ejecución atmosférica:** `baseline-atm-v1`
**Artefactos:** `/tmp/exo_baseline-atm-v1/` y `/tmp/exo_baseline-atm-v1.log`

## Estado de la base

- `origin/main` se integró en una rama limpia sin sobrescribir el WIP del usuario; ese WIP está
  preservado en un stash reversible.
- La serie atmosférica existente y el plan multiagente están publicados en la rama de trabajo.
- Build Godot/C# sin warnings ni errores.
- Suite xUnit completa: **526/526**.
- Perfil termodinámico: 8/8 tests.
- Estado de aerosoles/clima: 8/8 tests.

## Matriz framebuffer

La ejecución fuera del sandbox resolvió el bloqueo de X11 y terminó con `ATMOSPHERE_OK`:

- 16/16 hitos: suelo día/amanecer/atardecer/noche, 10/30/70/120/400 km día/noche y cockpit
  día/noche.
- 1.157 frames de convergencia.
- Tiempo medio por frame entre **159,65 y 160,51 ms** en llvmpipe; es una referencia del
  entorno de CPU, no un objetivo universal de hardware.
- Sin `GAP`, `FALLBACK` ni errores de shader.
- Día de suelo: `mean=0.46518`, `skyWhiteClipFrac=0.01911`, `neonGreenFrac=0`.
- Atardecer de suelo: `twilightWarmth=0.36761`, `twilightHorizonMean=0.15681`.
- Noche de suelo: `sharpStarCount=28`, `darkFrac=0.99621`.
- 400 km día: `mean=0.10038`, con limbo azul visible y espacio negro fuera de la columna.

Las imágenes `exo_play_ground_day.png` y `exo_play_400km_day.png` se revisaron visualmente:
el gradiente diurno es continuo, el limbo orbital está presente y no hay frame negro. El
número de estrellas en noche es deliberadamente bajo y queda como parámetro de comparación para
la futura calibración de exposición y magnitud estelar.

## Ascenso E2E de Flight 7

Ejecución `baseline-ascent-v1` (`--ascent --flight7`) completada con `ASCENT_ORBIT_OK`:

- Secuencia observada: `IGNITION → LIFTOFF → MAX_Q → MECO → hot-stage → SEPARATION →
  ASCENT_SHIP → INSERT → ORBIT`.
- Órbita final: aproximadamente **158 × 143 km**, excentricidad `e=0.001`.
- 1.992 frames de diagnóstico; `insertObserved=true`.
- Velocidad vertical de inserción dentro de la telemetría: mínimo `−99,7 m/s`, máximo de
  descenso `99,7 m/s`.
- Se generaron capturas de pad, liftoff, max-q, hot-stage, separación y órbita en
  `/tmp/exo_baseline-ascent-v1/`.

La telemetría deja una observación para la auditoría de propulsión: durante la primera fase el
campo de diagnóstico reporta `throttle > 0` con `runningEngines=0` y `spool=0`, aunque el
propelente sí disminuye y el vehículo acelera. Debe verificarse si es sólo un contador de HUD o
si el modelo está aplicando empuje sin estados de arranque coherentes.

## EDL E2E — fallo reproducible

Ejecución `baseline-edl-v1` (`--edl`) alcanzó entrada, pico de calentamiento, retro-burn, flip
físico y descenso final, pero terminó correctamente clasificada como fallo:

- `ENTRY → PEAK_HEATING → AERO_DESCENT → RETRO_BURN → FINAL_DESCENT` sí se observaron.
- El flip terminó en 21,1 s con alineación `0.99624` y `omega=0.0683 rad/s`.
- Cerca de 11 m la velocidad vertical era sólo `−0,4 m/s`, pero el contacto posterior dejó
  `34,21 m/s` de impacto, `peakLegLoad=6.399 MN`, `contacts=1` y `settled=True`.
- El runner recibió `CRASHED`, no se creó `exo_play_touchdown.png` y el gate EDL falló.

Esto apunta a una interacción entre la lógica de throttle/engine-out final, la detección de
contacto y la respuesta de las patas: no se debe convertir este estado en touchdown artificial.
El siguiente tranche debe corregir el perfil de frenado y/o el contacto, añadir una prueba de
velocidad de contacto segura y repetir `--edl` hasta obtener un aterrizaje físico verificable.

## Limitaciones descubiertas

1. La LUT de densidad ya usa `P/T`, pero transmittance y scattering múltiple todavía consumen
   perfiles exponenciales de `AtmosphereOptics`; hay que cerrar esa paridad antes de ampliar la
   cáscara termósferica visible.
2. El estado de aerosoles/clima existe y está validado en CPU, pero aún no modifica el shader ni
   invalida LUTs por revisión.
3. La matriz atmosférica no es un E2E de misión: aún falta ejecutar menú→VAB→lanzamiento→órbita→
   reentrada→aterrizaje con telemetría de controles y persistencia.
4. La medición llvmpipe muestra un arranque/render lento; se necesita separar coste de primera
   construcción de LUT de coste estable y precalentar recursos durante la pantalla de carga.

## Próximos criterios de comparación

- La nueva paridad debe conservar `ATMOSPHERE_OK`, no aumentar el tiempo medio de frame más de
  10% sin una explicación, y mantener monotonicidad/energía en tests CPU.
- El clima debe cambiar sólo la contribución Mie/aerosol; Rayleigh, ozono y refracción deben
  permanecer invariantes salvo que una fuente documentada indique lo contrario.
- El E2E debe demostrar progreso físico, no sólo una captura estática: separación, staging,
  engine restart, actitud, contacto y estado de guardado deben aparecer en el log.
