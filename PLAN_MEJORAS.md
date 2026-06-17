# Exosphere — Plan de mejoras (Ronda 14) · DIVIDIDO POR AGENTES

> Estado: Ronda 13 integrada y commiteada. Este documento lista TODO lo pendiente, ya repartido
> en **agentes con archivos EXCLUSIVOS (sin solapamiento)** para correr en paralelo. Cada agente
> tiene objetivo, archivos, subtareas paso a paso, contratos con otros agentes y criterios de
> aceptación. Está pensado para ejecutarse directo y que salga bien a la primera.

## ✅ Hecho en Ronda 13 (NO rehacer)
Modelo de acero suave + nariz ojival + 33 bells curvas · físicas de motor (getters de telemetría
+ suelo de throttle Raptor inerte) · plumas con Mach diamonds + nube de despegue mayor ·
**espacio con estrellas/Vía Láctea + Sol con glow + Luna + limbo atmosférico** · HUD estilo SpaceX
+ navball + rejilla de motores · ascenso a tiempo real (warp 1). El ascenso [G] **llega a órbita**.
La escala nave/planeta quedó proporcional.

---

## 0. Contexto técnico (leer SIEMPRE antes de tocar)
- Godot 4.6.3 mono · C# / .NET 8 · escala **1 u = 2.8 m** · el vessel activo se renderiza en el
  ORIGEN (`FloatingOrigin`). Planetas = scaled-space. Sitio de lanzamiento 27.5°N/80.7°O (tierra).
- **Builds 0/0 OBLIGATORIO** en cada paso:
  `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet` y
  `dotnet build Exosphere.csproj --nologo -v quiet`.
- Binario Godot headless:
  `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 --path . --rendering-driver opengl3`.
- **Prueba visual**: autoload temporal `scripts/_XxxShot.cs` → headless → PNG en /tmp → revisar →
  BORRAR helper + `git checkout project.godot`. Nunca commitear el harness ni el autoload.
- **CLAVE — el sim modela cada ETAPA como UNA sola parte-motor**: Super Heavy = 1 parte (74 MN),
  Starship = 1 parte. El "33" y "6" son solo el MODELO visual. `Parts.ActiveEngines.Count == 1`.
- **CLAVE — impacto con superficie**: hoy `ExosphereSimulation/Universe.cs:204-214` hace
  "soft-rest" en CUALQUIER impacto (si `altitude<0` te sube a `Radius+1` y pone la velocidad de la
  superficie). Por eso el cohete "aterriza" derecho sin estrellarse — ver Agente D/E (N11).
- Reglas: estilo de archivo (comentarios ES/EN) · no romper el ascenso [G] a órbita · no romper
  la EDL de aterrizaje (usa throttle profundo) · commits por tarea.

---

## 1. CONTRATOS DE INTERFAZ (acordados entre agentes — respetar nombres EXACTOS)
Para que los agentes no choquen, estos son los puntos de contacto. El agente DUEÑO los crea; los
demás solo los LEEN.

- **Crash (dueño: Agente D · lector: Agente E)** — en `ExosphereSimulation/Vessel.cs`:
  - `public bool IsDestroyed { get; set; }` — true cuando el vessel se estrella.
  - `public double CrashImpactSpeed { get; set; }` — m/s relativos a superficie en el impacto.
  - `public Vector3d CrashSimPosition { get; set; }` — posición de simulación del impacto.
  - D la marca; E observa `SimulationBridge.Instance.ActiveVessel.IsDestroyed`.
- **Time-warp (dueño: Agente D)** — en `scripts/SimulationBridge.cs`:
  - `public void SetWarpIndex(int i)`, `public int WarpIndex`, `public double[] WarpLevels`,
    `public int MaxAllowedWarpIndex` (según altitud/propulsión). `WarpController` (D) los usa.
- **Sistemas/recursos (dueño: Agente F)** — autocontenido: `SystemsController` (nodo nuevo) lee
  el vessel y el universo desde `SimulationBridge.Instance`; NO edita `Vessel.cs`.
- **Interplanetario (dueño: Agente G)** — usa solo API de lectura existente de
  `SimulationBridge`/`Universe`/`Vessel`; aplica empuje vía `vessel.Orientation`/`vessel.Throttle`.

---

