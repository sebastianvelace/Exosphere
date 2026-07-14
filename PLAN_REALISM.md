# Exosphere — Plan de mejoras de REALISMO (auditoría end-to-end)

> Basado en (a) un **playthrough headless por telemetría** del ascenso real (autopiloto [G],
> ignición→Max-Q→staging→circularización, muestreado cada 5 s) y (b) una **auditoría de los datos
> y modelos** contra valores del mundo real. Criterio rector: **lo más realista posible**.
> Cada ítem trae evidencia, referencia real, causa-raíz (`archivo:línea`), fix propuesto, archivos
> y criterio de aceptación. Organizado en olas para ejecución multi-agente con archivos exclusivos.

## ESTADO DE EJECUCIÓN (actualizado tras implementar e ir validando por telemetría)

**HECHO y validado en `main`:**
- **Ola 1 (R1-R3) — ascenso realista.** Gravity turn agresivo + hot-staging en MECO. Telemetría:
  Max-Q ~33 kPa a 8 km, separación a 61 km/2.16 km/s, **órbita a 150 km/7.67 km/s en ~8 min**.
- **R9, R10 — touchdown EDL físico multipunto, ISP cluster 363 s.**
- **R8, R4 — ya estaban hechos** (escudo data-driven; drag de ascenso con el modelo de 9 m).
- **Reingreso AHORA OCURRE** (era IMPOSIBLE): el guard on-rails destruía cualquier órbita suborbital
  en el apoapsis apenas el periapsis caía bajo el radio → la nave se aniquilaba a 200+ km al
  deorbitar. Ahora solo el cónico RADIAL (degenerado) se resuelve al instante; una elipse suborbital
  coastea y se destruye al tocar superficie (la atmósfera/EDL vuelan el reingreso). Telemetría:
  deorbit → aero-frenado (q sube) → calentamiento. + tanque de Starship con escudo windward.

- **R7 — termosfera residual (HECHO, validado numéricamente).** Cola exponencial de densidad sobre
  `MaxAltitude` (140 km) anclada a la densidad del borde ISA, H=45 km, corte a 1000 km. Contra
  NRLMSISE-00 (actividad solar media) queda dentro de factor ~2-5 entre 140 y 500 km. Solo
  `GetDensity` tiene cola; presión sigue en vacío sobre 140 km y `MaxAltitude` sigue siendo la
  frontera aerodinámica de los controllers. Test de aceptación: órbita circular de 150 km en RK4
  decae de forma lenta y monótona (`OrbitalDecayTests`). A warp ≥10 el vessel solo pasa a on-rails
  fuera de la termosfera residual; dentro de `ThermosphereTopAltitude` se fuerza RK4 (B3)
  (gate `density < 0.01`) y NO decae — limitación documentada.

- **R6 — sustentación de cuerpo con ángulo de ataque (HECHO).** `AerodynamicsModel.ComputeLift`:
  CL = 0.7·sin(2α), perpendicular al flujo en el plano eje-flujo, hacia el lado del morro; signo
  correcto volando de cola. Cero exacto axial y de costado puro (cilindro simétrico), L/D ≈ 0.3 a
  α=70° (régimen EDL real de Starship). `Vessel.ComputeDragAt` ahora delega en `AerodynamicsModel`
  (drag+lift, se eliminó la duplicación inline) y devuelve la fuerza aero total. La EDL actual
  comanda belly-flop ~90° ⇒ lift ~0 ⇒ sin regresión del perfil R13 validado; usar α<90° en la EDL
  para guiado con lift queda como mejora de game-layer.

- **R13 — reingreso sobrevivible end-to-end (HECHO, validado).** La EDL ahora mantiene belly-flop
  todo el descenso (flip por altitud baja ~800 m, no por `stopDist` vertical que era ∞), el aero
  frena (peakQ 390→**21 kPa**, ≈real), el escudo cubre las piezas windward, y un controlador de
  perfil de descenso lineal hace el flip-and-burn final. Telemetría (Starship ~148 t, reserva 6%,
  deorbit desde 150 km): aero-frena a ~70 m/s → **aterriza ~0 m/s, sin destruirse**, peakHeat 0.10.
  Commit `93228af`. + decoupler/hot-stage ring con escudo (acero, tol 1700 K).

---

