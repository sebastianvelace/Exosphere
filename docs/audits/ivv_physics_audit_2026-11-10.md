# Auditoría IV&V de física, astrodinámica y numérica — 2026-11-10

Estado del repositorio auditado: `main` en `7518761`.

## 0. Cómo leer este documento

**Método.** Auditoría de solo lectura. Para cada subsistema: (1) derivé el modelo correcto
de forma independiente, (2) traduje el código a ecuaciones, (3) comparé término a término,
(4) estimé a mano la magnitud del desajuste en un caso concreto. **No se ejecutó nada**: ni
tests, ni builds, ni scripts, ni cálculos con código. Todas las cifras son aritmética manual
y deben tomarse con ±10–20 %.

**Qué significa cada campo.**

- *Severidad*: alta = puede cambiar el resultado de una maniobra o un contacto; media =
  error visible en una magnitud de misión (km, m/s, grados); baja = por debajo de las
  tolerancias históricas actuales, pero real.
- *Confianza*: alta = derivación cerrada y línea de código leída; media = derivación
  cerrada pero depende de un dato no leído; baja = hipótesis.
- *Escala*: cómo crece el error con `dt`, warp o duración.
- Las cifras marcadas **(de memoria)** son constantes publicadas que no verifiqué contra la
  fuente en esta sesión; hay que confirmarlas antes de usarlas en una prueba.

**Reglas del repo que aplican a cualquier corrección.** No aflojar tolerancias históricas;
todo dato no publicado necesita registro en `data/provenance/`; cada corrección de física
compartida lleva prueba xUnit; las pruebas de paridad comparan integradores con aero
distribuida apagada.

**Orden de trabajo recomendado**: sección 9.

---

## 1. Cobertura

| Área | Archivos | Estado |
|---|---|---|
| Integración off-rails y marcos | `Universe.cs` 2566–3351, 3489–3793; `Universe.PropellantSlosh.cs` | Leído |
| Integrador rígido | `Integrators/RigidBody6DofIntegrator.cs` | Leído |
| Masa variable | `Physics/OpenSystemTransport.cs`, `VariableMassRotationalDynamics.cs`, `FixedShapeMassComponent.cs`, `BodyMassDistribution.cs`, `VariableMassStepModel.cs`, `RigidBodyForceEvaluator.cs` | Leído |
| Slosh | `Slosh6DofDynamics.cs`, `SloshStepModel.cs`, `PropellantSlosh.cs`, `SloshMomentumTransfer.cs` | Leído |
| Slosh (resto) | `SloshKineticSystem.cs`, `SloshParameterSchedule.cs`, `SloshVariableMassDynamics.cs`, `SloshTransportCorrection.cs`, `Integrators/Slosh6DofIntegrator.cs` | **No leído** |
| Entorno | Todo `Environment/` salvo `EnvironmentContext.cs` y `EnvironmentProductFile.cs` | Leído |
| Geodesia | `CelestialBody.cs` 225–555 | Leído |
| Cuerpo (resto) | `CelestialBody.cs` fuera de 225–555 (atmósfera por capas, `DatedBasisAt`, `PlanningElementsAt`) | **No leído** en esta pasada |
| Órbitas | `OrbitalElements.cs`, `Integrators/KeplerPropagator.cs`, `Math/MathUtils.cs` | Leído |
| Órbitas (resto) | `Integrators/RK4Integrator.cs`, `CoastingAttitudeIntegrator.cs`, `Navigation/TrajectoryPrediction.cs` | **No leído** |
| Navegación | `LambertSolver.cs`, `LunarTransferPlanner.cs`, `TransearthInjectionPlanner.cs`, `LunarLaunchWindow.cs`, `CapturePlanner.cs`, `Flight/TranslunarCorrection.cs` | Leído |
| Navegación (resto) | `RendezvousPlanner.cs`, `ProximityApproach.cs`, `Flight/LambertGuidedBurn.cs`, `PeriapsisCaptureBurn.cs`, `ProgradeEnergyBurn.cs`, `ApsidesTargetBurn.cs`, `LunarPoweredDescent.cs`, `CapsuleEntryGuidance.cs`, `TransearthCorrection.cs` | **No leído** |
| Aerodinámica | `Physics/CrossflowDrag.cs`, `DistributedAerodynamics.cs`, `BodyFlapAerodynamics.cs` | Leído |
| Aerodinámica (resto) | `AerodynamicsModel.cs` (modelo romo/trimado, calentamiento), `DragModel`, buoyancy/agua | **No leído** |
| Control | `Vessel.cs` 455–486, 936–1031, 1515–1700; `Parts/PartGraph.cs` 1130–1402 | Leído |
| Control (resto) | `Physics/RcsAllocation.cs`, `Flight/ControlAuthority.cs`, `SasHold`, resto de `Vessel.cs` | **No leído** |
| Contacto | `Physics/SurfaceContactSolver.cs`, `GravityGradient.cs` | Leído |
| Geometría de masa | `Parts/PartMassGeometry*.cs`, `Part.cs` (geometría de empuje, signo del gimbal) | **No leído** |
| Datos | `data/bodies/earth.json`, `moon.json` (solo GM, J2, radios, SOI) | Leído parcial |

Lo no leído queda como trabajo de auditoría pendiente (sección 8).

---

## 2. Resumen ejecutivo

| ID | Hallazgo | Severidad | Confianza | Por defecto |
|---|---|---|---|---|
| A-01 | La cuna de captura se evalúa en la época de fin de paso mientras las etapas RK4 están en épocas intermedias | Alta | Alta | **Corregido** |
| A-02 | Marco N-cuerpos en mundo Kepler frente a los raíles | — | Alta | **Retirado**: límite inherente de los raíles, no defecto |
| A-03 | Ventana lunar y TLI: sin regresión nodal J2, Lambert cerca de 180°, Luna osculante con μ_T solo | Media | Media-alta | Activo |
| A-04 | Turbulencia von Kármán: realizaciones independientes en filas duplicadas a 1 mm, campo no solenoidal | Media | Alta | Apagado |
| A-05 | Líquido rígido en balanceo: sobre-amortiguamiento/giro por chorro en etapas con salida cerca del eje | Media-baja | Media | Activo |
| A-06 | Asignación de gimbal con brazo respecto al CG propio cuando hay pila acoplada | Media-baja | Media | **Corregido** |
| A-07 | Columnas de propelente siempre asentadas hacia popa (`Vector3d.Up`) | Media-baja | Alta | Activo |
| A-08 | Inercia rígida del líquido en cabeceo no reducida (falta I₀ de SP-106) | Baja-media | Media | Activo |
| A-09 | `OrbitalElements`: ángulos por `acos` y banda parabólica | Baja | Alta | **Parcial** (ángulos corregidos) |
| A-10…A-22 | Menores (sección 5) | Baja | Variable | — |