## 2. ROSTER DE AGENTES (archivos EXCLUSIVOS → paralelizable)

| Agente | Tareas | Archivos EXCLUSIVOS (nadie más los toca) |
|--------|--------|------------------------------------------|
| **A · HUD-fixes** | N1, N2, N6a | `scripts/EngineGridHUD.cs`, `scripts/AttitudeNavball.cs`, `scripts/HUDController.cs` |
| **B · Tierra/nubes** | N3, N4 | `assets/shaders/earth_surface.gdshader`, `assets/shaders/earth_ground.gdshader` |
| **C · Llama/VFX** | N5, N7 | `assets/shaders/raptor_plume.gdshader`, `scripts/PlumeSystem.cs`, `scripts/LaunchEffectsController.cs` |
| **D · Sim-core** | N8, N11-det, N6b | `ExosphereSimulation/Universe.cs`, `ExosphereSimulation/Vessel.cs`, `scripts/SimulationBridge.cs`, `scripts/AscentController.cs`, **nuevo** `scripts/WarpController.cs` |
| **E · Explosión** | N11-vfx | **nuevo** `scripts/ExplosionController.cs`, `scripts/MissionManager.cs` |
| **F · Sistemas** | N9 | **nuevos** `ExosphereSimulation/Systems/*.cs`, `scripts/SystemsController.cs`, `scripts/SystemsHUD.cs` |
| **G · Interplanetario** | N10 | `scripts/TransferPlanner.cs`(nuevo), `scripts/ManeuverExecutor.cs`(nuevo), `scripts/MapViewController.cs`, `scripts/ManeuverPlanner.cs` |

Nota de registro en escena: los agentes que añaden NODOS nuevos (WarpController, ExplosionController,
SystemsController/HUD) deben instanciarlos desde un punto ya existente sin editar `Flight.tscn`
(p.ej. añadirlos como hijos desde `SimulationBridge`/`HUDController._Ready` SOLO si ese archivo es
suyo; si no, usar un autoload propio del agente o un nodo que se auto-añade en `_Ready` de un nodo
que sí posean). Si un agente necesita registrarse y no posee un punto de enganche, **dejarlo
documentado** para integrarlo el principal. Coordinar para no editar `Flight.tscn` en paralelo.

---

## AGENTE A · HUD-fixes (N1, N2, N6a)
**Archivos**: `EngineGridHUD.cs`, `AttitudeNavball.cs`, `HUDController.cs`.

### N1 — Rejilla muestra "1/33" (BUG)
- Causa: `EngineGridHUD.cs:70-80` usa `engines.Count` (=1, una parte-motor por etapa).
- Fix: encender puntos = `round(N_nominal · throttle)` con propelente; `N_nominal` = **33** si la
  etapa activa contiene `super_heavy_booster` (por `Definition.Id` en `CurrentStageParts()`),
  **6** si es Starship. Dibujar el layout correcto (33 = 3/10/20; 6 en anillo). Tally "33/33",
  "20/33", "6/6", "0/N".
- Aceptación: SH 100% → 33; Starship → 6; throttle parcial → proporcional; cambia al separar.

### N2 — Navball: cuadro marrón (BUG)
- Causa: `AttitudeNavball.cs:181-205` dibuja quads cielo/suelo más grandes que el disco y "confía
  en el bisel" para tapar esquinas; el bisel es fino → se ve el cuadrado marrón.
- Fix (recomendado): tras dibujar cielo/suelo, **tapar todo lo de fuera del disco** con un anillo
  relleno del color del panel (`DrawArc` grueso o polígono dona) de `Radius`→grande; bisel fino
  encima. Alternativa: recortar los polígonos al círculo, o `SubViewport`/`clip_contents`.
- Pulir: escalera de pitch y marcadores prograde/retrograde/radial dentro del disco.
- Aceptación: a cualquier roll/pitch el horizonte queda DENTRO de un disco limpio; sin cuadrado.

### N6a — HEADING del HUD salta
- Síntoma: rumbo salta (275→54→90→97→109…). Revisar el cálculo de heading en
  `HUDController.cs`/`AttitudeNavball.cs` (proyección del eje/velocidad sobre el plano horizontal);
  estabilizar (usar surface velocity consistente, evitar saltos por casi-cero).
