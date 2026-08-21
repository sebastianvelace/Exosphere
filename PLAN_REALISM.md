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

- **R15-R19 — auditoría física Jul 2026 (motores, staging, movimiento terrestre, reingreso),
  HECHO.** Cinco hallazgos, todos gateados por `--ascent --flight7` + `--edl`: (1) fase orbital
  J2000 de la Tierra corregida — `mean_anomaly_at_epoch` tenía 1.1° de error de fase (~1.1 día),
  único planeta interior fuera de la convención `M = L − ϖ`; (2) cap de sub-step térmico revisado
  y **descartado como bug real** — es inalcanzable por 2.5× dado `MaxCoastStep = 2.0 s` (headroom
  subido igual, 256→2048, con test que falla si algo lo vuelve alcanzable); (3) separación de
  etapas ahora conserva CoM y momento angular — antes teletransportaba la nave sobreviviente hacia
  arriba la longitud completa del booster sin offset complementario en el debris, e ignoraba el
  término de transporte `ω × r`; (4) Sutton-Graves ahora usa un radio de estancamiento
  dependiente de actitud (mezclado en `cos²α` igual que el área/Cd aerodinámico) — el error
  original estaba **al revés de lo esperado**: de costado el modelo YA sobre-estimaba el flujo
  ~1.41×, el hueco real era solo en la actitud de morro/cola; (5) discontinuidad de drag
  hipersónico de 4.8% en Mach 5 suavizada con una rampa lineal 5≤M<8, rechazando una meseta
  Newtoniana de ~1.7 que habría excedido el límite Newtoniano de placa plana y roto
  `AerodynamicLiftTests`. Detalle completo, evidencia `archivo:línea` y las tres premisas de diseño
  refutadas durante esta pasada: `docs/audits/PHYSICS_AUDIT_JUL2026.md`.

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

### R5. Modelo "1 parte-motor por etapa" (la mayor simplificación) — ✅ HECHO (lifecycle) / ✅ HECHO (torque)
- **Estado real (previo a esta sesión):** el estado por motor (lifecycle encendido/apagado, gimbal,
  térmico, feed, fallo) ya existía por instancia, antes de este cambio. Lo que faltaba era que un
  fallo asimétrico o una deflexión de gimbal produjeran torque real: los métodos de torque/actitud
  de `PartGraph` usaban una única palanca escalar por parte en vez de la posición 3D real de cada
  mount de motor.
- **Fix aplicado:** `feat(physics): compute real per-engine torque from mount geometry` — nuevo
  `Part.GetEngineInstanceThrustGeometry`, `PartGraph.GetTotalTorque`,
  `PartGraph.GetPitchYawRollAngularAcceleration` (aditivos; los escalares existentes
  `GetTotalThrust`/`GetPitchYawAngularAcceleration`/`GetRollAngularAcceleration` quedan intactos).
- **Archivos:** `ExosphereSimulation/Parts/*` (PartGraph, Part, PartDefinition),
  `ExosphereSimulation.Tests/EngineTorqueTests.cs` (6 tests:
  `NominalSymmetricCluster_ProducesZeroNetTorque`,
  `FailingOffCenterOuterMount_ProducesExactYawTorqueTowardOppositeSide`,
  `FailingDiametricallyOppositeOuterMounts_CancelToNearZeroTorque`,
  `IsolatedGimbalDeflectionOnSingleGimballedMount_ProducesTorqueMatchingCrossProductSign`,
  `GetEngineInstanceThrustGeometry_TiltDirectionGeneralizesForNonVerticalMount`,
  `RegressionSafety_ScalarPitchYawAndRollAuthorityUnchangedByNewTorqueApi`), más el hecho
  `BoosterEngineOutProducesAsymmetricTorque_NotJustProportionalThrustLoss` en
  `ExosphereSimulation.Tests/StarshipFlight7DataTests.cs`.
- **Aceptación:** ✅ suite 369/369 (antes 362); un motor fuera de eje produce torque neto, no sólo
  pérdida de empuje proporcional; el fallo de un motor diametralmente opuesto cancela a ~cero.