Nota global dada antes: 7,5/10 (modelos 9, proceso 9, consistencia entre subsistemas 6,
guiado 6, cobertura de misión 6,5). Esta segunda pasada no la cambia: los archivos nuevos
(`BodyMassDistribution`, `SloshMomentumTransfer`, `RigidBody6DofIntegrator`, geodesia,
`GravityGradient`) son correctos; los hallazgos nuevos vuelven a ser de **consistencia entre
subsistemas**, no de ecuaciones mal escritas.

---

## 3. Hallazgos principales

### A-01 — Cuna de captura evaluada en la época equivocada dentro del RK4

> **Estado: corregido.** `Universe.CatchCradleAtStage` lleva la cuna a la época de la etapa
> con su velocidad relativa al cuerpo, en los cuatro caminos de integración. Prueba:
> `CatchContactTests.RotatingCradleSupportIsIndependentOfTheStep`. La medida confirmó y
> agravó la estimación: los pines están a ±4,4 m del centro de la cuna, así que con
> dt = 0,02 s solo la etapa final caía dentro de la envolvente y el vehículo atravesaba la
> cuna (2,67 m de diferencia de compresión frente a dt = 0,005 s; ahora 0,155 m en ambos,
> el valor analítico m·g/2k).

- **Severidad** alta · **Confianza** alta · **Activo** en toda captura Mechazilla.
- **Código**: `Universe.cs:3247` y `3275` (`GetCatchTargetPositionAt(evaluationTime)`),
  definición en `Vessel.cs:185–192`; llamadas en `Universe.cs:2663`, `2877`, `3057` y
  `Universe.PropellantSlosh.cs:132`. Envolvente: `SurfaceContactSolver.cs:43–46`,
  `CatchCaptureRadiusM = 5.0` en `Universe.cs:3242`.

**Modelo correcto.** La integración off-rails trabaja en el marco trasladante del cuerpo de
referencia con los cuerpos congelados en `t+dt`. Un candidato RK4 en la etapa de época
τ ∈ {0, dt/2, dt/2, dt} tiene posición relativa al cuerpo en τ. La cuna, fija a la
superficie, debe evaluarse en la misma época:

```
r_cuna_rel(τ) = r_cuna_rel(t+dt) − v_cuna_rel · (dt − τ) + O(ω² R (dt−τ)²)
v_cuna_rel    = ω × r_cuna_rel
```

**Qué hace el código.** Evalúa la cuna en `evaluationTime` = fin de paso para todas las
etapas. El candidato de la etapa k1 (época t) se compara con una cuna que está
`v_rot·dt` más al este.

**Magnitud.** Starbase, φ ≈ 26°: v_rot = ωR cos φ ≈ 7,292e-5 · 6,378e6 · 0,899 ≈ 418 m/s.
Con dt = 0,02 s el desfase horizontal por etapa es 8,4 / 4,2 / 4,2 / 0 m. El radio de
captura es 5 m:

- k1 queda fuera de la envolvente (8,4 m > 5 m) → fuerza de soporte nula en esa etapa.
- k2 y k3 quedan dentro solo si el error real hacia el oeste es < 0,8 m.
- Resultado: el soporte efectivo es 5/6 del nominal en el mejor caso y 1/6 en cuanto el
  pin tiene ≥ 0,8 m de error al oeste. El término de corrección cuadrático
  ω²R(dt)²/2 ≈ 7e-6 m es despreciable.

**Escala.** Lineal en dt. Con dt = 1/60 s: 7,0 / 3,5 / 3,5 / 0 m (k1 sigue fuera). A dt
≤ 0,0119 s k1 entra en la envolvente con error cero.

**Corrección propuesta.**
`cradle_rel(τ) = cradle(t+dt) − (v_cradle − v_ref)·(dt − τ)`, pasando la época de la
etapa (el evaluador ya recibe `time`). Alternativa: muestrear la cuna con
`GetSurfacePositionAtTime` en la época de la etapa y restar el estado del cuerpo en esa
misma época (`BodyStateAt`).

**Prueba de regresión.** Vehículo en reposo relativo sobre la cuna con la carga estática:
la fuerza de contacto de las cuatro etapas debe ser igual hasta 1e-9 relativo, y el
equilibrio estático debe mantenerse 10 s con dt = 0,02 s y con dt = 0,005 s con la misma
penetración (independencia de dt).

**Pregunta abierta.** ¿El contacto con el suelo (`FromSphere`, `SurfaceContactSolver.cs:16–22`)
sufre lo mismo? Ahí la superficie es un plano infinito que se muestrea por el punto de
consulta, de modo que el error horizontal no cambia la penetración; solo la velocidad
tangencial de la superficie usa la orientación de fin de paso (error ω·dt·v_rot ≈ 6e-4 m/s,
despreciable). No hay hallazgo, pero conviene cubrirlo en la misma prueba.

---

### A-02 — Marco N-cuerpos en mundo Kepler frente a los raíles (retirado como defecto)

> **Estado: retirado tras releer el código; no se cambia nada.** La primera lectura lo
> planteó como un error del marco. No lo es: es el límite inherente de mover los cuerpos
> sobre raíles Kepler, y la corrección que proponía habría empeorado la física.

- **Código**: `Universe.cs:3356–3371` (`GetReferenceBodyRailAcceleration`: aceleración del
  marco = atracción de todos los demás cuerpos sobre el de referencia), frente a
  `Environment/KeplerEphemeris.cs:34–36` (cada cuerpo sobre una cónica con μ del padre).

