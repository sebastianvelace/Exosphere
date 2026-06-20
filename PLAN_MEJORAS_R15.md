# Exosphere — Plan de mejoras (Ronda 15) · DIVIDIDO POR AGENTES

> Corrige los problemas reportados en sesión de vuelo real (ver capturas) y añade realismo de
> motores, atmósfera y reingreso. Repartido en **6 agentes con archivos EXCLUSIVOS (sin
> solapamiento)** para correr en paralelo. Cada agente: objetivo, archivos, causa-raíz investigada,
> subtareas, contratos y criterios de aceptación. Al final, backlog de **fixes / próximos caminos**.

## 0. Contexto técnico (leer SIEMPRE — vale para todos)

- Godot 4.6.3 mono · C# / .NET 8 · escala **1 u = 2.8 m** · vessel activo en el ORIGEN (`FloatingOrigin`).
- **Build 0/0 OBLIGATORIO** en cada paso (lo refuerza el Stop hook `.claude/hooks/build-check.sh`):
  `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet` y
  `dotnet build Exosphere.csproj --nologo -v quiet`.
- Godot headless: `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 --path . --rendering-driver opengl3`.
- **CLAVE — cada etapa = UNA parte-motor** en el sim (Super Heavy = 1 parte ≈74 MN; el "33" es visual).
- **El modelo de motor YA es realista** (NO reescribir, AUDITAR/ajustar): `Part.SpoolToward()` (rampa
  2.0/s ≈ Raptor), `Part.GetIsp(presión)` (ISP vac↔SL interpolado), `Part.ApplyThrottleFloor()`.
  Verificado autoconsistente: F = ṁ·Isp·g₀ (74.4 MN / 327 s / 23.2 t/s ✓).
- **Las piezas ya traen datos térmicos**: `Part.Temperature`, `Definition.heat_tolerance`,
  `has_heat_shield` (ver `data/parts/*.json`). El reingreso debe USARLOS.
- **Skills** (`.claude/skills/`): `orbital-physics`, `godot-rendering-bridge`, `visual-testing`,
  `data-content`. Agente revisor: `physics-reviewer` (`.claude/agents/`).
- Reglas: comentarios ES/EN como el archivo vecino · **no romper** el ascenso `[G]` ni la EDL ·
  commits por tarea.

---

## 1. CONTRATOS DE INTERFAZ (nombres EXACTOS — el dueño los crea; los demás solo LEEN/llaman)

- **Ignición / throttle continuo (dueño: A · llamador: E)** — en `scripts/SimulationBridge.cs`:
  - `public void Ignite()` — si `ActiveVessel.IsGroundHeld`, arranca ignición y **suelta el clamp al
    TWR > 1.02**; si ya vuela, comanda throttle = 1.
  - `public void ThrottleUp(double dt)` / `public void ThrottleDown(double dt)` — suben/bajan el
    throttle COMANDADO de forma continua (mientras se mantiene la tecla). El spool de `Part` suaviza.
  - `public bool IsIgnitionActive { get; }`.
- **Destrucción (dueño ÚNICO: C)** — `Vessel.IsDestroyed` SOLO lo escribe `Universe.cs` (C). B, E, F
  lo LEEN. F entrega el daño térmico; C decide la destrucción (ver contrato térmico).
- **Térmico/reingreso (dueño: F · lector: C)** — F expone en `StressSolver`:
  - `public static double WorstHeatRatio(PartGraph parts)` — máx. `Temperature/heat_tolerance` de las
    piezas expuestas al flujo. C marca `IsDestroyed` si supera 1.0 en una pieza SIN escudo orientado al
    flujo. F NO escribe `IsDestroyed`.
  - F mantiene estable la API de atmósfera que A/C leen: `AtmosphereModel.GetDensity(alt)` y
    `GetPressure(alt)` (si cambia firmas, lo coordina por este contrato).
- **Throttle en descenso (dueño: B)** — `EDLController` solo comanda `vessel.Throttle` cuando su fase
  EDL ≠ `Inactive`; al desactivarse NO vuelve a tocarlo.

> Si un agente necesita un dato ajeno, lo pide vía contrato; **nunca** edita un archivo de otro agente.

---