### R5b. TVC diferencial por motor — ✅ HECHO
- **Fix aplicado:** `PartGraph.SolveDifferentialGimbal` asigna un comando de gimbal por mount
  (mínimo-norma sobre el Jacobiano de palanca/empuje de cada instancia viva y gimballed,
  regularizado). `Vessel.Tick` dimensiona el torque deseado con
  `GetDifferentialTVCAngularAccelerationEnvelope` (solo mounts que el solver puede comandar)
  y aplica el torque real por geometría en ambas ramas (con y sin input), con suelo RCS.
  Suite: `DifferentialTVCTests.cs` + regresiones de autoridad en stack Flight 7 ensamblado.
- **Archivos:** `ExosphereSimulation/Parts/PartGraph.cs`, `Part.cs`, `Vessel.cs`,
  `ExosphereSimulation.Tests/DifferentialTVCTests.cs`.
- **Aceptación:** ✅ cada mount gimballed recibe su propio comando; un torque de disturbio
  sintético se anula diferencialmente; `[G]` ascent y EDL flip siguen verdes.

### R5c. Torque como disturbio no wireado en `Vessel.Tick` — ✅ HECHO (parcial, adrede)
- **Fix aplicado:** `Vessel.Tick` ahora aplica `GetPitchYawRollAngularAcceleration` como
  disturbio real, pero SOLO en la rama `!hasInput`, no incondicionalmente en ambas ramas.
  Motivo: `GetTotalTorque` lee el `GimbalDeg` real de cada instancia, que el servo de gimbal
  ya mueve hacia el `GimbalOffset` comandado por el piloto en la rama `hasInput` — esa rama
  ya aplica una estimación idealizada de autoridad máxima para esa misma deflexión
  (`GetPitchYawAngularAcceleration`/`GetRollAngularAcceleration` con `GimbalRange` máximo).
  Sumar el torque real por mount ahí también habría contado dos veces la misma cadena causal
  (piloto comanda gimbal → empuje se desvía → torque neto).
- **Archivos:** `ExosphereSimulation/Vessel.cs`, `ExosphereSimulation.Tests/UnpilotedEngineOutTests.cs`.
- **Aceptación:** ✅ empuje simétrico sin input no rota; una falla de motor asimétrica sin
  input produce rotación observable y con signo correcto; la autoridad de actitud pilotada
  existente quedó sin cambios (pin test verificado con comparación real antes/después vía
  `git stash`). Gates `--ascent`/`--edl` sostenidos (ninguno inyecta fallas de motor, así
  que el cambio es inerte en ambos por construcción, confirmado).

### R5d. Magnitud de empuje promediada en cluster mixto bajo steering activo — ✅ HECHO
- **Fix aplicado:** `Part.GetThrustVector` con runtime por motor suma los vectores de
  `GetEngineInstanceThrustGeometry` en vez de inclinación única × ΣT con gimbal promedio.
  Los mounts fijos conservan empuje axial completo; la lateral solo viene de mounts gimballed.
- **Archivos:** `ExosphereSimulation/Parts/Part.cs`,
  `ExosphereSimulation.Tests/MixedClusterThrustVectorTests.cs` (4 tests).
- **Aceptación:** ✅ `GetThrustVector` ≡ Σ geometría; mounts fijos sin lateral; `|F| < ΣT`
  cuando los gimbals divergen; gimbal uniforme preserva la identidad legacy.

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

## OLA JUL2026 — Auditoría de motores, staging, movimiento terrestre y reingreso

> Auditoría separada de la ola original (motores/staging/Tierra/reingreso), ejecutada y cerrada en
> `docs/audits/PHYSICS_AUDIT_JUL2026.md`. Los cinco ítems abajo están **HECHOS y gateados** por
> `--ascent --flight7` + `--edl`; el backlog abierto que dejó esta pasada queda al final.

### R15. Fase orbital J2000 de la Tierra ✅ HECHO
- **Evidencia:** `data/bodies/earth.json` tenía `mean_anomaly_at_epoch: 358.617`; con
  `longitude_of_node + argument_of_periapsis = 102.94719` (= ϖ de Standish para la Tierra/EMB), el
  valor correcto por convención `M = L − ϖ` es `357.517`. Error real: 1.100° (~1.116 días de fase).
  Único planeta interior fuera de convención (Mercurio/Venus/Marte están a <0.05°).