### R13 (resuelto — detalle del root cause). La Starship NO sobrevivía el reingreso — la EDL no la mantenía en belly-flop
- **Evidencia (harness de reingreso, Starship sola ~148 t, deorbit desde 200 km):** la nave **penetra
  profundo a alta velocidad** (4600 m/s a 26 km, 3500 m/s a 22 km) en vez de frenar arriba; q alcanza
  **~390 kPa** (≈8× el reingreso real de ~50 kPa) y se quema (`ThermalBreakup`) a ~20 km con
  heatRatio 1.2 / maxT ~1810 K. Pasa igual con entrada empinada (-130 m/s) o somera (-70 m/s).
- **Causa-raíz PRECISA (`scripts/EDLController.cs:131-136`):** el "physics gate" pasa de la entrada
  belly-flop (Entry/Peak/Aero) a **`Retro` (motores retrógrados = eje AXIAL, Cd ~0.6, área pequeña)**
  en cuanto la *distancia de frenado VERTICAL* (`stopDist = vDown²/2aVert`) cubre la altitud — pero
  ignora la enorme velocidad HORIZONTAL aún presente. Al voltear a axial pierde el drag broadside
  (Cd 1.5, área lateral 9 m × L) que debería frenarla en la atmósfera alta, y el escudo ventral deja
  de encarar el flujo. Resultado: drag débil → penetra profundo (4600 m/s a 26 km) → q ~390 kPa y
  calor que supera las tolerancias. El modelo aero YA da alto drag broadside; el fallo es el gate
  EDL que abandona el belly-flop demasiado pronto (y solo razona sobre el componente vertical).
- **Fix propuesto:** en `scripts/EDLController.cs`, fase Entry/Peak/Aero → comandar y MANTENER actitud
  belly-flop (eje ⟂ a la velocidad, escudo al flujo) hasta el flip-and-burn final; verificar que el
  drag broadside frena a ~1-2 km/s en la atmósfera alta antes de descender. Calibrar tolerancias
  térmicas y el flip-and-burn por masa. Validar con el **harness de reingreso** (deorbit → aterrizaje).
- **Aceptación:** una Starship con masa de aterrizaje realista reingresa belly-first, frena por aero a
  ~ sub-km/s en la atmósfera alta sin superar el escudo, hace flip-and-burn y aterriza ≤2 m/s.
- **Método de validación (reproducible):** autoload temporal `_ReentryShot` (patrón visual-testing):
  `JumpToOrbit(200km)` + `TriggerStaging()` + drenar propelente a reserva + deorbit retrógrado, luego
  registrar alt/spd/q/heatRatio/maxT/fase hasta aterrizaje o destrucción.

### R14. (relacionada) El test de reingreso usaba el stack completo / tanques llenos
- La Starship reingresa casi vacía (~120-150 t), no con 1300 t de propelente (TWR ~1, no frena). Y se
  reingresa solo la Starship, no el stack. El guiado de aterrizaje debe asumir masa de aterrizaje real.

---

## 0. Veredicto de la auditoría

**Lo que YA es realista (no tocar):**
- Datos de cuerpos: Tierra radio 6371 km, masa 5.972e24, GM 3.986e14, SOI 924 000 km, día sidéreo
  86164 s, atmósfera ISA con capas y lapse rates correctos (−6.5 K/km troposfera, etc.). ✓
- Piezas: Super Heavy 74.4 MN SL / O/F 3.55 / dry 200 t / prop 3300 t; Starship 6 motores 13.5 MN
  vac / dry ~100 t / prop 1200 t; TWR liftoff 1.58. Todo ≈ Starship V1 real. ✓
- Aero de reingreso orientación-dependiente (área/Cd belly-flop vs axial) + multiplicador transónico
  de Mach. ✓ · EDL con belly-flop + flip-and-burn + suicide burn. ✓ · Órbitas patched-conic. ✓