- Aceptación: el heading varía suave durante el ascenso.

---

## AGENTE B · Tierra y nubes (N3, N4)
**Archivos**: `earth_surface.gdshader`, `earth_ground.gdshader`. **Mantener** `planet_alpha` (fade
del backdrop) y el umbral de luces nocturnas ya existentes.

### N3 — Tierra pixelada/blanda desde órbita (~150 km)
- Causa: `earth_surface.gdshader:54` muestrea `day_tex` (8K) directo; a 150 km cada texel (~5 km)
  se magnifica sin detalle → borroso/bloque.
- Fix: añadir **detalle fbm de alta frecuencia** que module contraste/brillo (y opcional un
  micro-relieve/bump) del color base, mezclado por cercanía (fuerte de cerca, nulo de lejos para
  no ensuciar el planeta completo). Opcional: textura día 16K solo si hace falta (⚠️ memoria).
- Aceptación: a ~150 km la superficie se lee nítida/con micro-detalle; vista lejana intacta.

### N4 — Nubes poco realistas
- Causa: `earth_surface.gdshader:56,62` nubes = `mix(día, blanco, cloud·0.85)`: blanco plano, en
  bloque, sin sombra ni volumen. El parche de suelo no tiene nubes.
- Fix: nubes orbitales con **detalle fbm** sobre la cobertura, ligera translucidez/penumbra,
  **sombra de nube proyectada** (oscurecer el día bajo nubes), deriva animada, tono no 100% blanco.
  Opcional: penumbra/nubes finas en `earth_ground.gdshader` y revisar que el agua se vea como agua.
- Aceptación: nubes con detalle/volumen y sombra; sin bloques; agua de baja altitud se lee bien.

---

## AGENTE C · Llama realista + VFX de despegue (N5, N7)
**Archivos**: `raptor_plume.gdshader`, `PlumeSystem.cs`, `LaunchEffectsController.cs`. **Mantener**
las firmas públicas de `PlumeSystem` (`SetupSH/SetupStarship/Update`).

### N7 — Llama realista por presión atmosférica (AMBOS modelos)
Pedido del usuario: lo más realista, con la versión de **tierra** y la de **espacio**.
- **Nivel del mar (despegue)**: escape **sobre-expandido** → llama CORTA pero **muy
  brillante/ancha**, naranja-blanca, con **discos de Mach** marcados y apretados cerca de la
  tobera; mejorar mucho su calidad visual (núcleo incandescente, gradiente, turbulencia) para que
  lo corto NO se vea pobre.
- **Al subir (baja presión)**: los diamantes se separan/suavizan; la llama se **alarga** y aclara.
- **Vacío**: pluma **larga, ancha y TENUE/translúcida** (núcleo fino + halo difuso azulado), sin
  diamantes. Escalar longitud/brillo/diamantes con un proxy de presión (`exp(-alt/7000)`), throttle.
- **Referencias reales**:
  - https://www.twz.com/starships-33-engines-created-the-mother-of-all-shock-diamonds
  - https://everydayastronaut.com/starship-super-heavy-flight-4/
  - https://en.wikipedia.org/wiki/SpaceX_Starship
- Aceptación: despegue = llama corta MUY brillante con diamantes; en altura se alarga; en vacío
  larga y tenue; transición continua con la presión.

### N5 — Nube de despegue ENORME (primeros segundos)
- La nube de diluvio/polvo/vapor debe DOMINAR la pantalla a 0–3 s y disiparse al subir; más
  volumen/altura/persistencia + frente de polvo radial a ras de suelo. ≤ ~700 partículas.
- Aceptación: a 0–3 s la base queda envuelta en una nube enorme.

---

## AGENTE D · Sim-core: time-warp + detección de crash + G de ascenso (N8, N11-det, N6b)
**Archivos**: `Universe.cs`, `Vessel.cs`, `SimulationBridge.cs`, `AscentController.cs`, **nuevo**
`WarpController.cs`. **Define los contratos** de la sección 1.

### N8 — Time-warp estilo KSP (x1…x1000+)
- Niveles: `WarpLevels = {1,2,3,5,10,50,100,1000}` (en `SimulationBridge`). API:
  `SetWarpIndex/WarpIndex/MaxAllowedWarpIndex`.