- **Fix:** `357.517` en `data/bodies/earth.json`; se borró además una `surface_gravity: 9.807` raíz
  muerta (nadie la lee; la que sí se lee es `atmosphere.surface_gravity: 9.80665`).
- **Test:** `EphemerisPhaseTests.cs` — `M₀ ≈ L − ϖ` para los 4 planetas interiores, tolerancia 0.05°.
- **Fuera de alcance, dejado así:** Júpiter (~0.35°) y Saturno (~0.33°) exceden la tolerancia y se
  excluyeron explícitamente en vez de aflojar el test — necesitan su propia corrección.
- **Límite conocido no tocado:** sin corrección baricéntrica (Tierra sin masa relativa al Sol, Luna
  sin masa relativa a la Tierra) y sin fase sideral real de rotación en t=0 (ver R18 abajo).

### R16. Cap de sub-step térmico — premisa descartada, no era un bug ✅ REVISADO
- **Preocupación original:** el cap de 256 sub-steps a 0.02 s parecía alcanzable sobre un tick de
  integración >5.12 s.
- **Lo que el código realmente hace:** el `dt` que llega a `StressSolver.ApplyThermalLoads` está
  acotado por `Universe.MaxCoastStep = 2.0 s` — 5.12 s es inalcanzable por un factor de 2.5×. El
  propio sub-step además tiene margen de estabilidad de ~430× (`h < 2c/(4εσT³) ≈ 8.6 s` vs 0.02 s
  real).
- **Veredicto:** no era un bug vivo; construir un clamp de warp para esto habría sido resolver un
  problema inexistente. Se subió igual `MaxSubSteps` 256→2048 (headroom barato) y se agregó
  `ThermalSubstepTests.cs`, que falla si algún cambio futuro sube `Universe.MaxCoastStep` por
  encima de lo que el integrador térmico puede absorber con seguridad.

### R17. Separación de etapas: teletransporte de CoM y momento angular faltante ✅ HECHO
- **Evidencia:** `TriggerStaging` teletransportaba la nave sobreviviente hacia arriba la longitud
  completa de la etapa desprendida (`ActiveVessel.Position += axis * separationHeight`), sin
  ningún offset complementario en el debris — inyectando energía potencial (~70 m en el booster de
  Flight 7) y sin término de transporte `ω × r`, filtrando momento angular respecto del CoM
  combinado. Tres rutas de split inconsistentes (`Stage`, `BreakAtJoint`, `DeployPayload`), una de
  ellas viviendo parcialmente en la capa Godot — por eso el bug era intesteable desde xUnit puro
  (producía velocidad de separación cero).
- **Fix:** helper único `Vessel.ApplyMassSplitKinematics`, usado por las tres rutas: reparte el
  offset geométrico y el impulso de apertura por razón de masa (preserva el hueco exacto del
  renderer: `L·m_d/M + L·m_s/M = L`) y agrega el término `ω × r` faltante. Nueva propiedad
  `PartDefinition.SeparationImpulseNs`, sin poblar en ninguna pieza actual — así todo vehículo
  sigue tomando el mismo fallback de 1.0 m/s que ya usaba el código viejo.
- **Test que habría cazado el bug original:**
  `StageSeparationConservationTests.StagingConservesAngularMomentumAboutTheCombinedCentreOfMass`.
- **Aserción corregida (fijaba el bug):** `StarshipRealismTests.StagingPreservesDetachedStageRigidBodyMotion`
  afirmaba `Assert.Equal(vessel.Position, detached.Position)`; ahora afirma lo que su nombre dice:
  orientación/velocidad angular/cuerpo de referencia compartidos, momento **conservado** (no idéntico).
- **Magnitud en Flight 7:** nave ~1.5×10⁶ kg, booster ~3.3×10⁶ kg. Split viejo: nave +67.5 m,
  booster +0. Split nuevo: nave +46.4 m, booster −21.1 m (el hueco de 70.9 m del renderer no
  cambia). Perturbación relativa contra energía orbital de escala LEO: ~2×10⁻⁵.