## AGENTE A — Lanzamiento (clamp + hold-throttle) + auditoría de motores
**Skills:** `orbital-physics` · `godot-rendering-bridge` · `visual-testing` · `data-content` (JSON de
motores) · revisar con `physics-reviewer`.

**Archivos EXCLUSIVOS:** `ExosphereSimulation/Vessel.cs` · `ExosphereSimulation/Parts/Part.cs` ·
`ExosphereSimulation/Parts/PartGraph.cs` · `ExosphereSimulation/Parts/PartDefinition.cs` ·
`scripts/SimulationBridge.cs` · `scripts/LaunchPadController.cs` · `data/parts/*.json`

**Problema 1:** con **TWR 1.85 y 33/33 motores encendidos** el cohete NO despega (sigue en PRE-LAUNCH).
**Causa-raíz (verificada):** `[Z]` solo hace `bridge.SetThrottle(...)`; nada suelta `IsGroundHeld`, y
`Universe.TickPhysics` clampea el vessel a la superficie mientras esté ground-held, ignorando el empuje.
**Problema 2:** hay que **spamear `[Z]`** para encender los motores; se quiere **mantener `[Z]`** para
que suban progresivamente (la captura muestra 33/33, pero llegar ahí es a punta de toques).

**Subtareas:**
1. `SimulationBridge.Ignite()` (contrato §1): con `IsGroundHeld`, rampa de throttle comandado y
   `ReleaseGroundHold()` al `TWR>1.02` (reusar `ComputeThrust(body)` + gravedad local). Entrar
   `MissionPhase.LIFTOFF` (llamada a B). Si ya vuela, throttle = 1.
2. `SimulationBridge.ThrottleUp(dt)` / `ThrottleDown(dt)`: ajuste continuo del throttle comandado
   (p. ej. ±0.5/s · dt). El spool de `Part.SpoolToward()` ya suaviza la respuesta → realista.
3. **Auditoría de motores (no reescritura):** revisar y, si hace falta, ajustar para que queden
   realistas: `SpoolRate` (arranque/parada), `ApplyThrottleFloor` (mínimo de Raptor ~40 %), curva
   `GetIsp(presión)` (vac≈363 s / SL≈327 s), gimbal, y el comportamiento al cortar (MECO suave).
   Validar números contra Raptor real; documentar cualquier cambio en el JSON (`IspVac`, `IspSL`,
   `MinThrottle`, `SpoolRate`).
4. (Opcional) `LaunchPadController`: soltar hold-downs visuales sincronizados con el release.

**Criterios de aceptación:**
- Build 0/0. **Mantener `[Z]`** (cableado por E) sube motores progresivamente y **despega al TWR>1**
  sin usar `[L]`/`[O]`/`[G]`. Captura headless del despegue manual.
- Números de motor consistentes (F=ṁ·Isp·g₀) y realistas a toda altitud. Veredicto **CORRECTO** de
  `physics-reviewer`. No rompe el ascenso `[G]`.

---

## AGENTE B — Máquina de estados EDL / Mission (fin del latch + robo de throttle)
**Skills:** `godot-rendering-bridge` · `visual-testing`.

**Archivos EXCLUSIVOS:** `scripts/EDLController.cs` · `scripts/MissionManager.cs`

**Problema:** tras un descenso, en el espacio se muestra **CRASHED / FINAL DESCENT / EDL·EARTH** y los
**motores no encienden** (throttle pisado a 0).
**Causa-raíz (verificada):** `EDLController` solo se activa con la guarda `_phase == Inactive`
(línea ~72); una vez en `Retro`/`Final` durante un descenso, al volver a subir **nunca se desactiva**
(`case Final` hace `return` en línea ~145 manteniendo `_phase=Final` y `Visible=true`), y `AdvancePhase`
**comanda `vessel.Throttle` cada frame** (suicide-burn → ~0), pisando al jugador.

**Subtareas:**
1. Guarda de desactivación que corra SIEMPRE (no solo en `Inactive`): si
   `_alt > body.Atmosphere.MaxAltitude*1.05` **o** `_vUp > 5` (ascendiendo) **o** el jugador comanda
   throttle arriba → `Deactivate()` y dejar de tocar el throttle.
