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