- **Harness:** `--ascent --flight7` → `ASCENT_ORBIT_OK` (189×145–192×148 km). `--edl` → `LANDED`
  (inmune por construcción: `BeginReentryDemonstration` sobrescribe posición/velocidad después de
  `TriggerStaging`).

### R18. Radio de estancamiento de Sutton-Graves dependiente de actitud ✅ HECHO
- **Preocupación original:** todo llamador de `ThermalModel.ComputeHeatFlux` pasaba
  `MaximumDiameter / 2` (4.5 m en Starship) sin importar actitud, asumiendo que esto
  **sub**-estimaba el flujo pico de un cilindro en belly-flop.
- **Lo que es realmente cierto:** Sutton-Graves es una correlación de punto de estancamiento
  esférico. Para un cilindro en flujo cruzado (de costado — la actitud real que vuela todo camino
  con contrato), la correlación de línea de estancamiento 2-D (Reshotko–Beckwith) predice ~1/√2
  del valor esférico a igual radio — o sea el radio de casco fijo **sobre**-estima el calentamiento
  de costado ~1.41×, no lo sub-estima. El hueco real está solo en la actitud de morro/cola (radio
  real ~3 m de la punta del cono, no los 4.5 m del casco).
- **Fix, acotado:** nueva `PartDefinition.NoseRadiusM` (3.0 m en `starship_command.json`) +
  `ThermalModel.EffectiveNoseRadius(hullRadius, noseRadius, cosAlpha)`, mezclado en `cos²α` igual
  que el blend de área/Cd de `AerodynamicsModel` — a `cosAlpha = 0` devuelve el radio de casco
  bit-a-bit.
- **Explícitamente NO hecho, y por qué:** la corrección 1/√2 de costado NO se aplicó — enfriaría
  la entrada de panza ~8% (ayuda `PeakStructure < 900 K`) pero angostaría la brecha
  panza-vs-cola que usan los tests de destrucción (`tail − belly > 800 K`). Necesita su propio
  re-baseline medido, no un bundle con este fix de blend por actitud. Queda como follow-up (cita:
  Reshotko & Beckwith, transferencia de calor de línea de estancamiento 2-D/axisimétrica).
- **Verificación medida:** ambos escenarios de `OrbitalReentrySurvivalTests` (belly-first,
  tail-first) reproducidos con instrumentación de `max(|cosAlpha|)` en toda la entrada (~900 s):
  <1e-9 en ambos — ningún número `PeakSkin`/`PeakStructure`/`Damage` existente cambió.
- **Bug preexistente encontrado y dejado a propósito:** la llamada de heat-flux en
  `scripts/VesselRenderer.cs` no pasa argumento de radio, cayendo por defecto a `noseRadius = 1.0`
  m — más agudo que los 3 m declarados de Starship, inconsistente con el resto de call sites.
  Cablearlo en el blend movería un valor de costado (Rn 1.0→4.5, ~mitad de flujo en ese call site)
  — fuera de alcance de un fix aditivo. Follow-up abierto.

### R19. Discontinuidad de drag hipersónico en Mach 5 ✅ HECHO
- **Evidencia:** `AerodynamicsModel.GetMachDragMultiplier` llegaba a 1.05 acercándose a Mach 5 por
  abajo y caía a exactamente 1.0 en Mach 5 — un escalón real de 4.8%, no una transición física.
- **Fix rechazado durante el diseño:** una meseta Newtoniana hipersónica de ~1.7 parecía la
  completación obvia de la curva. Está mal: el coeficiente broadside de `ComputeReentryDrag` ya es
  `cd = 1.5`, que **ya excede** el límite Newtoniano real de flujo cruzado de un cilindro
  (`4/3 ≈ 1.33`). Multiplicar por ~1.7 daría `Cd = 2.55` — por encima del máximo Newtoniano de
  placa plana (2.0), físicamente imposible — y rompería `AerodynamicLiftTests` (`0.2 < L/D < 0.45`,
  que codifica el L/D ≈ 0.3 real de Starship). Ese test tenía razón; la meseta no.