**Qué hace y por qué es correcto.** En el marco de un cuerpo B la nave integra
`a_rel = g_todos(nave) − Σ_{j≠B} g_j(B)`: gravedad de B más las **mareas** de los demás.
Eso es la dinámica relativa físicamente correcta tanto en el marco Tierra como en el marco
Luna. La alternativa "coherente con el raíl" (restar solo la aceleración del raíl, la
cadena de padres anterior a E5) deja a una nave en LEO la atracción lunar completa,
3,3e-5 m/s², en vez de su marea de ~1e-6 m/s²: unos 120 km de deriva al día. El comentario
del código lo documenta y es la razón del cambio.

**Lo que queda, y no es corregible con raíles.** La Luna del raíl no cae libremente: su
aceleración relativa a la Tierra es `−μ_T r/r³`, sin el término `μ_L` ni la marea solar.
Por eso la aceleración relativa nave–Luna calculada en el marco Tierra y en el marco Luna
difiere en `g_L(T) + g_S(T) − g_S(L)` ≈ 2–6e-5 m/s² (3–5 % de la gravedad lunar en el
borde del SOI). No hay discontinuidad de estado ni error de integración: es el error de
efemérides del raíl Kepler visto desde la nave. Usar `μ_T + μ_L` en el raíl de la Luna
quitaría el primer término pero no la marea solar, y rompería el comportamiento "bit a
bit" del mundo Kepler y sus calibraciones.

**Conclusión.** Las misiones que necesitan esta fidelidad ya vuelan el mundo fechado
(DE440), donde marco y efemérides coinciden a ~1e-9
(`DatedPlanningFrameTests.IntegrationFramesAccelerateAsTheEphemeris`). Para el mundo
Kepler queda como límite conocido.

---

### A-03 — Planificación lunar: tres aproximaciones que se suman

- **Severidad** media · **Confianza** media-alta.

**(a) Regresión nodal J2: depende de cómo se vuele la órbita de aparcamiento (replanteado).**
`Navigation/LunarLaunchWindow.cs:62–64` fija el plano con el pad y la Luna en la llegada,
sin regresión nodal. `Ω̇ = −(3/2)·n·J2·(R/a)²·cos i ≈ −1,53e-6 rad/s` (a = 6 563 km,
i = 32,5°), es decir −0,87° en los 9 856 s hasta el TLI y hasta 0,47° fuera de plano.

> **Corrección a la primera lectura.** Atribuí a esto el 0,44° que cita
> `PHYSICS_MODEL.md`. No es eso: ese 0,44° es el residuo de guiado entre el plano volado y
> el de la ventana en la inserción (`LunarLaunchWindowTests`). La coincidencia numérica era
> casual.

El problema real es otro: **los raíles Kepler son de dos cuerpos** (`CelestialBody.J2`:
"Kepler-on-rails remains two-body"), así que el nodo regresa 0,87° si el aparcamiento se
vuela en tiempo real (RK4 con J2) y 0° si se vuela con warp. Compensar J2 en la ventana
arreglaría un caso y estropearía el otro. La corrección de fondo es dar a los raíles las
tasas seculares de J2 (Ω̇, ω̇, Ṁ) para que el resultado no dependa del warp; es un cambio
de propagación con impacto en todas las órbitas bajas con warp y en los saves, y queda
como trabajo propio, no como parche de la ventana.
*Prueba (cuando se haga)*: misma órbita de aparcamiento 9 856 s en RK4 y en raíles; el
nodo debe coincidir a 0,01°.

> **Ejecución de la TLI (hallado al trabajar A-03): corregido.** La primera corrección de
> medio curso costaba 140,7 m/s. La causa no era el plano ni la marea solar: la TLI se volaba
> prograda hasta la energía planificada, y en un encendido de ~570 s eso giraba la línea de
> ápsides 3,3° (llegada a 23 654 km del objetivo, 324 m/s de Lambert justo tras el corte),
> sumado a 115 km de deriva J2 del aparcamiento respecto a la cónica del plan.
> `Flight/TranslunarInjection` la vuela como `LambertGuidedBurn` al punto de llegada; la
> corrección de medio curso baja a 2,7 m/s. Prueba: `TranslunarInjectionScenarioTests`
> (límite 10 m/s).

> **(b) corregido dentro del guiado.** `LambertGuidedBurn.ConditionedAim` proyecta el punto
> objetivo sobre el plano instantáneo de la nave dentro de 10° de media revolución, con peso
> 1 − (sin η / sin 10°)². Antes, la TLI guiada pedía 4 km/s fuera de plano a 179,7°.

**(b) Lambert cerca de 180°.** `Navigation/LambertSolver.cs:71–78`. Si la transferencia
subtiende π − η y el objetivo está ε fuera del plano, la solución de dos puntos exige una
componente normal `Δv⊥ ≈ v·sin ε / sin η`. Con v = 10,8 km/s, ε = 0,44°, η = 5°:
≈ 960 m/s. El barrido (60–180 muestras por órbita) evita el pico por fuerza bruta, pero
el mínimo encontrado depende del muestreo.
*Corrección*: resolver en el plano de la órbita de aparcamiento (proyectar el objetivo) y
tratar el fuera-de-plano como corrección a medio camino, o excluir |η| < 10°.

**(c) Luna osculante con μ_T solamente y sin marea solar.**
Elementos osculantes en la llegada propagados hacia atrás con μ_T: error 44–80 km en las
últimas 14,3 h (la prueba `DatedPlanningElementsOsculateAtTheirInstant` ya documenta
100–2 000 km a tres días). La marea solar sobre la cónica translunar desplaza la llegada
≈ 500 km (½·a_marea·t², con a_marea ≈ 1,5e-5 m/s² medio y t = 2,6e5 s). *Hipótesis*:
esto explica parte de los ~140 m/s de la primera corrección de medio curso.
*Corrección*: iterar el objetivo de Lambert contra una propagación numérica (shooting de
1–2 iteraciones) en lugar de la cónica.

> **(d) corregido.** `TranslunarCorrection.Create` tiene una sobrecarga que devuelve el
> error de perilunio predicho (NaN sin encuentro); el director histórico lo registra y
> `TranslunarInjectionScenarioTests` exige convergencia en el caso nominal y que un
> objetivo inalcanzable no pase por resuelto.