2. `Deactivate()` resetea `_phase=Inactive`, `Visible=false` y no vuelve a escribir `vessel.Throttle`.
3. `MissionManager`: `CRASHED` es terminal **solo si `IsDestroyed==true`** (leer flag, contrato de C).
   Si el vessel no está destruido, no quedarse pegado en CRASHED/descenso; al ascender/`ORBIT`, limpiar
   restos de fase de descenso.

**Criterios de aceptación:** build 0/0. En órbita/escape NO aparece EDL/FINAL DESCENT/CRASHED salvo
impacto real. El throttle del jugador manda. La EDL real de aterrizaje **sigue funcionando**.

---

## AGENTE C — Órbita/transferencia + impacto REAL (sin rebote a órbita)
**Skills:** `orbital-physics` · `physics-reviewer` (OBLIGATORIO) · `visual-testing`.

**Archivos EXCLUSIVOS:** `ExosphereSimulation/Universe.cs` · `scripts/ManeuverExecutor.cs` ·
`scripts/TransferPlanner.cs` · `scripts/ManeuverPlanner.cs`

**Problema:** aceleré la órbita, el periapsis bajó de la superficie, **choqué con la Tierra y el cohete
volvió a salir a órbita** — imposible en la vida real.
**Causa-raíz (verificada):** el "soft-rest" de `Universe.cs` (líneas ~234-243 y ~309-314) reubica el
vessel a `Radius+1` con velocidad de superficie en CUALQUIER impacto que no supere 12 m/s, y en los
caminos on-rails (Kepler) **no se comprueba** que el periapsis caiga bajo el radio → la nave "atraviesa"
y reaparece. Un impacto a velocidad orbital (>>12 m/s) debe **destruir**, no rebotar.

**Subtareas:**
1. Impacto realista: si `altitude < 0` con velocidad relativa a superficie por encima de un umbral de
   aterrizaje seguro (p. ej. EDL con tren desplegado y `vDown` pequeño), seguir permitiendo el reposo;
   **en cualquier otro caso marcar `IsDestroyed`** (contrato §1). Nada de soft-rest a velocidad orbital.
2. On-rails seguro: en la propagación Kepler (`PropagateVesselOnRails`), si el periapsis predicho < radio
   del cuerpo, **NO** dejar que la nave atraviese: salir de rails y resolver el impacto (destruir).
3. Salida de on-rails fiable: si el jugador comanda throttle/empuje, salir de rails en ≤1 sub-paso
   (revisar `shouldBeOnRails` en `TickPhysicsMixed`, línea ~262).
4. `ManeuverExecutor`: confirmar que no choca con la desactivación de EDL de B (contrato §1).

**Criterios de aceptación:** build 0/0 (ambos). Bajar el periapsis bajo la superficie **destruye** la
nave (no rebota). Fijar transferencia no marca `IsDestroyed` sin impacto. Veredicto **CORRECTO** de
`physics-reviewer`. No rompe `[G]` ni la EDL.

---

## AGENTE F — Atmósfera realista + reingreso con escudo térmico
**Skills:** `orbital-physics` (modelos de física) · `data-content` (capas de atmósfera, escudos) ·
`physics-reviewer` (OBLIGATORIO) · `visual-testing` (plasma de reingreso).

**Archivos EXCLUSIVOS:** `ExosphereSimulation/AtmosphereModel.cs` ·
`ExosphereSimulation/AtmosphereModelJson.cs` · `ExosphereSimulation/Physics/AerodynamicsModel.cs` ·
`ExosphereSimulation/Physics/ThermalModel.cs` · `ExosphereSimulation/Physics/StressSolver.cs` ·
`scripts/ReentryPlasmaController.cs` (NUEVO — sin conflicto) · `data/bodies/*.json` (capas atmosféricas)

**Objetivo:** que el reingreso sea como en la vida real: arrastre y calentamiento por capas, y que **se
necesite el escudo térmico de la Starship** para sobrevivir; sin escudo (o con la cara equivocada al
flujo), la nave se destruye.

**Subtareas:**
1. **Atmósfera:** confirmar/mejorar `AtmosphereModel` (densidad/presión por capas — ya hay `layers` en
   `earth.json`). Exponer `GetDensity(alt)` y `GetPressure(alt)` estables (contrato §1, los lee A para
   ISP y C/aero).