**Telemetría del ascenso real (autopiloto [G], engine 8x):**
```
t=0    TWR 1.58  m=4800t           (liftoff)
t=51   q≈31 kPa  alt=8.2km  spd≈vsp=347   (Max-Q, casi VERTICAL)
t=123  alt=66km  spd=1580  vsp=1280  → horizontal solo ~930 m/s; motores CORTAN con ~585 t de prop
t=123-256  COAST balístico hasta apoapsis ~153 km (lofteo extremo)
t=261  re-enciende el SUPER HEAVY a 83.5 MN en el apoapsis para circularizar
t=287  recién ahí hace staging a Starship (13.5 MN); el booster hizo la inserción
```
**Referencia real:** Max-Q T+55 s a ~12-14 km (~33 kPa); MECO/hot-staging T+~2:40 a **~65 km y
~2.4 km/s casi horizontal**; el Super Heavy se separa con reserva y hace boostback; **Starship**
hace la inserción hasta SECO ~150 km / 7.6 km/s. El perfil del juego está MUY lofteado y la etapa
equivocada hace la órbita.

---

## OLA 1 — Perfil de ascenso (el mayor quiebre de realismo; probado por telemetría)

### R1. Gravity turn demasiado vertical → lofteo extremo
- **Evidencia:** a Max-Q (8 km) el cohete va ~94 % vertical; en el "MECO" (66 km) la velocidad
  horizontal es ~930 m/s de 1580 m/s totales. Real: a Mach 1 ya pitcheado ~30-45°.
- **Causa-raíz:** `scripts/AscentController.cs:254` — `f = clamp((alt−2000)/90000, 0, 0.90)`: el
  pitch-over es **lineal en altitud** y sólo llega a 0.90 a 92 km; mantiene ~10 % de empuje
  vertical siempre. A 8 km da f≈0.067 (casi recto).
- **Fix:** ley de gravity turn realista — iniciar el kick a ~150-300 m/s, y seguir prograde (la
  velocidad relativa) con un pitch programado mucho más agresivo (objetivo ~45° a ~Mach 1-2,
  ~10° a ~40 km), o un guiado por ángulo de trayectoria de vuelo. Apuntar a un apoapsis objetivo
  bajo (~150 km) construyendo velocidad horizontal temprano, NO lofteando a 153 km balístico.
- **Archivos:** `scripts/AscentController.cs`.
- **Aceptación:** captura de telemetría donde a Max-Q el vehículo esté pitcheado ≥20° y en el MECO
  la velocidad sea mayormente horizontal (>1.8 km/s) a ~65-80 km; sin coast balístico de 90 km.

### R2. Staging en apoapsis/depleción en vez de en MECO / hot-staging
- **Evidencia:** el Super Heavy corta a 66 km con prop, coastea a 153 km, **re-enciende ahí** y
  recién hace staging a t=287. El booster hace la inserción orbital; Starship casi no se usa.
- **Causa-raíz:** `scripts/AscentController.cs:389` `AutoStage` separa sólo cuando
  `stageFuel < 4000` (depleción); + el guiado corta el booster al alcanzar apoapsis objetivo y
  circulariza con la etapa activa (sea cual sea). `scripts/MissionManager.cs:206-219` arma MECO por
  "propelente casi agotado".
- **Fix:** separar en **MECO realista**: cuando el booster alcanza una velocidad/altitud de staging
  (~2.2-2.4 km/s, ~65 km) **con reserva** (deja ~6-8 % para boostback/landing), hacer hot-staging y
  pasar la inserción a Starship. La circularización la hace SIEMPRE la etapa superior.
- **Archivos:** `scripts/AscentController.cs` (trigger de staging + handoff), `scripts/MissionManager.cs`
  (criterio de MECO por velocidad/altitud, no sólo depleción).
- **Aceptación:** el Super Heavy se separa a ~65 km / ~2.4 km/s con propelente remanente; Starship
  hace la inserción a órbita. Fase MECO→SEPARATION→ASCENT_SHIP coherente con la telemetría.

### R3. Booster sin reserva de boostback/aterrizaje
- **Evidencia/causa:** ligado a R2 — hoy el booster quema hasta ~vacío.
- **Fix:** reservar propelente del booster en el corte de MECO; (opcional, ola futura) secuencia de
  boostback + descenso. Mínimo de esta ola: que el booster separado conserve reserva realista.
- **Archivos:** `scripts/AscentController.cs` (mismo dueño que R2; coordinar).
- **Aceptación:** masa del booster separado incluye ~6-8 % de propelente.