**(d) No convergencia sin señal (precisado).** `Flight/TranslunarCorrection.cs:76–91`: si
la predicción deja de dar encuentro (error NaN) o se agotan las 12 iteraciones de la
secante, el bucle sale y se devuelve igualmente un encendido apuntado al último
desplazamiento, sin indicar que no convergió. El lado de paso sí se conserva (l. 86–88);
lo que escribí sobre elegirlo por mínimo Δv no aplica a este archivo.
*Corrección*: devolver el error de perilunio predicho (NaN sin encuentro) para que el
llamador lo registre o repita, y prueba que fuerce el caso sin encuentro.

---

### A-04 — Turbulencia von Kármán (apagada por defecto)

- **Severidad** media · **Confianza** alta · `Environment/VonKarmanTurbulence.cs`.

1. **Filas duplicadas.** La tabla tiene filas separadas 1 mm (304,8 / 304,801 m y
   762 / 762,001 m) para representar discontinuidades de la especificación. El código
   genera una realización independiente por fila y mezcla entre ellas: al cruzar el
   milímetro la velocidad salta con rms `√(σ₁² + σ₂²)` ≈ 5,9 y 7,2 m/s (severo). Debe
   compartirse la misma semilla/fase a ambos lados y discontinuar solo σ y L.
2. **Divergencia no nula.** Las componentes se normalizan por separado con (σ_h, L_h) y
   (σ_w, L_w); el campo resultante tiene `∇·u ≈ (σ_w/L_w − σ_h/L_h)·∂û_z/∂ξ_z`:
   −15 % relativo a 10 km (6,28e-3 frente a 5,45e-3 s⁻¹), ≈ 80 % a 50 km. Para fuerzas
   puntuales no importa; para gradientes a lo largo del vehículo, sí.
3. **Mezcla cos/sin** entre filas: `L_ef = cos²·L_i + sin²·L_{i+1}` conserva la varianza
   pero no el espectro.

*Prueba*: continuidad de u(h) a través de 304,8 m y 762 m para 100 semillas (salto <
1e-3·σ); varianza y espectro longitudinal frente a la forma cerrada a media altura entre
filas.

---

### A-05 — Líquido rígido en balanceo

- **Severidad** media-baja · **Confianza** media · `Physics/FixedShapeMassComponent.cs:102`
  (inercia axial ½R² para la columna líquida).

**Modelo correcto.** Un líquido de baja viscosidad en un tanque axisimétrico liso no
participa en la rotación de balanceo en la escala de tiempo de un encendido (tiempo de
spin-up de Ekman ≫ segundos). Su inercia axial efectiva es ≈ 0 (salvo deflectores) y el
flujo saliente no transporta momento angular axial del casco.

**Qué hace el código.** Trata el líquido como sólido: I_axial incluye ½ m_liq R² y la
ecuación de transporte da `ω̇/ω = q·(R² − a²)/(2I)` con a el radio de salida. Es el "giro
al vaciar" que CLAUDE.md documenta (los controladores pasan `rollDampingGain`).

**Magnitud.** S-IVB: q ≈ 248 kg/s, R = 3,3 m, a ≈ 1 m, I ≈ 7e5 kg·m² (estimación mía):
1,75e-3 s⁻¹ → factor 1,8 en 347 s. En el S-IC casi se cancela (motores exteriores a
3,9 m ≈ R). El efecto existe en el modelo y no en el vehículo real.

**Corrección propuesta.** Fracción de participación axial del líquido por tanque
(`roll_participation`, 0 para tanque liso, con procedencia) aplicada a la vez a la
inercia axial y al momento angular transportado. Puerta: `SaturnVJetDampingHistoricalTests`
no debe cambiar en cabeceo/guiñada.

---

### A-06 — Gimbal asignado respecto al CG propio con pila acoplada

> **Estado: corregido.** `SolveDifferentialGimbal` y `GetZeroGimbalEngineTorque` aceptan el
> punto de referencia del par y `Vessel.ApplyAttitudeActuators` les pasa el centro de la
> pila acoplada, igual que al RCS. Prueba:
> `Apollo11SpsBurnScenarioTests.SpsGimbalMomentIsTakenAboutTheDockedStackCentre`. Medido en
> CSM+LM con el SPS: la pila giraba a 5,06e-3 rad/s cuando el par pedido correspondía a
> 2,26e-3 (×2,24); ahora 2,36e-3.

- **Severidad** media-baja · **Confianza** media.
- **Código**: `Parts/PartGraph.cs:1315` (`SolveDifferentialGimbal` usa brazos respecto a
  `CenterOfMass` propio), también `1229` (`GetTotalTorque`) y `1254`
  (`GetZeroGimbalEngineTorque`). El RCS sí usa el centro de la pila
  (`Vessel.cs:465`, `RcsTorqueReferenceLocal`). La inercia sí se corrige:
  `Vessel.cs:1551, 1558` dividen por `DockedInertiaScale()` y `ControlTorqueFor`
  (`1567–1570`) multiplica por la inercia propia, de modo que el par pedido es
  `I_pila·α`. El par *real* se traslada bien al centro compuesto en
  `RigidBodyForceEvaluator.cs:131–133`.

**Desajuste.** El asignador cree que una deflexión δ da `τ = r_propio × F(δ)`; la pila
recibe `r_pila × F(δ)`. Ganancia de lazo multiplicada por `|r_pila|/|r_propio|`.

**Magnitud.** CSM+LM: el propio código cita CG del CSM en X_A 934 in y CG de la pila en
1 038 in (`Vessel.cs:456`). Con el plano de gimbal del SPS en X_A ≈ 833 in **(de
memoria)**: brazos 2,57 m y 5,2 m → ganancia ×2,0. Además el par de trim a gimbal cero
(`GetZeroGimbalEngineTorque`) se calcula respecto al CG equivocado si la línea de empuje
no pasa por el CG de la pila: con desplazamiento lateral del CG de la pila de 5 cm y
F = 91 kN, 4,5 kN·m sin compensar ⇒ α ≈ 4,5e3/5e5 ≈ 0,5 °/s² hasta que el lazo de tasas
lo corrige.

**Escala.** Independiente de dt. Solo en encendidos acoplados (LOI, TLI no — ahí manda
el S-IVB —, correcciones translunares con SPS).