2. **Aerodinámica de reingreso** (`AerodynamicsModel`): arrastre y deceleración realistas en función de
   `q = ½ρv²`, área frontal y `drag_coefficient`; la nave debe frenar por la atmósfera (no caer recto).
3. **Calentamiento** (`ThermalModel` + `StressSolver`): flujo de calor ∝ ρ·v³ (ya hay
   `ComputeHeatFlux`); acumular `Part.Temperature` y comparar con `heat_tolerance`. Exponer
   `StressSolver.WorstHeatRatio(parts)` (contrato §1). Si una pieza SIN `has_heat_shield` orientada al
   flujo supera su tolerancia → reportar daño → C destruye. El escudo de la Starship (cara ventral)
   disipa: temperatura por debajo del límite mientras la orientación de reingreso sea correcta.
4. **VFX de plasma** (`ReentryPlasmaController`, nodo nuevo): glow/estela de plasma que escale con el
   flujo de calor; reemplaza la animación poco realista actual. Coordinar con B (fases EDL) solo por
   lectura del estado.

**Criterios de aceptación:** build 0/0 (ambos). Reingreso: la nave frena por arrastre, se calienta por
capas y **sobrevive solo con el escudo orientado al flujo**; sin escudo o mal orientada → destruida.
Veredicto **CORRECTO** de `physics-reviewer`. No rompe el ascenso ni la EDL nominal de aterrizaje.

---

## AGENTE D — Cámara de cabina: estabilización + mejora visual (más botones/indicadores)
**Skills:** `godot-rendering-bridge` · `visual-testing`.

**Archivos EXCLUSIVOS:** `scripts/CameraController.cs` · `scripts/CameraShake.cs` ·
`scripts/CockpitRenderer.cs` · `scripts/CockpitInstruments.cs`

**Problema 1:** la cabina **se sacude demasiado** en ascenso/órbita baja y no se ve nada.
**Causa-raíz (verificada):** `DriveCockpit` (CameraController ~180) ancla la cámara a `v.Orientation`
**cruda** + shake **×1.8** sin amortiguar.
**Problema 2:** la cabina debe **verse mejor**: más botones, más indicadores.

**Subtareas:**
1. **Estabilizar:** seguir una orientación de cámara **suavizada** (slerp hacia `v.Orientation`,
   ~6–10/s) en vez de la cruda; bajar el shake `*1.8` a ~0.6–0.9 y acotar el offset rotacional.
   Mantener el asentamiento de G acotado. El `CockpitRenderer` usa la misma orientación suavizada.
2. **Mejorar la cabina** (`CockpitRenderer`): más detalle de panel — botoneras, switches, MFD/pantallas
   adicionales, iluminación interior; estética tipo Crew-Dragon/Starship.
3. **Más indicadores** (`CockpitInstruments`): pantallas con telemetría viva (altitud, velocidad,
   v+vert, q, throttle, combustible, fase de misión, navball pequeño). Legibles desde el punto de vista
   del asiento.

**Criterios de aceptación:** build 0/0. Captura headless de la cabina en ascenso: ventanas y consola
**estables y legibles**; se conserva algo de vibración. Cabina visiblemente más rica (botones/pantallas).

---

## AGENTE E — HUD / UX estética + binding hold-throttle
**Skills:** `godot-rendering-bridge` · `visual-testing` (iteración por captura — IMPRESCINDIBLE).

**Archivos EXCLUSIVOS:** `scripts/HUDController.cs` · `scripts/SystemsHUD.cs` ·
`scripts/NavBallController.cs` · `scripts/AttitudeNavball.cs` · `scripts/MapViewController.cs` ·
`scripts/EngineGridHUD.cs`

**Problema:** UI poco estética: paneles solapados (TRAJECTORY tapado por ALTITUDE), valores encimados,
SYSTEMS con texto pisado, navball y mapa sin pulir.

**Subtareas:**
1. **Layout sin solapes:** anclas y márgenes coherentes (telemetría arriba-izq, SYSTEMS debajo,
   PROPULSION abajo-izq, STAGE/ORBIT arriba-der, MAP abajo-der, navball centrado-abajo). Eliminar
   textos encimados de la columna izquierda.
2. **Tipografía/jerarquía:** tamaños/colores consistentes, etiquetas atenuadas + valores destacados,
   alineación a rejilla (reusar `ThrottleCol` y demás constantes).