- **Fix real:** rampa lineal solo en la banda `5.0 ≤ Mach < 8.0` (que además es aprox. donde el
  principio de independencia de Mach de Oswatitsch predice que los coeficientes de presión dejan
  de variar con Mach de verdad). Cambio máximo en toda la curva: 5%, confinado a esa banda; fuera
  de ella, bit-idéntico a antes. La meseta en exactamente 1.0 queda documentada en el propio método
  como intencional ("por construcción, no por omisión").
- **Harness:** `--edl` siembra la entrada a ~M6.1, dentro de la banda cambiada, y llegó a `LANDED`
  (6 contactos asentados) en corridas repetidas. `--ascent --flight7` pasa brevemente por la misma
  banda durante el ascenso y llegó a `ASCENT_ORBIT_OK` en cada corrida. Ninguna aserción numérica
  de `OrbitalReentrySurvivalTests`/`AerodynamicLiftTests` necesitó cambiar.

### Backlog abierto dejado por esta auditoría (Jul 2026)
- **R18b.** Corrección broadside 1/√2 de Sutton-Graves — necesita su propio re-baseline medido de
  `PeakStructure`/`tail−belly` antes de aplicarse (ver R18).
- **R18c.** ~~`VesselRenderer` default `noseRadius = 1.0`~~ ✅ cerrado — ahora usa
  `ComputeStagnationHeatFlux` (mismo blend actitud que plasma/lighting).
- **R15b.** Fase orbital J2000 de Júpiter/Saturno (~0.3-0.5° off) — excluida a propósito de
  `EphemerisPhaseTests`, necesita su propia corrección, no aflojar la tolerancia.
- **Earth J2 + WGS84 ellipsoid** ✅ HECHO — `data/bodies/earth.json` arma `j2`,
  `equatorial_radius` y `polar_radius`. `GetGravityAt` es Vallado en el frame equatorial
  (+Z = eje de spin). `GetSurfacePosition` / `GetAltitude` / pads / contacto usan el
  elipsoide, así g polar > g ecuatorial en superficie. Kepler on-rails sigue siendo
  dos-cuerpos (el vessel activo en RK4 sí siente J2). Tests de scheduler prueban Kepler≡RK4
  contra `WithoutOblateness()`. Limitación documentada: el planner lunar Lambert es
  dos-cuerpos, y `RequiresOffRailsPhysics` fuerza RK4+J2 bajo `ThermosphereTopAltitude`
  (1 000 km) para que LEO decaiga (R7); un TLI con perigeo ~185 km no puede usar ese
  arco como prueba de targeting. Isp de Atlas LR-105 lumped 350 s es calibración del
  agregado (no química LOX/RP-1 publicada).
- **Limitaciones conocidas, no tocadas esta pasada** (registradas para no "redescubrirlas" como
  bugs nuevos — detalle completo en `docs/audits/PHYSICS_AUDIT_JUL2026.md` §7): Kepler on-rails
  omite J2 (SSO bajo warp no precesa; el jugador en LEO a ×1 sí). Lambert/Apollo 8 es dos
  cuerpos — coherente con el coast on-rails. Más: sin fase sideral Greenwich en t=0 (longitud
  medida desde un meridiano arbitrario — por eso `BeginReentryDemonstration` reubica manualmente el
  reingreso al lado diurno); sin corrección baricéntrica; Luna en cónica osculante fija (ya
  flagueado en `CLAUDE.md` como "dated lunar ephemerides"); termosfera sin variabilidad solar
  (F10.7/bulge diurno); sin fallo estructural por presión dinámica pura (`GetDynamicPressure` se
  calcula pero nada rompe solo por q); EDL es autopiloto scripteado, no ley de guiado (sin
  modulación de bank para cross-range, sin sitio de aterrizaje objetivo).

---

## OLA 4 — Sistemas / UX (realismo de misión, backlog)