**Corrección.** Pasar a `SolveDifferentialGimbal`, `GetTotalTorque` (cuando alimenta al
control) y `GetZeroGimbalEngineTorque` la misma referencia que usa el RCS:
`DockedStackCenterOfMassLocal ?? CenterOfMassOffsetLocal`.
*Prueba*: CSM+LM acoplados, escalón de tasa de 1 °/s con SPS encendido; la respuesta en
lazo cerrado debe igualar la del CSM solo escalada por inercia (misma constante de tiempo
±5 %).

---

### A-07 — Propelente siempre asentado hacia popa

- **Severidad** media-baja · **Confianza** alta · `Physics/VariableMassStepModel.cs:232`
  (columnas a lo largo de `Vector3d.Up`), cabeceras como masas puntuales (`98–99`).

El centro de masa y la inercia del líquido suponen superficie libre normal al eje y
asentada en el fondo de popa. Es correcto bajo empuje axial. No lo es en: (i) barriga
(belly-flop), donde la aceleración propia es lateral y el líquido residual se va a la
pared; (ii) frenado aerodinámico con el morro por delante sin empuje (aceleración propia
hacia proa: el líquido va al domo delantero); (iii) costa en 0 g.

**Magnitud.** Starship en descenso: los tanques principales están ventilados y se
aterriza con cabeceras (puntuales, correctas). Súper Heavy en retorno con ~5 % de
propelente (≈ 170 t) en una columna de ~3,5 m en un tanque de ~40 m: bajo deceleración
aerodinámica el líquido real iría 35 m hacia proa. Desplazamiento del CG ≈
170·35/(200+170+…) ≈ 10–15 m sobre un vehículo de 70 m. Cambia el margen estático en la
fase de entrada del booster. *El modelo de slosh pendular no lo representa* (amortigua
solo con aceleración axial > 0, `SloshStepModel.cs:189–194`).

**Corrección.** Asentar la columna según el signo de la aceleración propia axial filtrada
(con histéresis), y registrar el caso lateral como límite de validez en
`docs/physics/propellant_slosh.md`. Para el booster real, las cabeceras/deflectores
mantienen el propelente de aterrizaje: verificar qué tanques declara el JSON.

---

### A-08 — Inercia rígida del líquido en cabeceo sin reducir

- **Severidad** baja-media · **Confianza** media · `Physics/PropellantSlosh.cs`.

Verificado correcto: `m₁/m = 2·tanh(x)/((ξ²−1)·x)` con `x = ξh/R`, ξ = 1,8411837813…;
`L = R/(ξ tanh x)`; altura de la masa de slosh `h[1 − tanh(x/2)/(x/2)]`; bisagra = altura
+ L; la masa fija conserva el CG.

Falta: el modelo mecánico de SP-106 asigna a la masa fija un momento de inercia I₀
*menor* que el del líquido congelado, porque un líquido ideal en un cilindro no gira
rígidamente con el tanque en cabeceo. El código conserva `(3R² + h²)/12` por unidad de
masa. Para tanques esbeltos (h/R ≳ 3) la diferencia es pequeña; para tanques chatos
(h/R ≲ 1) el líquido real aporta mucha menos inercia propia.

**Magnitud.** El término propio es pequeño frente al de ejes paralelos. Ejemplo S-IC LOX
(R = 5 m, h = 15 m, a 20 m del CG): propio (3·25 + 225)/12 = 25 m², paralelo 400 m²;
aunque I₀ fuera la mitad, el error es ~3 % del aporte del tanque. Relevante solo para
etapas cortas con tanques chatos (LM, SM).

**Corrección.** Tabla I₀/I_rígido(h/R) de SP-106 con procedencia; prueba analítica en los
límites h/R → ∞ (→ 1) y h/R → 0 (→ 0).

---

### A-09 — `OrbitalElements`: condicionamiento

> **Estado: punto 1 corregido.** Inclinación, nodo, argumento del periapsis y anomalía
> verdadera salen de `atan2` (seno y coseno) en vez de `acos`. Prueba:
> `OrbitalElementsRoundTripTests.AnglesNearTheirSingularValuesRoundTripAtLunarDistance`.
> Medido: 0,41 m por conversión cerca del apogeo translunar antes; < 0,2 mm ahora. Los
> puntos 2–4 (banda parabólica, arranque hiperbólico, época de la anomalía media) siguen
> abiertos.

- **Severidad** baja · **Confianza** alta.

1. Anomalía verdadera, argumento del periapsis, nodo e inclinación por `acos`
   (`OrbitalElements.cs:307, 319, 348, 384`). Cerca de 0 o π, `acos` pierde la mitad de
   los dígitos: δ ≈ 1,5e-8 rad → ~6 cm en LEO y ~5,7 m a distancia lunar *por conversión*.
   Con conversiones repetidas (entrada/salida de raíles) no se acumula de forma
   sistemática, pero rompe comparaciones bit a bit y pruebas de ida y vuelta.
   *Corrección*: `atan2(|a×b|, a·b)` con signo por el triple producto.
2. Banda parabólica `|e − 1| < 1e-10` (`287–291`) fija `a = p`; para e ligeramente < 1
   se propaga como elipse con semieje equivocado, y para e ≥ 1 se congela en la ν
   almacenada (`165–172`). Solo alcanzable por casualidad numérica; debería propagarse
   con la ecuación de Barker o variables universales.
3. Arranque hiperbólico `F = M` para |M| ≤ 6 (`199–201`): converge, pero lento para
   e ≫ 1 y M pequeño; sin hallazgo de exactitud.
4. `SolveKeplerEquation` (`Math/MathUtils.cs:42–66`): Newton con arranque de tercer orden,
   tolerancia 1e-10 en el paso. Correcto. Para e → 1 y M → 0 el residuo
   `E − e·sin E − M` sufre cancelación (error relativo ~1e-16/(1−e)); con e = 0,97
   (translunar) es irrelevante. `M % 2π` con M ~ 1e6 rad (épocas de 1969, |t| ~ 1e9 s, si
   la anomalía media se forma como n·(t − t₀) con t₀ lejano) pierde ~1e-10 rad ≈ 0,7 mm
   en LEO: irrelevante, pero conviene confirmar que `t₀` es la época del elemento y no
   cero.

---

## 4. Segunda pasada: lo que se verificó correcto

Cada punto con la derivación mínima que lo sostiene.

