# Handoff F4 → F5 (rama `claude/refine-local-plan-90r917`)

Base: `d130a70` (checkpoint F4 de `codex/f4-completion`). Este commit contiene el cierre de F4
en producción y el arranque de F5. Reemplaza a `HANDOFF_F4_completion.md` como estado vigente.
Sin merge a `main`. Los cambios de `.atl/` no se tocaron.

## Estado de verificación (léelo primero)

- CI completa (`tools/ci_check.sh`) **antes** de la corrección de amortiguación de alabeo:
  1428/1430; los únicos 2 fallos eran Flight 14 (`attitude-settling-envelope-exceeded`).
- Corregido después (`rollDampingGain`), con verificación dirigida: pruebas Flight 14 afectadas,
  Saturn V / Apollo 11 (19/19), compuerta histórica F4 y analíticas F4 en verde.
- La corrida completa CI3 (que incluye esa corrección, los datos de Saturn V y la compuerta
  histórica) **estaba aún ejecutándose al hacer este push**, por petición del usuario. El
  próximo agente debe correr `bash tools/ci_check.sh` primero y corregir lo que falle.
- Estándar esperado: 0 warnings, 0 errores, todas las pruebas en verde.

## Lo que se hizo — F4 (cerrado, pendiente de confirmar CI completa)

### Geometría de masa compartida
- `Parts/PartMassGeometry.cs` (nuevo): una sola descomposición por pieza: estructura seca
  (`dry_mass_center_y_m`, `dry_gyration_m` → cilindro equivalente), columna líquida asentada por
  recurso sobre el fondo plano equivalente del tanque declarado (`propellant_tanks`;
  λ = ρπR², h = m/λ, centroide = fondo + h/2, k² transversal = (3R² + h²)/12), cilindro
  uniforme si no hay tanque, y puntos de `header_tanks`.
- `PartGraph.BuildMassProperties` y `Part.MassCenterOffsetYM` usan esa descomposición.
- `PartCatalog` exige procedencia para `dryGyration`, `propellantTanks`, `ventOutlet` y
  `nozzleExit`, y valida los tanques (`ValidatePropellantTanks`).

### Salidas y flujo interno
- Salida de tobera: punto de empuje menos `NozzleExitAxialOffsetM` del modelo de motor, normal
  = −dirección, radio = `NozzleExitDiameterM`/2 (`Part.TryGetNozzleExit`).
- `vent_outlet` por pieza (Starship), toberas RCS desde los recibos, y salida de perturbación
  (`Vessel.DisturbanceOutletBodyM`; Gemini 8 `StartStuckThruster` usa el propulsor declarado).
- Convención de flujo interno: punto de pérdida (superficie libre o centroide) → salida del
  almacén (fondo) → conducto recto a la salida. p_rel = Σ q(e − t) cumple continuidad exacta.

### Transporte lineal/angular, cambios de flujo y CoM
- `Physics/OpenSystemTransport.cs` (nuevo): F = m(c̈ + 2ω×ċ) − ṗ_rel − 2ω×p_rel; torque
  −İω − J_out ω − ω×h_rel − ḣ_rel − ċ×p_rel; tensor de jet damping D = İ + J_out.
- `BodyMassDistribution.Sample` con centroides móviles; `FixedShapeMassComponent` con columnas.
- `Physics/VariableMassStepModel.cs` (nuevo): construye el modelo por paso desde la ventana de
  fondos (grupos por pool de alimentación, otros motores, venteo, RCS, perturbación) o devuelve
  una razón de fallback (`Vessel.LastVariableMassStatus`).
- Cortes de flujo: P y H continuos (impulso de golpe de ariete) en nudos internos y entre pasos
  (`Vessel.TransportRelativeMomentumBody`/`...AngularMomentumBody`, guardados en `SaveGameV2`).

### Producción
- `Universe.VariableMassTransportEnabled` (por defecto `true`); camino acoplado
  `IntegrateVesselVariableMass`. Legacy split y agua quedan en masa congelada.
- Física nueva visible: un líquido drenando gana alabeo (D_yy < 0). Los controladores que
  deben sostener el alabeo pasan `rollDampingGain` (`AttitudeGuidance.ComputeAxisPointingCommand`;
  Flight 14 ascenso y quemado orbital).
- Pruebas de agotamiento que comprueban la frontera congelada desactivan el transporte.

### Datos y procedencia
- Saturn V AS-506: S-IC/S-II/S-IVB con centro seco, giración, tanques (fondos planos de igual
  volumen, NASA SP-106) y headers de líneas de succión del S-IC (masas FER bajo tanque).
  `data/provenance/saturn5_as506_mass_geometry_1969.json`; estaciones de tanques S-II/S-IVB
  marcadas como estimadas.