> Olas 1 = un solo agente sobre `AscentController.cs` + `MissionManager.cs` (R1+R2+R3 están
> acoplados al guiado/staging). Verificación: re-correr la telemetría headless y comparar el perfil.

---

## OLA 2 — Fidelidad de simulación (sim, con tests + physics-reviewer)

### R4. Área de referencia de drag en ASCENSO es burda ✅ HECHO
- **Fix aplicado:** `EstimateReferenceArea` usa el diámetro físico del grafo (`MaximumDiameter`);
  `Vessel.ComputeDragAt` delega en `AerodynamicsModel` con `EffectiveArea` orientación-dependiente
  (mismo modelo 9 m que reingreso). Max-Q telemetría ~33 kPa a ~8 km.
- **Archivos:** `ExosphereSimulation/Physics/AerodynamicsModel.cs`, `ExosphereSimulation/Vessel.cs`.
- **Aceptación:** ✅ Max-Q realista; ascenso y reingreso comparten modelo de área.

### R5. Modelo "1 parte-motor por etapa" (la mayor simplificación)
- **Hoy:** Super Heavy = 1 parte ≈74 MN; Starship = 1 parte. No hay engine-out, throttling por
  motor, gimbal individual ni empuje asimétrico. Es contrato declarado en CLAUDE.md.
- **Fix (esfuerzo grande, ola propia):** modelar N motores como sub-partes con estado propio
  (encendido/apagado, gimbal, fallo). Habilita engine-out, hot-staging real, boostback.
- **Archivos:** `ExosphereSimulation/Parts/*` (PartGraph, Part, PartDefinition), `data/parts/*.json`.
- **Aceptación:** apagar un motor reduce empuje proporcional y desplaza el centro de empuje; tests.
- **Nota:** romper este contrato impacta render (los 33/6 visuales) y staging; planear aparte.

### R6. Sin sustentación aerodinámica / ángulo de ataque ✅ HECHO
- **Hoy (antes):** el aero era sólo drag (orientación-dependiente). Starship reentra con **lift de
  cuerpo** y control por flaps para cross-range y guiado.
- **Fix aplicado:** `AerodynamicsModel.ComputeLift` — CL = CLmax·sin(2α) (CLmax 0.7), perpendicular
  al flujo en el plano eje-flujo, hacia el lado del morro, con signo correcto volando de cola.
  `Vessel.ComputeDragAt` delega ahora en `AerodynamicsModel` (drag + lift; se eliminó la
  duplicación inline del modelo aero). Sim puro + tests.
- **Archivos:** `ExosphereSimulation/Physics/AerodynamicsModel.cs`, `ExosphereSimulation/Vessel.cs`,
  tests `AerodynamicLiftTests.cs`. EDL game-layer lift-up ~70° DONE (`EDLController` +
  `ComputeLiftUpEntryAxis`).
- **Aceptación:** ✅ lift ⊥ al flujo hacia el lado del morro, cero axial y de costado puro
  (cilindro simétrico), L/D ≈ 0.3 a α=70° (régimen EDL real); el alcance cambia con la actitud.

### R7. Atmósfera cortada a 140 km → sin decaimiento orbital ✅ HECHO
- **Causa:** `max_altitude 140000` en `earth.json`; sobre eso, densidad 0 → LEO no decae nunca.
- **Fix aplicado:** termósfera residual — sobre `MaxAltitude` la densidad decae exponencialmente
  desde la densidad de borde (`ThermosphereScaleHeight` ≈ 45 km, tope `ThermosphereTopAltitude`
  1000 km). Aproximación de escala única, documentada como tal (H real crece con la altitud).
  **Clave:** NO se movió `MaxAltitude` (lo leen EDL/ascenso/systems como límite aerodinámico);
  sólo `GetDensity` gana la cola, `GetPressure` sigue 0 sobre 140 km.
- **Archivos:** `ExosphereSimulation/AtmosphereModel.cs`, `AtmosphereModelJson.cs`,
  `data/bodies/earth.json`, tests `AtmosphereThermosphereTests.cs`.
- **Aceptación:** ✅ densidad positiva/continua/monótona en LEO bajo (150/200/400 km), vacío sobre
  1000 km (7 tests). Revisado por `physics-reviewer` en contexto fresco: veredicto CORRECTO.