- **`BodyMassDistribution.cs`**. Con masas afines en el paso y centros de columna de
  velocidad constante: `m·ċ = Σ ṁᵢ(cᵢ − c) + Σ mᵢ ċᵢ` (l. 136, 149);
  `m·c̈ = 2Σ ṁᵢ ċᵢ − 2ṁ ċ` (l. 137, 150), que sale de derivar la anterior con m̈ᵢ = 0,
  c̈ᵢ = 0; `İ = Σ[ṁᵢ(Jᵢ + P(dᵢ)) + mᵢ J̇ᵢ + mᵢ Ṗ(dᵢ, ċᵢ)]` donde la parte en −ċ se anula
  porque Σ mᵢ dᵢ = 0 (l. 138–147); `Ṗ(d, v) = 2(d·v)E − d vᵀ − v dᵀ` (l. 181–190). ✓
- **`VariableMassStepModel.cs`**. `R·I·Rᵀ` (l. 340–355) ✓; tensor seco
  diag(transversal, axial, transversal) con Y axial (l. 331–335) ✓; retirada a masa
  congelada ante cobertura incompleta, propelente infinito o salida no declarada ✓.
- **`SloshMomentumTransfer.cs`**. En ejes cuerpo, con v la velocidad del datum:
  `p = m v + ω × (m c) + p_int`, `h_O = m c × v + I_O ω + h_int`. Imponiendo p y h_O
  iguales antes y después: `v′ = P_mat/m − ω′ × c′` y, sustituyendo,
  `h_O − h′_int − c′ × P_mat = (I′_O − m P(c′)) ω′ = I′_cm ω′` (l. 50–56). ✓ Conserva los
  seis momentos; rechaza cambio de masa (l. 34).
- **`RigidBody6DofIntegrator.cs`**. `ω̇ = I⁻¹(τ − ω × Iω)` (l. 104–108) ✓;
  `q̇ = ½ q ⊗ (0, ω_cuerpo)` (l. 110–120) ✓ (ω en ejes cuerpo, producto por la derecha);
  pesos RK4 ✓; masa muestreada en la época de cada etapa (l. 95) ✓.
  Observación: las etapas intermedias se **normalizan** (l. 137, 165). Para ω constante,
  con a = hω/2: 1/n₂ ≈ 1 + a²/8 y 1/n₃ ≈ 1 − a²/8, de modo que el error cúbico se cancela
  entre k₂ y k₃ y n₄ = 1 + O(a⁴). El orden del cuaternión baja como mucho de 4 a 3
  (término a⁴ no verificado). Con ω = 1 rad/s, h = 0,02 s: a⁴ = 1e-8 por paso, ≤ 5e-7
  rad/s de deriva de fase. Despreciable; no es hallazgo.
- **`GravityGradient.cs`**. `τ = (3μ/r³)·n × (I n)` (l. 40–42) ✓, cuadrático en n, el
  signo de n no importa. Aceleración diferencial `(μ/r³)(3n(n·d) − d)` (l. 20–22) ✓.
  Límite: atractor puntual (sin J2; error relativo ~J2 ≈ 1e-3 del par).
- **Geodesia (`CelestialBody.cs:321–366, 391–412, 441–453`)**. Directa:
  `N = a/√(1 − e² sin²φ)`, `ρ = (N + h) cos φ`, `z = (N(1 − e²) + h) sin φ` ✓. Inversa:
  Bowring de un paso, `θ = atan2(z a, p b)`,
  `φ = atan2(z + e′² b sin³θ, p − e² a cos³θ)` ✓, `h = p/cos φ − N` ✓. Radio geocéntrico
  del elipsoide `ab/√(b² cos²φ_c + a² sin²φ_c)` ✓. `GetSurfaceVelocity = ω × r` ✓;
  `GetAirRelativeVelocity = v − v_cuerpo − (ω × r + viento)` ✓.
  Límites: Bowring de un paso tiene error máximo de ~0,002″ a alturas del orden de 2a
  **(de memoria)**, centímetros; `h = p/cos φ − N` se degrada a < 4 mm del polo (error
  relativo 1e-16/(π/2 − φ)), cubierto en parte por la guarda de l. 345.
- **`SolveDifferentialGimbal` (`PartGraph.cs:1312–1384`)**. Mínima norma:
  `J_x = r × (F₀·rango·X̂)`, `J_z = r × (F₀·rango·Ẑ)`, `M = Σ(J_x J_xᵀ + J_z J_zᵀ)`,
  `λ = (M + εE)⁻¹ τ`, `gᵢ = (J_x·λ, J_z·λ)` ✓ (pseudo-inversa de Tikhonov,
  ε = 1e-6·max(1, tr M)).
- **`BodyFlapAerodynamics.cs`**. Normal `(lado·sin δ, 0, −cos δ)` ⟂ envergadura ✓;
  coeficiente de presión de remanso de Rayleigh-pitot → 1,839 a M → ∞ con γ = 1,4 ✓;
  pendiente de Jones `(π/2)·AR` ✓ para AR ≲ 1.
- **`CapturePlanner.cs`**. Vis-viva en ambos términos ✓.
- **`SurfaceContactSolver.cs`**. `F_n = max(0, k·pen − c·v_n)`, fricción
  `min(c_t|v_t|, μF_n)` opuesta a v_t ✓, par `r × F` respecto al CoM ✓, velocidad del
  punto `v_CoM + ω × r` ✓.
- **`MathUtils.OrbitalToInertialStateVector`**. Matriz perifocal→inercial 3-1-3 ✓;
  `v = √(μ/p)·(−sin ν, e + cos ν)` ✓.

Primera pasada (ya informado, se mantiene): fuerza y par de transporte de masa variable,
continuidad, exclusión de `R·ċ` en velocidad material, cortes de flujo, ecuaciones del
núcleo de slosh y sesgo de carta, consistencia de época del slosh en costa, cadena IERS
(orden, signos, ERA, s′, interpolación UT1−TAI con segundos intercalares, ω con LOD),
términos TDB−TT, reloj de Neumaier, Clenshaw valor/derivada, Tierra = EMB − Luna·μ_L/(μ_T+μ_L),
recursiones de Cunningham y normalización, ángulos 3-1-3 lunares, fórmulas de Lambert,
asíntota de TEI, convención "desde" del viento, residuo de gas ideal ≤ 0,1 % en la mezcla,
terminación de la pseudo-inversa redistribuida de flaps, contabilidad de pares de gimbal/RCS.