3. **Navball** (`NavBallController`/`AttitudeNavball`) y **mapa** (`MapViewController`): pulir marcas,
   prograde/retrograde y que el panel TRANSFER no pise la órbita (padding/orden).
4. **Binding hold-throttle (contrato §1):** en `_Process`, sondear si `[Z]`/`[X]` están **mantenidas**
   (`Input.IsPhysicalKeyPressed`); con `IsGroundHeld` llamar `Ignite()`, si no `ThrottleUp(dt)`/
   `ThrottleDown(dt)`. Mantener `[Z]` debe spool-ear progresivamente. Actualizar la barra de ayuda.

**Criterios de aceptación:** build 0/0. Capturas headless (despegue, ascenso, órbita, mapa) **sin
solapes** y legibles. Mantener `[Z]` despega el cohete (integración con A). Telemetría correcta.

---

## 2. Orden sugerido / dependencias

- **Tanda 1 (independientes):** A (lanzamiento+motores), B (EDL), C (impacto), D (cabina). B↔C y C↔F
  comparten contratos de `IsDestroyed`/térmico — archivos disjuntos, coordinan por §1.
- **Tanda 2:** F (atmósfera/reingreso) se apoya en el contrato térmico con C; E cablea `[Z]` al
  `Ignite()`/`ThrottleUp` de A (puede hacer el layout en paralelo y el binding al final).
- Cada agente: build 0/0 + captura `visual-testing` + (A, C, F) revisión `physics-reviewer`. Un commit
  por tarea. Worktree por agente para aislar.

---

## 3. Fixes / próximos caminos (backlog — con todo lo que sé del proyecto)

**Física / simulación**
- **Sin tests automatizados.** Crear un proyecto `ExosphereSimulation.Tests` (xUnit) para órbitas
  (conservación de energía/momento en coast RK4), Kepler vs RK4, ISP/empuje, y reingreso. Cerraría el
  loop sin depender solo de capturas.
- **Limitación "1 parte-motor por etapa".** Impide *engine-out* real (apagar motores sueltos), empuje
  asimétrico y fallos individuales. Camino: modelar N motores como sub-partes con gimbal/estado propio.
- **Separación de etapas y boostback/Mechazilla catch:** hoy el "33/6" es visual; falta secuencia real
  de hot-staging, flip + boostback del Super Heavy y captura en la torre.
- **Floating-origin y precisión** a distancias interplanetarias: validar que no hay jitter en cruceros
  largos (Tierra→Marte) y que el scaled-space se mantiene proporcional.
- **SOI / cuerpo dominante en transiciones** (Tierra→Luna→Sol): revisar `GetDominantBody` en las
  fronteras y los "patched conics" del planificador de transferencias.

**Jugabilidad / sistemas**
- **Recursos de vida/energía/comms/térmico** (`Systems/*`): conectar consumo real a las fases (eclipse
  = sin solar, comms con retardo por distancia, control térmico en sombra/sol).
- **Construcción de naves (VAB):** la escena `scenes/construction` está vacía; falta editor de piezas.
- **Save/Load** (`SaveSystem`): persistir misión, órbita y recursos entre sesiones.
- **Autopiloto de aterrizaje (suicide burn)**: pulir setpoints para tomas suaves repetibles en Marte.

**Presentación**
- **Audio** (`AudioManager`): motores, viento atmosférico, alarmas de q/g, sonido de separación.
- **VFX**: plumas con Mach diamonds por altitud, nube de despegue, plasma de reingreso (Agente F),
  daño/quemado visible del escudo.
- **Mapa del sistema** ([Tab]): ventanas de lanzamiento, nodos de maniobra arrastrables, predicción de
  intercepción con la Luna/Marte.

**Ingeniería / flujo**
- **CI headless**: correr `dotnet build` + tests en cada push (el Stop hook ya cubre el local).
- **Profiling**: medir coste por frame de HUD/render con muchas piezas; el `Dt 21 ms` del HUD sugiere
  margen, pero conviene vigilar al escalar.
- **Más skills de proyecto** según crezca: `staging-sequence`, `reentry-thermal`, `vab-construction`.

> Plan finalizado. Cuando quieras, lanzo la Tanda 1 (A, B, C, D) en paralelo, cada agente en su worktree.
