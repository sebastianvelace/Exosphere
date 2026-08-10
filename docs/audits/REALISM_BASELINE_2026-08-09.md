# Baseline de realismo y jugabilidad — 2026-08-09

**Rama:** `codex/realism-program`
**Ejecución atmosférica:** `baseline-atm-v1`
**Artefactos:** `/tmp/exo_baseline-atm-v1/` y `/tmp/exo_baseline-atm-v1.log`

**Actualización visual:** `aerosol-v2` (`/tmp/exo_aerosol_v2/` y
`/tmp/exo_aerosol_v2.log`) es la matriz posterior a la integración climática.

## Estado de la base

- `origin/main` se integró en una rama limpia sin sobrescribir el WIP del usuario; ese WIP está
  preservado en un stash reversible.
- La serie atmosférica existente y el plan multiagente están publicados en la rama de trabajo.
- Build Godot/C# sin warnings ni errores.
- Suite xUnit completa en la base inicial: **526/526**; después de los contratos ópticos y de
  propulsión: **534/534**.
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

La matriz `aerosol-v1` conservó los 16 hitos y 1.157 frames, pero fue rechazada por el gate de
exposición (`ground_day skyWhiteClipFrac=0.16751`) al usar AOD550 Earth 0.08. El perfil costero
despejado AOD550 0.04 de `ce97d59` produjo `aerosol-v2`: `ATMOSPHERE_OK`, 16/16 hitos, 1.157
frames, `ground_day skyWhiteClipFrac=0.08642`, sin `GAP`, `FALLBACK` ni errores de shader.

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

La telemetría histórica dejaba una observación: durante la primera fase el contador consultaba
siempre el cluster Ship y reportaba `runningEngines=0` aunque el booster sí producía empuje. El
harness ahora deriva el estado de `Parts.ActiveEngines` y expone `selected`, `lit`, `ramp`,
`residual` y `failed`. El gate stage-aware de Flight 7 exige y observa `33 → 39 → 6` en
`/tmp/exo_stage_ascent_v1.log`, además de mantener `ASCENT_ORBIT_OK`.

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

## Iteraciones EDL posteriores

- `edl-fix-v3` (`/tmp/exo_edl_fix_v3.log`) confirmó que mantener empuje ante un solo pie evita el
  corte prematuro, pero la transición a un motor a 19,2 m elevó la deriva horizontal a 11,7 m/s;
  terminó en `CRASHED`, 34,66 m/s, un contacto y sobrecarga de 7,07 MN.
- `edl-fix-v4` mostró el siguiente defecto: el mínimo de dos motores quedó bloqueado mientras no
  había contacto, por lo que el vehículo entró en hover a unos 520–577 m con `throttle=0.400`.
- `edl-fix-v5` eliminó ese hover de tres motores y llegó con dos motores hasta ~20 m, pero el
  mínimo de 40 % produjo otro rebote/hover: de 19,9 m pasó a 39,2 m sin registrar contacto. El
  gate se detuvo conservando las capturas y el log; la política one-engine/low-thrust final aún
  es un trabajo abierto.
- `edl-v6` (`/tmp/exo_edl_v6.log`) validó la transición 3→2 y el nuevo gate de energía baja, pero
  no alcanzó un touchdown: a 286,6 m iba a `−10,8 m/s`, rebotó a `+13,5 m/s`, derivó hasta 74 m
  y terminó a 1,5 m con impacto de `106,31 m/s`, `peakLegLoad=44,784 MN`, `contacts=1` y
  `overload=True`. Se observaron `ENTRY → PEAK_HEATING → RETRO_BURN → FLIP_COMPLETE →
  FINAL_DESCENT`; el gate sigue fallando y no existe `exo_play_touchdown.png`.
- `a13fc02` corrige el salto de actitud identificado en v6: el eje final ya no cambia a
  `-velDir` al superar 12 m/s laterales mientras el vehículo está subiendo. FINAL mantiene
  un eje principalmente vertical, con inclinación limitada a 20° para reducir deriva, y
  conserva un término horizontal acotado en la demanda de frenado. El cambio pasa 8 pruebas
  EDL y compila sin warnings; el playtest `edl-final-axis-v7` queda pendiente porque otro
  harness visual tenía el `flock` global ocupado. No se considera touchdown hasta obtener
  `exo_play_touchdown.png`, `TOUCHDOWN`, contacto multipunto y el gate completo.

- `edl-final-axis-v7` (`/tmp/exo_edl_v7.log`) confirmó el primer efecto de `a13fc02`: el salto
  de eje final desapareció y no se repitió el impacto de 106,31 m/s de v6. Sin embargo, la
  combinación de dos motores y el mínimo físico de `40 %` dejó una oscilación sostenida entre
  aproximadamente 272 y 335 m; el proceso se detuvo en `t≈372 s` sin contacto ni resumen.
- `edl-lateral-cap-v8` (`/tmp/exo_edl_v8.log`) incorporó `9e5c924`, que limita a `4 m/s` la
  demanda de frenado horizontal en FINAL. La amplitud del rebote se redujo, pero el sistema
  quedó en hover de dos motores entre aproximadamente 211 y 230 m (`vUp≈+0,8/-2,2 m/s`);
  terminó en `t≈382 s` sin tocar la superficie. Esto conserva el gate abierto: no es touchdown.
- `edl-one-engine-v9` (`/tmp/exo_edl_v9.log`) incorporó `c8a7558` y sí demostró la transición
  discreta solicitada: a `t=356,6 s`, `alt≈226 m`, `vUp≈-0,5 m/s`, `horiz≈14,3 m/s` pasó de
  dos a un motor. El vehículo no alcanzó el suelo; con un motor siguió oscilando alrededor de
  220–240 m, llegando a `throttle≈0,77–1,00`, y el run fue detenido en `t≈382,2 s` (`vUp≈-5,8
  m/s`). `ac4a08e` aumenta de forma acotada el sesgo descendente sólo en ese estado, pero no
  tiene validación visual todavía. No existe `TOUCHDOWN`, `exo_play_touchdown.png` ni un
  resumen de aterrizaje; por tanto v7–v9 son corridas **incompletas**, no aterrizajes aprobados.

Estas fallas son evidencia física útil: el controlador necesita resolver simultáneamente el
  mínimo de throttle, la selección discreta de motores y la deriva lateral antes de declarar
  `LANDED`. Ningún EDL PNG de touchdown se considera válido mientras no exista contacto multipunto
  lento, carga por pata dentro del límite y `IsSurfaceSettled` sostenido.

## Limitaciones descubiertas

1. La LUT, el overload de transporte y el shader pueden usar `AtmosphereDensityProfile` basado en
   `P/T`, aerosoles y ozono. El shader además acepta `AerosolClimateState` opt-in con AOD550,
   Ångström y escala vertical; la ruta sin bloque climático sigue en el camino legacy.
2. La envolvente climática se evalúa en el observador y se aplica a la contribución Mie durante
   el transporte realtime. Las LUTs de transmittance/multiple scattering siguen estáticas por
   cuerpo y no se regeneran durante un time-warp.
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