---

## 5. Hallazgos menores

| ID | Dónde | Qué | Magnitud | Confianza |
|---|---|---|---|---|
| A-10 | `VariableMassStepModel.cs:249` | Salidas de motor declaradas abiertas en [0, min(duración, dt)) aunque el motor encienda a mitad de paso; con dos motores del mismo depósito que arrancan en instantes distintos, el reparto del flujo entre salidas se equivoca en `q_B·(e_B − e_A)` | Un paso por encendido; ≤ q·Δe·dt de momento angular | Media |
| A-11 | `SloshStepModel.cs:39` frente a `VariableMassStepModel.cs:63` | **Revisado estáticamente:** ambas rutas usan `AssignSkirtDatumPositions`; la ruta de masa usa una caché invalidada por cambios de topología. No se ha demostrado una diferencia de datum; falta una prueba específica de invalidación tras cambios de topología. | Riesgo de caché pendiente de prueba | Baja |
| A-12 | `PartGraph.cs:1370–1384` | Recorte independiente por motor sin redistribución cuando un gimbal satura (documentado). Con un motor apagado en un anillo, el par entregado es menor que el pedido aunque haya autoridad de sobra | Hasta 1/N del par | Alta |
| A-13 | `PartGraph.cs:1180` | Envolvente de aceleración angular con brazo axial `|ΔY|` y radio de balanceo heurístico 0,65·R; no es la envolvente real del asignador | ±30 % en autoridad de balanceo declarada | Alta |
| A-14 | `PartGraph.SolveSymmetric3x3` | **Corregido:** la matriz se normaliza antes de evaluar su determinante y la regularización escala con su traza. Una regresión con motor de 1 mN comprueba que conserva autoridad. | Antes: orden de gimbal cero a escala pequeña | Alta |
| A-15 | `BodyFlapAerodynamics.cs` | **Corregido:** Cp,max usa γ de la atmósfera en fuerzas, asignación y predicción. La regresión compara el resultado a Mach 5 con γ = 1,29. | Antes: γ terrestre fijo en otros gases | Alta |
| A-16 | `BodyFlapAerodynamics.cs` | Fuerza en el centroide de la placa, no en el centro de presión (25 % cuerda en subsónico, ~50 % en newtoniano); sin sombra del casco (documentado) | Momento de bisagra, no fuerza; brazo ±25 % de cuerda | Alta |
| A-17 | `RigidBodyForceEvaluator.cs:75`, `Vessel.cs:1271, 1610` | Contrapresión de motores por `Atmosphere.GetPressure(altitud)` en vez del punto único `AtmosphericPressureAt` (ignora NRLMSIS/campo fechado) | **Corregido**: todas las lecturas pasan por `GetAtmosphericPressure`; prueba `NrlmsisAtmosphereTests.EngineBackPressureReadsTheAtmosphereField`. Efecto físico nulo por debajo de 80 km (el campo fechado solo difiere encima) | Alta |
| A-18 | `DistributedAerodynamics.cs:166` | Término esbelto de Munk sin factor `cos(α/2)` | +13 % del término potencial a α = 60° | Media |
| A-19 | `CrossflowDrag.cs:34` | η·C_dn fijado en el valor de Re_n = 1e7 por encima; belly-flop de Starship ~6e7 | Desconocida; Jorgensen no tabula más allá | Media |
| A-20 | `NrlmsisAtmosphereField` | Hora local necesita longitud IERS; no se exige que la orientación fechada esté activa. Recorte en el polo en vez de reflexión de 12 h | Densidad ±10–30 % a > 200 km si la longitud es la del meridiano arbitrario | Media |
| A-21 | Gravedad EGM2008 | Coeficientes tide-free sin marea permanente: ΔJ2 ≈ 9,3e-9 **(de memoria: ΔC̄20 ≈ 4,17e-9)** | ~5 m/día en LEO | Media |
| A-22 | Bordes de cobertura | Fuera de 1960–2051 el sistema salta a Kepler sin transición; `IersEarthOrientation.cs:119` lanza excepción | Discontinuidad de estado de cuerpos | Alta |
| A-23 | `Universe.cs:2657, 2670` | Trayecto partido heredado evalúa la gravedad en el datum, no en el CoM | 1–2e-4 m/s² (solo línea base de comparación) | Alta |
| A-24 | Reloj en 1969 | Cuantización del `double` a |t| ≈ 9,6e8 s: ulp = 1,2e-7 s → ≤ 24 µm por paso a 200 m/s si la posición usa `t` absoluto en alguna diferencia | Hipótesis de baja confianza | Baja |
| A-25 | Freedom 7 | UT1 = UTC; el código declara σ = 1 s | ≤ 41 m si |UT1 − UTC| ≤ 0,1 s; hasta 465 m con 1 s | Media |
| A-26 | `SloshStepModel.cs` | `AdiabaticRelativeRateLimit = 0,1` es una estimación; tanques del secundario acoplado tratados como masa constante | Límite de validez | Alta |
| A-27 | `KeplerPropagator.cs:132`, datos | Raíles y planificadores usan `body.GM`; el campo armónico usa su propio GM. Δμ/μ ~ 1e-9 | Despreciable; anotar | Alta |
| A-28 | `SurfaceContactSolver.cs:20` | Velocidad de superficie evaluada en el punto proyectado al elipsoide, no en el punto de contacto a altura h | ω·h ≈ 7e-3 m/s a 100 m | Alta |
| A-29 | `Vessel.cs:478–483` | `DockedInertiaScale` aplica la razón transversal a los tres ejes (documentado) | Balanceo de pila acoplada mal escalado por I_ax,pila/I_ax vs I_t,pila/I_t | Alta |

---

## 6. Preguntas abiertas (necesitan lectura o una prueba)

1. **A-11**: ¿`ComputePartLocalPositions()` y `GetTopologyPartLocalPositionsForMass()`
   devuelven el mismo origen en todos los vehículos (con y sin tren, con faldón)?
2. **A-06**: confirmar la estación del plano de gimbal del SPS en el JSON del SM y el
   desplazamiento lateral real del CG de la pila CSM+LM en el modelo.