- **Sobre-rieles**: a warp ≥ x10 (configurable), poner el vessel en órbita kepleriana exacta
  (`Vessel.IsOnRails=true` + `OrbitalState`) en vez de integrar; volver a física al bajar.
  `Universe` ya tiene ramas por TimeScale — extender para on-rails del vessel activo.
- **Límites**: bloquear warp alto cerca del suelo / en atmósfera densa / con motores encendidos
  (`MaxAllowedWarpIndex` según altitud y throttle). Coordinar con el auto-warp de `AscentController`
  (hoy warp 1 propulsado / 4 coast): el auto-warp del AP tiene prioridad mientras [G] está activo.
- **WarpController.cs** (nodo nuevo, auto-registrado): teclas `[.]`/`[,]` + indicador en pantalla
  del nivel y el tope permitido. (El BOTÓN clicable lo puede dibujar este nodo; no editar
  `HUDController.cs`, que es del Agente A.)
- Aceptación: subir/bajar warp con teclas/indicador; en órbita x1000 sin derivar; cerca del
  suelo/propulsado limitado; HUD muestra nivel y tope.

### N11-detección — Crash (la parte de simulación)
- En `Universe.cs:204-214` (impacto de superficie): antes de hacer soft-rest, calcular la
  velocidad de impacto relativa a la superficie. **Si es impacto DURO** (p.ej. `impactSpeed > 12 m/s`
  o el vessel no está en config de aterrizaje controlado), marcar `vessel.IsDestroyed=true`,
  `vessel.CrashImpactSpeed=impactSpeed`, `vessel.CrashSimPosition=...`, y **dejar de integrarlo**
  (congelarlo en el punto de impacto, sin soft-rest). Si es **aterrizaje suave** (vel < umbral),
  mantener el soft-rest/LANDED actual (no romper la EDL).
- Aceptación: caer a alta velocidad marca el vessel como destruido con su velocidad de impacto; un
  aterrizaje suave controlado NO se marca.

### N6b — Pico de G alto en inserción (~5.3 g)
- Revisar el perfil PEG/throttle de `AscentController.cs` para no meter un pico de G alto al final
  (real ~3.5–4 g). Suavizar throttle/ángulo si procede.
- Aceptación: G de ascenso ≲ ~4.5 g; sigue llegando a órbita.

---

## AGENTE E · Explosión por impacto (N11-vfx)
**Archivos**: **nuevo** `ExplosionController.cs`, `MissionManager.cs`. **Lee** el contrato de crash
del Agente D (`ActiveVessel.IsDestroyed/CrashImpactSpeed`).
- **ExplosionController** (nodo): cada frame observa `SimulationBridge.Instance.ActiveVessel`. Al
  pasar `IsDestroyed` a true: spawnear una **explosión** en la posición de render del vessel —
  bola de fuego (GPUParticles3D + emisivo), **humo** y **escombros** (trozos que salen volando con
  física simple), flash de luz; ocultar el modelo del cohete (`VesselRenderer` Visible=false ó vía
  un flag — NO editar `VesselRenderer.cs`; si hace falta, ocultar el nodo del vessel por nombre).
  Reproducir sonido (`AudioManager` si existe método; si no, dejar hook).
- **MissionManager**: añadir fase `CRASHED` y mostrarla (event log "CRASHED / VEHICLE LOST"); que
  no siga la lógica de ascenso tras el crash.
- Aceptación: si el cohete impacta duro contra la Tierra, **explota** (fuego + humo + escombros +
  flash) y se destruye; un aterrizaje suave no explota; el HUD marca CRASHED.

---

## AGENTE F · Sistemas y recursos de misión (N9) — soporte vital + energía + térmico/comms
**Archivos (todos NUEVOS)**: `ExosphereSimulation/Systems/LifeSupportSystem.cs`, `PowerSystem.cs`,
`ThermalSystem.cs`, `CommsSystem.cs`; `scripts/SystemsController.cs` (nodo que los actualiza cada
frame leyendo el vessel/universo); `scripts/SystemsHUD.cs` (panel). **No editar `Vessel.cs`** — los
recursos viven en los objetos de sistema (asociados al vessel activo).
- **Soporte vital**: O2 (consumo por tripulante/seg), CO2 (acumulación + depuración), agua, comida;
  reservas, duración estimada, alertas y límite de supervivencia.