- **Nota (B3):** `RequiresOffRailsPhysics` fuerza RK4 mientras haya densidad residual bajo
  `ThermosphereTopAltitude`, así el warp ≥10 ya no congela el lifetime de LEO. Sobre el tope
  termosférico siguen rails/Kepler sin decay espurio.
  Comentario de `AscentController.cs` de parking orbit actualizado (ya no dice "no decae").

---

## OLA 3 — Reingreso / térmico / aterrizaje (pulido de realismo)

### R8. Escudo térmico por proxy, no por flag de datos ✅ HECHO
- **Fix aplicado:** `PartDefinition.HasHeatShield` deserializa `has_heat_shield` del JSON;
  `ThermalModel.HasHeatShield` delega directamente (sin proxy por categoría/tolerancia).
- **Archivos:** `ExosphereSimulation/Parts/PartDefinition.cs`, `ExosphereSimulation/Physics/ThermalModel.cs`,
  `data/parts/starship_command.json`, `starship_tank.json`, `decoupler_heavy.json`.
- **Aceptación:** ✅ tests `PhysicsRegressionTests.HeatShieldProtectsOnlyWhenWindwardFaceMeetsFlow` y
  `ReentryWithoutHeatShieldBurnsThrough`; `VesselRenderer` respeta el flag para tiles windward.

### R9. Touchdown físico multipunto ✅ HECHO V1
- **Fix aplicado:** se eliminó el gate altitud/velocidad y el snap a `IsGroundHeld`. Seis pies
  producen fuerza normal, fricción y torque; carga/recorrido pueden destruir el vehículo.
  `LANDED` exige ≥3 contactos y 0,50 s dentro del envelope cinemático/upright.
- **Archivos:** `SurfaceContactSolver.cs`, `Vessel.cs`, `Universe.cs`, `EDLController.cs`,
  `starship_landing_gear.json`, `VesselRenderer.cs`.
- **Aceptación:** ✅ pruebas puras/integradas y golden EDL con contacto/settlement explícitos.
  Pendientes: terreno DEM/pendiente, stick-slip, contacto de casco y datum sim↔render único.
- **Detalle y fuentes:** `docs/audits/LANDING_CONTACT_REALISM.md`.

### R10. ISP del cluster Starship algo optimista ✅ HECHO
- **Fix aplicado:** `data/parts/starship_engines.json` `isp_vac` **363** (antes 380; mezcla RVac/SL real).
- **Archivos:** `data/parts/starship_engines.json`.
- **Aceptación:** ✅ Δv de etapa superior consistente con ISP ~363 s.

---

## OLA 4 — Sistemas / UX (realismo de misión, backlog)

### R11. Sistemas de vida/energía/comms desconectados de las fases
- **Hoy:** `Systems/*` existen (LifeSupport, Power, Comms, Thermal) pero no atados a eventos:
  eclipse → sin solar; comms con retardo por distancia; consumo por fase.
- **Archivos:** `ExosphereSimulation/Systems/*`, `scripts/SystemsController.cs`.
- **Aceptación:** en sombra cae la energía; el retardo de comms crece con la distancia.

### R12. Boostback + captura en torre (Mechazilla) — depende de R5
- Secuencia real de flip + boostback del Super Heavy y captura. Requiere el modelo multi-motor (R5).

---

## Orden de ejecucion actual
1. No reabrir R1-R4, R8-R10 ni R13 salvo regresion demostrada por telemetria.
2. Backlog fisico real pendiente: R5 multi-motor. (R6 lift/AoA ✅, R7 termosfera/decay ✅, B2 hot-stage overlap ✅)
3. Backlog mision/sistemas: R11 sistemas conectados a fases, R12 boostback/captura dependiente de R5.
4. Backlog visual vive en `PLAN_VISUAL_REALISM.md`; no duplicar aqui la auditoria visual.

## Método de verificación (para cada ola)
- Build 0/0 (sim + juego) + `dotnet test` verde; tests nuevos donde se toque física.
- **Re-correr el harness de telemetría headless** (autoload temporal `_*Shot`, limpieza obligatoria)
  y comparar el perfil contra los números reales de esta auditoría.
- Cambios de física en sim → `physics-reviewer` en contexto fresco antes de mergear.
- Worktrees parten de base que puede ser vieja → diff de 3 puntos + rebase al integrar (gotcha conocido).