3. **Rigidez de contacto**: con RK4 explícito hace falta `ω_n·dt < 2,78`
   (`ω_n = √(k/m)`). Con dt = 0,02 s, `k < 1,9e4·m` N/m. ¿Lo cumplen los `SpringStiffnessNPerM`
   de patas, pines y testigos de casco para la masa mínima (vehículo seco)? No leí los
   valores.
4. **A-09.4**: ¿la anomalía media se forma respecto a la época del elemento o respecto a
   t = 0? Con épocas de −9,6e8 s importa para la precisión.
5. **Sentido de los ejes de órbita**: `OrbitalToInertial` usa Z como polo; el simulador
   usa Y como eje axial de vehículo. Confirmar que la convención de polo de cuerpos y
   elementos es única en `CelestialBody`/`OrbitalElements` (no revisado en esta pasada).
6. **A-07**: qué tanques y cabeceras declara el JSON de Super Heavy para el propelente de
   aterrizaje.
7. **Signo del gimbal** en `Part.cs` (~l. 1106) frente al jacobiano de
   `SolveDifferentialGimbal`: no leído.
8. **RK4 del cuaternión**: confirmar con una prueba analítica (ω constante, 1 rad/s,
   1 000 s) que el error de fase es de orden ≥ 3 con las etapas normalizadas.

---

## 7. Pruebas de regresión propuestas (resumen)

| Para | Prueba | Criterio |
|---|---|---|
| A-01 | Reposo sobre la cuna, dt ∈ {0,02; 1/60; 0,005} | Fuerza igual en las 4 etapas (1e-9 rel.); penetración independiente de dt |
| A-03a | Nodo tras 9 856 s de aparcamiento, RK4 frente a raíles | Coinciden a 0,01° |
| A-03d | Secante sin encuentro | Estado `NoSolution` explícito, no retorno silencioso |
| A-04 | Continuidad a través de 304,8 m y 762 m, 100 semillas | Salto < 1e-3·σ |
| A-05 | S-IVB en encendido con ω_x inicial 0,01 rad/s, sin control | ω_x constante ±1 % con participación 0 |
| A-06 | Escalón de tasa CSM solo frente a CSM+LM con SPS | Misma constante de tiempo ±5 % |
| A-07 | Booster con 5 % de propelente bajo deceleración hacia proa | CG se desplaza hacia el domo delantero |
| A-08 | I₀/I_rígido en h/R → ∞ y → 0 | → 1 y → 0 |
| A-09 | Ida y vuelta estado→elementos→estado en ν ∈ {1e-9, π − 1e-9}, i ∈ {1e-9, π − 1e-9} | < 1e-6 m a distancia lunar |
| A-12 | Anillo con un motor apagado, par pedido al 60 % de la autoridad restante | Par entregado ≥ 99 % del pedido |
| A-14 | Vehículo de 1 kg con motor de 1 mN | Solución no nula |

Todas las de física compartida deberían etiquetarse con `FidelityGate`/`GateKind` según
`docs/physics/fidelity_gates.md` si cierran una fase.

---

## 8. Auditoría pendiente (archivos no leídos)

Por orden de riesgo estimado:

1. `Integrators/Slosh6DofIntegrator.cs`, `Physics/SloshKineticSystem.cs`,
   `SloshVariableMassDynamics.cs`, `SloshTransportCorrection.cs`,
   `SloshParameterSchedule.cs`: el acoplamiento slosh + masa variable es el punto donde
   más términos cruzados pueden faltar (derivada temporal de m₁, L y bisagra con el
   llenado).
2. Guiado en vuelo: `Flight/LambertGuidedBurn.cs`, `PeriapsisCaptureBurn.cs`,
   `ProgradeEnergyBurn.cs`, `ApsidesTargetBurn.cs`, `LunarPoweredDescent.cs`,
   `CapsuleEntryGuidance.cs`, `TransearthCorrection.cs`: pérdidas por encendido finito,
   criterio de corte, y si usan el CoM o el datum.
3. `Physics/RcsAllocation.cs`: asignación de pares, reserva de propelente, pila acoplada.
4. `AerodynamicsModel.cs`: modelo romo/trimado, calentamiento (Sutton-Graves o
   equivalente), y la transición con la aero distribuida en L/D = 3.
5. `Parts/PartMassGeometry*.cs` y `Part.cs`: descomposición de masa única, signo del
   gimbal, `NozzleExitAxialOffsetM`.
6. `Navigation/TrajectoryPrediction.cs`, `RendezvousPlanner.cs`, `ProximityApproach.cs`.
7. `Integrators/RK4Integrator.cs`, `CoastingAttitudeIntegrator.cs`.
8. Resto de `CelestialBody.cs` (atmósfera por capas en metros geopotenciales,
   `DatedBasisAt`, `PlanningElementsAt`), `EnvironmentContext.cs`,
   `EnvironmentProductFile.cs`.
9. Flotación y agua (Flight 14), FTS, ruptura estructural.

---

## 9. Orden de trabajo recomendado

1. **A-01** (cuna de captura). Corrección local, riesgo bajo, afecta a un hito visible.
   Recalibrar después `BoosterReturnController` y la captura de Ship si las pruebas de
   captura cambian.
2. ~~A-02~~ retirado (ver sección 3).
3. **A-06** (gimbal con pila acoplada). Cambio de una referencia; después re-verificar
   LOI y correcciones translunares de Apollo 11 (pueden necesitar recalibración de
   ganancias, con procedencia `calibrated`).
4. **A-03d** (no convergencia visible). **A-03a** pasa a ser "J2 secular en raíles", trabajo propio. Luego **A-03b/c** (shooting
   numérico), que debería bajar la primera corrección de medio curso; medir el Δv antes
   y después.
5. **A-09** y **A-14** (robustez numérica; baratos).
6. **A-07** y **A-05** (dónde está el líquido). Necesitan decisión de modelo y datos con
   procedencia.
7. **A-04** antes de activar la turbulencia por defecto en ninguna misión.
8. **A-08**, **A-12**, **A-17**, **A-18**, resto de menores.
9. Auditoría pendiente de la sección 8, empezando por el acoplamiento slosh + masa
   variable y el guiado en vuelo.