- **Energía**: paneles solares (potencia = f(ángulo al Sol, eclipse/sombra de la Tierra)), baterías
  (carga/descarga), consumo base de sistemas; en eclipse drena batería.
- **Térmico**: balance Sol vs radiadores/sombra, límites de temperatura, radiadores.
- **Comms**: enlace con la Tierra según línea de visión/distancia; retardo de señal; sin señal →
  aviso/pérdida de control del autopiloto (definir consecuencia suave para no frustrar).
- **SystemsHUD**: panel con barras de O2/CO2/agua/energía/temperatura/señal + alertas.
- Aceptación: los recursos se consumen/regeneran con tiempo y estado (Sol, eclipse, tripulación);
  el HUD los muestra con alertas; sin energía/señal hay consecuencia.

---

## AGENTE G · Fijar rumbo a otro planeta (N10) — intermedio (Hohmann editable + autopiloto)
**Archivos**: **nuevos** `TransferPlanner.cs`, `ManeuverExecutor.cs`; existentes
`MapViewController.cs`, `ManeuverPlanner.cs`. Usa solo API de lectura de `SimulationBridge`/
`Universe`/`Vessel`; aplica empuje vía `vessel.Orientation`/`vessel.Throttle`.
- **Selector de destino** en el mapa `[M]` (`MapViewController`): elegir cuerpo (Marte, Luna…).
- **TransferPlanner**: desde la órbita actual calcula una **transferencia tipo Hohmann** al cuerpo
  destino → crea un **nodo de maniobra** (Δv + tiempo) reutilizando `ManeuverPlanner`; el jugador
  puede **ajustarlo**; mostrar la trayectoria/encuentro resultante en el mapa.
- **ManeuverExecutor**: autopiloto que, llegado el momento del nodo, orienta la nave y ejecuta el
  burn (apaga al consumir el Δv del nodo).
- Aceptación: el jugador elige un planeta, aparece un nodo de transferencia editable + trayectoria;
  el autopiloto ejecuta el burn y la nave queda en curso al destino.

---

## 3. ORDEN Y DEPENDENCIAS
- **Sin dependencias (lanzar ya, en paralelo)**: A, B, C, F, G (archivos disjuntos).
- **Dependencia ligera**: E (explosión) **lee** el contrato de crash de D. Lanzar D y E juntos: E
  programa contra los nombres del contrato (sección 1) aunque D aún no haya terminado; integrar D
  antes que E al final. (Si se prefiere, lanzar D primero y E después.)
- **Prioridad sugerida**: 1) bugs molestos **A (N1,N2)** y **B (N3,N4)**; 2) **C** (llama); 3)
  **D+E** (warp + crash/explosión); 4) **F** y **G** (las más grandes, más diseño).
- Cada agente: build 0/0 en su worktree, NO correr Godot (lo verifica el principal), NO commitear,
  dejar cambios en el working tree, no editar `project.godot`, borrar scratch.
- Integración: el principal copia los archivos disjuntos al árbol principal, build 0/0, verifica con
  harness headless (despegue, ascenso, órbita, crash, warp, sistemas, transferencia), commit por tarea.

## 4. CRITERIOS GLOBALES DE "HECHO"
- [ ] N1: rejilla 33 (SH)/6 (Starship) según throttle; tally correcto.
- [ ] N2: navball sin cuadro marrón, recortado al disco, a cualquier actitud.
- [ ] N3: Tierra nítida desde ~150 km; N4: nubes con detalle/sombra; sin bloques.
- [ ] N5/N7: llama corta+brillante+diamantes+nube enorme en despegue; larga+tenue en vacío.
- [ ] N6: heading suave; pico de G ≲ 4.5 g.
- [ ] N8: warp x1…x1000+ con on-rails en órbita y límites cerca del suelo/propulsado.
- [ ] N9: recursos (O2/CO2/agua/energía/térmico/señal) se consumen y se muestran con consecuencias.
- [ ] N10: destino → nodo Hohmann editable + autopiloto que ejecuta.
- [ ] N11: impacto duro → explosión (fuego+humo+escombros) + CRASHED; aterrizaje suave no explota.
- [ ] Ambos proyectos 0/0; harness temporal eliminado; commits por tarea.