- Starship: `vent_outlet` estimado; convenciones en `data/provenance/rigid_body_f4_outlets_2026.json`.
- `docs/physics/estimates_register.md` regenerado.

### Compuertas F4
- Analíticas: `SettledColumnMassTests` (5), `OpenSystemTransportTests` (4: continuidad,
  remoción co-rotante, ecuación del cohete con impulsos de alimentación, ley de jet damping
  integrada de forma independiente).
- Histórica: `SaturnVJetDampingHistoricalTests` contra el FER AS-506 (MPR-SAT-FE-69-9, tablas
  20-10 y 5-I) y el plano MSFC/Boeing (salida F-1 a −2.930 m). Resultado: liberación CG 30.244
  vs 30.276 m, J 14.471e6 vs 14.388e6; corte CG 46.956 vs 46.453 m, J/q +2.0 %; ΔI +5.6 %.
  No usa el coeficiente del actuador SPS.
- Documentación: `fidelity_gates.md`, `PHYSICS_MODEL.md`, `variable_mass_torques.md`,
  `rigid_body_fidelity_plan.md`, `ROADMAP.md` y `CLAUDE.md` marcan F4 cerrado.

## Lo que se hizo — F5 (iniciado)

- `ExosphereSimulation/Physics/PropellantSlosh.cs`: primer modo lateral por tanque cilíndrico
  (NASA SP-8009 / SP-106): ω₁² = (ξ₁a/R)tanh(ξ₁h/R), m₁/m, altura de m₁, L₁ = a/ω₁², m₀ fijo
  que conserva el centroide de la columna (m₀ + m₁ = m, sin masa añadida).
  `SloshPendulumCoupling` (portador plano + péndulo: momento lateral y derivadas). Compila.
- Borrador de pruebas en `docs/physics/drafts/PropellantSloshTests.cs.draft` (fuera del proyecto
  de pruebas a propósito; aún no compila ni tiene filas en `fidelity_gates.md`): raíz de J₁′,
  conservación de masa/centroide, límites profundo y somero, momento conservado y frecuencia
  libre-libre, e histórica AS-501 Fig. 11-15.
- Fuentes reunidas: SP-106 (NTRS 19670006555), SP-8009 (NTRS 19690005221), FER AS-501
  (Fig. 11-15: LOX ≈0.35 Hz a 0 s → ≈0.51 Hz a ~115 s; combustible ≈0.35 → ≈0.46 Hz a ~100 s →
  ≈0.42 Hz a 130 s), FER AS-504 (S-II LOX 0.5–1.0 Hz, LH2 0.2–0.4 Hz, ζ LH2 ≈ 0.02; S-IVB
  ≈0.5–0.65 Hz).

## Lo que falta

### F4
1. Correr `bash tools/ci_check.sh` completo y corregir lo que falle.
2. Actualizar o retirar `HANDOFF_F4_completion.md`, `docs/physics/f4_completion_worklog.md` y
   `docs/physics/variable_mass_resource_adapter.md`, que aún describen el estado WIP.
3. Brechas de datos documentadas para F6: CG de la nave espacial ~2.3 m alto frente a FER
   91.658 m, estaciones de tanques S-II/S-IVB estimadas, flujo del F-1 en altura, y AS-503 sin la
   misma geometría.

### F5
1. Mover el borrador a `ExosphereSimulation.Tests/PropellantSloshTests.cs` y arreglarlo. La
   prueba histórica usa un `LastCoupled6DofSpecificForceWorld` que no existe; usar
   `vessel.GetProperAcceleration(earth)` proyectada sobre el eje (hace falta pasar el cuerpo
   desde `SaturnVAscentFlight.Fly` o resolverlo en la prueba). Confirmar también que
   `FindPropellantTank` y los nombres de propiedades de `PropellantTankDef` coinciden.
2. Registrar las filas F5 (analytic/historical, estado "open") en `docs/physics/fidelity_gates.md`
   para que `FidelityGateRegistryTests` pase.
3. Procedencia: registros para las constantes SP-8009/SP-106 y la curva AS-501 en
   `data/provenance/`, y regenerar `docs/physics/estimates_register.md`.
4. Documento de diseño `docs/physics/propellant_slosh.md`: péndulo por tanque extraído de la
   masa de la columna (m₀ + m₁ = m, CG conservado), cómo preservar la inercia rígida, ecuaciones
   de acoplamiento en el 6-DoF, amortiguación (Miles con deflectores / ζ medido), límite de
   costa no asentada (a ≤ 0) y estado en save/load.
5. Integración en producción: estado del péndulo por tanque en el integrador acoplado, sin masa
   ficticia; compuerta histórica de frecuencias AS-501 y luego amplitudes/amortiguación.
6. CI completa, commit como `Sebas <sebastianvelace@gmail.com>` (autor y committer, sin
   trailers) y push.