### R11. Sistemas de vida/energía/comms atados a fases — ✅ jugable
- Eclipse → solar a 0 (penumbra proporcional); delay de comms ∝ distancia; LS Idle vs Active+.
- Fases de sistemas: `Idle` / `Active` / `HighLoad` / `Entry` / `PeakHeating` (mapa desde
  `MissionPhase` en `SystemsController.MapMissionPhase`).
- EC: LS por fase + `SystemsPhaseLoads.AvionicsExtraKw` (boost en HighLoad/Entry/Peak).
- Térmica: acoplamiento de flujo aero → cabina (`ThermalSystem` + área por fase).
- Retardo de mando en tierra: `GroundCommandRelay` retrasa stick/throttle del HUD por
  `SignalDelaySeconds`; uplink cae en LOS/blackout; guiado a bordo (Ascent/EDL) no pasa
  por el relay. HUD: `GROUND DELAY` / `BLACKOUT` / `LOS`.
- Tests: `SystemsMissionPhaseTests` (eclipse, delay, LS, thermal aero, relay).
- Archivos: `ExosphereSimulation/Systems/*`, `scripts/SystemsController.cs`,
  `scripts/HUDController.cs`, `scripts/SystemsHUD.cs`.

### R12. Boostback + captura en torre (Mechazilla) — ✅ jugable (Ship + booster + entry burn)
- Catch de la etapa superior (Ship) cerrado: fases `Catch`/`Caught`, cuna de dos pines,
  `MissionPhase.CAUGHT`.
- Booster (`BoosterReturnController` + `BoosterReturnGuidance`): tras `VesselStaged`
  arma boostback (13 motores, corta cuando el componente outbound &lt; 100 m/s o reserva),
  costa, **entry burn** a &lt;5 km (13 motores) → catch a &lt;1.5 km (3 motores), multi-vessel
  cradle/chopsticks, pines en `super_heavy_booster` / V3. HUD: línea `BOOSTER …` bajo la
  guía del Ship (no roba `MissionPhase`). Δv budget 6%→2.5% anclado a banda IFT
  (800–1800 m/s) en xUnit. Evidencia: `BoosterReturnGuidanceTests`, `CatchContactTests`.
- Pendiente fino opcional: telemetría de boostback en vuelo real vs IFT wall-clock,
  divert-to-Gulf abort path.

---

## Orden de ejecucion actual
1. No reabrir R1-R4, R5/R5b/R5c, R8-R10, R13 ni R15-R19 salvo regresion demostrada por telemetria/harness.
2. Backlog fisico R5 cerrado: R5/R5b/R5c/R5d ✅.
   (R6 lift/AoA ✅, R7 termosfera/decay ✅, B2 hot-stage overlap ✅)
3. Backlog de la auditoria Jul 2026 (motores/staging/Tierra/reingreso, ver seccion "OLA JUL2026"
   arriba): R18b correccion broadside 1/√2 de Sutton-Graves (necesita re-baseline propio), R18c
   `VesselRenderer.cs` con `noseRadius=1.0` por defecto, R15b fase J2000 de Jupiter/Saturno. Mas
   limitaciones conocidas sin tocar: Kepler on-rails omite J2 (el jugador RK4 en LEO si lo
   siente), sin fase sideral Greenwich en epoch, sin correccion baricentrica,
   Luna en conica fija, termosfera sin variabilidad solar, sin fallo estructural por presion
   dinamica pura, EDL sin ley de guiado real.
4. Backlog mision/sistemas: R11 ✅; R12 ✅ (Ship catch + booster boostback/entry/catch + HUD).
5. Backlog visual vive en `PLAN_VISUAL_REALISM.md`; no duplicar aqui la auditoria visual.

## Método de verificación (para cada ola)
- Build 0/0 (sim + juego) + `dotnet test` verde; tests nuevos donde se toque física.
- **Re-correr el harness de telemetría headless** (autoload temporal `_*Shot`, limpieza obligatoria)
  y comparar el perfil contra los números reales de esta auditoría.
- Cambios de física en sim → `physics-reviewer` en contexto fresco antes de mergear.
- Worktrees parten de base que puede ser vieja → diff de 3 puntos + rebase al integrar (gotcha conocido).
