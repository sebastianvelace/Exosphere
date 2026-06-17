# Exosphere — Plan de mejoras (post-Ronda 13)

> Estado: la Ronda 13 (multi-agente) ya está integrada y commiteada. Este documento ahora SOLO
> lista lo que FALTA: bugs y mejoras detectados en pruebas en vivo, con archivo·línea·mecanismo
> y criterios de aceptación. Súper específico para poder ejecutarlo directo.

## ✅ Hecho en Ronda 13 (no rehacer)
Modelo de acero suave + nariz ojival + 33 bells curvas · físicas de motor (getters de telemetría
+ suelo de throttle Raptor inerte) · plumas con Mach diamonds que escalan con altitud + nube de
despegue mayor · **espacio con estrellas/Vía Láctea + Sol con glow (excelente) + Luna + limbo
atmosférico** · HUD estilo SpaceX + navball + rejilla de motores · ascenso a tiempo real (warp 1).
El ascenso [G] **llega a órbita** correctamente. La escala nave/planeta quedó proporcional.

---

## 0. Contexto técnico (igual que siempre)
- Godot 4.6.3 mono · escala `1 u = 2.8 m` · vessel en el origen (FloatingOrigin) · planetas
  scaled-space · sitio de lanzamiento 27.5°N/80.7°O (tierra).
- Builds 0/0 obligatorio: `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet`
  y `dotnet build Exosphere.csproj --nologo -v quiet`.
- Binario Godot headless: `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 --path . --rendering-driver opengl3`.
- Patrón de prueba: autoload temporal `scripts/_XxxShot.cs` → correr headless → PNGs en /tmp →
  revisar → BORRAR helper + `git checkout project.godot`. Nunca commitear el harness.
- **IMPORTANTE sobre la simulación de motores**: el sim modela cada ETAPA como **UNA sola
  parte-motor** (el Super Heavy = 1 parte que produce 74 MN; Starship = 1 parte). El "33" y el
  "6" son solo el MODELO visual. Por eso `Parts.ActiveEngines.Count == 1`. Cualquier HUD que
  cuente motores debe derivar la cuenta nominal por etapa, no de `ActiveEngines.Count`.

---

## N1 · Rejilla de motores muestra "1/33" (solo 1 motor encendido) — BUG
**Síntoma** (capturas de despegue y órbita): el panel PROPULSION enciende 1 punto y dice
"1/33"; en la etapa de Starship sigue mostrando 33 y "1/33".
**Mecanismo**: `scripts/EngineGridHUD.cs:70-80` — `_totalActiveEngines = engines.Count` (= 1, una
parte-motor por etapa) → `_litEngines = clamp(1, 0, TotalEngines)= 1`. `TotalEngines` es la
constante 33 fija.
**Fix**:
- Encender puntos = `round(N_nominal · throttle)` cuando hay propelente, donde `N_nominal` es la
  cuenta NOMINAL de motores de la etapa activa: **33** si la etapa activa contiene
  `super_heavy_booster`, **6** si es la etapa de Starship (3 SL + 3 vac). Detectar por
  `Definition.Id` de las partes de `CurrentStageParts()` o de `ActiveEngines`.
- Dibujar el layout correcto por etapa: 33 (3/10/20) para SH, 6 para Starship.
- El tally debe leer p.ej. "33/33" a empuje pleno, "20/33" a ~60%, "6/6" en Starship.
**Archivo**: `scripts/EngineGridHUD.cs` (constante/­layout por etapa + cuenta nominal).
**Aceptación**: SH a 100% → 33 encendidos; Starship → 6; throttle parcial → proporcional; el
número y el layout cambian al separar etapas.

---

## N2 · Navball: aparece un CUADRO marrón/rojo (horizonte sin recortar) — BUG
**Síntoma** (capturas a >2 km): junto/detrás del disco azul del navball aparece un cuadrado
marrón que no encaja en el círculo; el medidor "no se ve bien".
**Mecanismo**: `scripts/AttitudeNavball.cs:181-205` — el propio código admite *"Godot's immediate
_Draw has no clip... we approximate with polys sized to the disc and rely on the bezel ring to
hide the corners"*. Dibuja quads de cielo/suelo (`DrawColoredPolygon`, líneas 201-202) más
grandes que el disco y el anillo del bisel (línea ~204) es demasiado fino para tapar las esquinas
→ el quad de suelo (marrón) se ve como cuadrado.
**Fix** (elegir uno, recomendado el primero):
1. Tras dibujar cielo/suelo, tapar de verdad: dibujar un **anillo relleno** del color del panel
   desde `Radius` hasta un radio grande (p.ej. `DrawArc`/triángulos en abanico, o un polígono
   tipo dona) que oculte TODO lo que esté fuera del disco. Luego el bisel fino encima.
2. O recortar los polígonos cielo/suelo a la circunferencia (intersección polígono-círculo).
3. O renderizar el navball en un `SubViewport` con máscara circular / un `Control` con
   `clip_contents` + máscara.
Además: pulir el look (escalera de pitch dentro del disco, marcadores prograde/retrograde/radial
nítidos, línea de horizonte).
**Archivo**: `scripts/AttitudeNavball.cs`.
**Aceptación**: a cualquier roll/pitch, el horizonte cielo/suelo queda DENTRO de un disco limpio,
sin ningún cuadrado marrón visible.

---

## N3 · Tierra desde órbita aún se ve pixelada/blanda (~150 km) — VISUAL
**Síntoma** (capturas en órbita): la superficie y las nubes se ven borrosas/en bloques al mirar
de cerca desde ~150 km.
**Mecanismo**: `assets/shaders/earth_surface.gdshader:54` muestrea `day_tex` (8K) directo; a
150 km cada texel (~5 km) se magnifica sin ningún detalle procedural que lo rompa → borroso.
**Fix**:
- Añadir **detalle fbm de alta frecuencia** que module contraste/brillo (y opcionalmente un
  bump/relieve) del color base, mezclado por distancia/zoom, para romper la magnificación de
  texels (igual que ya hace `earth_ground.gdshader` de cerca). Mantenerlo sutil para no
  "ensuciar" la vista lejana.
- Opcional (si hace falta): textura de día 16K (⚠️ memoria ~3×400 MB — evaluar; quizá solo día) o
  un mapa de detalle tileable de terreno/océano.
**Archivo**: `assets/shaders/earth_surface.gdshader`.
**Aceptación**: a ~150 km la superficie se lee con micro-detalle nítido, sin bloques borrosos;
la vista lejana (planeta completo) sigue limpia.

---

## N4 · Nubes poco realistas — VISUAL
**Síntoma**: las nubes desde órbita se ven blancas, planas y en bloques; de cerca, los cuerpos
de agua del parche de suelo se ven como manchas azules borrosas.
**Mecanismo**: `assets/shaders/earth_surface.gdshader:56,62-63` — nubes = `mix(dayCol, vec3(1.0),
cloud_tex.r·cloud_amount·day·0.85)`: blanco plano sin sombra ni volumen, magnificado desde la
textura 8K. El parche de suelo (`earth_ground.gdshader`) no tiene capa de nubes.
**Fix**:
- Nubes orbitales: añadir **detalle fbm** sobre la cobertura (rompe el bloque), ligera
  translucidez/penumbra, **sombra de nube proyectada** sobre la superficie (oscurecer el día bajo
  nubes), y deriva animada más natural. Tono no 100% blanco (gris cálido en bordes).
- Opcional: capa fina de nubes/penumbra en `earth_ground.gdshader` para baja altitud, y revisar
  que las manchas azules de agua se vean como agua (specular/borde) y no como nubes.
**Archivos**: `assets/shaders/earth_surface.gdshader` (principal), opcional `earth_ground.gdshader`.
**Aceptación**: nubes con detalle/volumen y sombra sobre la Tierra; nada de manchas blancas en
bloque; el agua de baja altitud se lee como agua.

---

## N5 · Humo de despegue aún insuficiente los primeros segundos — VFX
**Síntoma** (captura de LIFTOFF a 36 m): sale fuego pero la nube no DOMINA la pantalla como en un
lanzamiento real de Starship; el usuario insiste en "muchísimo más" al inicio.
**Mecanismo**: `scripts/LaunchEffectsController.cs` — la nube ya se agrandó en Ronda 13 pero falta
volumen/altura/persistencia y un frente de polvo radial más violento en los primeros ~5 s.
**Fix**: más partículas de vapor/polvo, mayor escala y buoyancy, columna más alta y duradera,
frente de polvo radial a ras de suelo más amplio; mantener rendimiento (≤ ~700 partículas).
**Archivo**: `scripts/LaunchEffectsController.cs`.
**Aceptación**: a 0–3 s la base queda envuelta en una nube enorme que domina el encuadre y se
disipa al subir.

---

## N6 · Menores (revisar/pulir)
- **HEADING del HUD salta** (275°→54°→90°→97°→109°… durante el ascenso): revisar el cálculo de
  rumbo en `scripts/HUDController.cs`/`AttitudeNavball.cs` (proyección del vector velocidad/eje).
- **G-force pico ~5.3 g** en inserción (real ~3.5–4 g): revisar si el perfil de empuje/PEG de
  `scripts/AscentController.cs` mete un pico de G alto al final; suavizar si procede.
- Confirmar que la rejilla y el navball se ven bien a la resolución real (no solo headless).

---

---

# Nuevas funciones (Ronda 14) — pedido del usuario

## N7 · Llama del cohete REALISTA por presión atmosférica (ambos modelos) — VFX
**Pedido**: al despegue la llama debe ser MUCHO más grande; conforme sube, "menos". El usuario
quiere **el modelo más realista**, con la versión de **nivel del mar** (corta pero intensa) y la
de **vacío** (larga y tenue) — *"quiero ambos modelos, en el espacio y en la tierra"*.

**Física/realismo de referencia** (de footage real de Starship/Super Heavy):
- **Nivel del mar (despegue)**: escape **sobre-expandido** → llama relativamente CORTA pero
  **muy brillante/ancha**, naranja-blanca, con **discos de Mach (shock diamonds)** marcados y
  apretados cerca de la tobera, envuelta en una **nube de polvo/vapor ENORME** que domina la
  base. (33 Raptors → "the mother of all shock diamonds".)
- **Conforme sube y baja la presión**: el ángulo del choque se afina, los diamantes se separan y
  se suavizan; la llama se **alarga** y aclara.
- **Vacío**: escape **perfectamente/ sub-expandido** → pluma **larga, ancha y TENUE/translúcida**
  (núcleo brillante fino + halo difuso azulado), sin polvo, casi sin diamantes. Se ve "menos"
  porque es difusa, aunque geométricamente sea más larga.
- Mejorar la calidad visual de la llama corta de despegue (no que se vea pobre por ser corta):
  núcleo incandescente, gradiente de color realista, turbulencia.

**Imágenes/footage de referencia (añadir al revisar):**
- TWZ — "Starship's 33 Engines Created The Mother Of All 'Shock Diamonds'":
  https://www.twz.com/starships-33-engines-created-the-mother-of-all-shock-diamonds
- Everyday Astronaut — Starship/Super Heavy Flight (plumas SL→altura, bells SL vs vacío):
  https://everydayastronaut.com/starship-super-heavy-flight-4/
- Wikipedia — SpaceX Starship (3 SL + 3 vacuum Raptor, campanas alargadas):
  https://en.wikipedia.org/wiki/SpaceX_Starship

**Archivos**: `assets/shaders/raptor_plume.gdshader` (curva longitud/brillo/diamantes vs presión),
`scripts/PlumeSystem.cs` (escala por throttle × densidad), `scripts/LaunchEffectsController.cs`
(nube de polvo gigante de despegue — ligado a N5).
**Aceptación**: despegue = llama corta MUY brillante con diamantes + nube enorme; en altura la
llama se alarga; en vacío pluma larga y tenue. Transición continua con la altitud/presión.

## N8 · Botón de acelerar tiempo estilo KSP (x1…x1000+) — UI + SIM
**Pedido**: botones de warp x2/x3/x10… **Estilo KSP (hasta x1000+)**.
**Diseño**:
- Niveles: x1, x2, x3, x5, x10, x50, x100, x1000 (configurable). UI con botones/indicador y
  teclas `[.]`/`[,]` (ya existen) + el botón.
- **Sobre-rieles en órbita**: a warp alto, poner la nave en órbita kepleriana exacta
  (`IsOnRails` + `OrbitalElements` ya existen en `Vessel`) en vez de integrar paso a paso.
- **Límite por altitud / propulsión**: warp alto BLOQUEADO cerca del suelo, en atmósfera densa o
  con motores encendidos (como KSP). Mostrar el warp máximo permitido según el estado.
- Respetar/!coordinar! con el auto-warp del `AscentController` (que ahora usa warp 1 en
  propulsado, 4 en coast).
**Archivos**: `scripts/HUDController.cs` (botón/indicador), `ExosphereSimulation/Universe.cs`
(niveles + on-rails + sub-stepping), `scripts/SimulationBridge.cs` (`SetTimeScale`), posible
`scripts/WarpController.cs`.
**Aceptación**: el jugador sube/baja warp con botón y teclas; en órbita llega a x1000 sin que la
órbita derive; cerca del suelo/propulsado el warp se limita; el HUD muestra el nivel y el tope.

## N9 · Recursos y sistemas de misión realista — SIM + UI
**Pedido (prioridad del usuario)**: **Soporte vital**, **Energía**, **Térmico y comunicaciones**
(NO se pidió tripulación/EVA por ahora).
**Sistemas**:
- **Soporte vital**: O2 (consumo por tripulante), CO2 (acumulación/depuración), agua, comida;
  alertas y límites de supervivencia; reservas y duración estimada.
- **Energía**: paneles solares (potencia según ángulo al Sol y eclipse/sombra de la Tierra),
  baterías (carga/descarga), consumo de sistemas; modo eclipse drena batería.
- **Térmico**: balance de calor (Sol vs radiadores/sombra), límites de temperatura, radiadores.
- **Comunicaciones**: antena + enlace con la Tierra (según línea de visión/distancia), retardo de
  señal y **pérdida de control del autopiloto sin señal** (o aviso).
**Archivos**: nuevos en `ExosphereSimulation/Systems/` (p.ej. `LifeSupportSystem.cs`,
`PowerSystem.cs`, `ThermalSystem.cs`, `CommsSystem.cs`) integrados en `Vessel.Tick`; datos de
recurso en `Part`/`PartDefinition`/JSON; nuevo panel de HUD `scripts/SystemsHUD.cs`.
**Aceptación**: los recursos se consumen/regeneran con el tiempo y el estado (Sol, eclipse,
tripulación); el HUD muestra O2/CO2/agua/energía/temperatura/señal con barras y alertas; quedarse
sin energía o señal tiene consecuencias.

## N10 · Fijar rumbo a otro planeta — INTERMEDIO — SIM + UI
**Pedido**: elegir planeta destino → **nodo de maniobra Hohmann automático editable** + autopiloto
que lo ejecuta. (Ya existe un planificador de maniobras de la Semana 10 — reutilizar.)
**Diseño**:
- Selector de **destino** (Marte, Luna, etc. — cuerpos del universo) en el mapa/HUD.
- Calcular una **transferencia tipo Hohmann** desde la órbita actual: crear un **nodo de
  maniobra** (Δv, tiempo) que el jugador puede **ajustar**; mostrar la trayectoria resultante.
- **Autopiloto** que orienta y ejecuta el burn del nodo en el momento correcto.
- (Intermedio: no hace falta ventana de lanzamiento/porkchop fino, pero sí una transferencia
  válida y editable.)
**Archivos**: `scripts/ManeuverPlanner*`/mapa orbital existentes (Semana 10), nuevo
`scripts/TransferPlanner.cs` (Hohmann hacia el cuerpo destino), UI de selección de destino en el
mapa `[M]`, autopiloto de ejecución de nodo (extender `AscentController`/nuevo
`ManeuverExecutor.cs`).
**Aceptación**: el jugador elige un planeta, aparece un nodo de transferencia editable y la
trayectoria de encuentro; el autopiloto ejecuta el burn y la nave queda en curso al destino.

---

## Reparto sugerido (archivos disjuntos → se puede paralelizar con agentes)
| Frente | Tareas | Archivos exclusivos |
|--------|--------|---------------------|
| HUD-bugs | N1, N2, N6(heading) | `scripts/EngineGridHUD.cs`, `scripts/AttitudeNavball.cs`, `scripts/HUDController.cs` |
| Tierra/nubes | N3, N4 | `assets/shaders/earth_surface.gdshader` (+ opcional `earth_ground.gdshader`) |
| VFX-llama | N5, N7 | `assets/shaders/raptor_plume.gdshader`, `scripts/PlumeSystem.cs`, `scripts/LaunchEffectsController.cs` |
| Ascenso | N6(G-force) | `scripts/AscentController.cs` |
| Time-warp | N8 | `ExosphereSimulation/Universe.cs`, `scripts/SimulationBridge.cs`, `scripts/WarpController.cs` (HUD del warp: coordinar con HUD-bugs) |
| Sistemas | N9 | `ExosphereSimulation/Systems/*` (nuevos), `scripts/SystemsHUD.cs` (nuevo), JSON de partes |
| Interplanetario | N10 | `scripts/TransferPlanner.cs`/`ManeuverExecutor.cs` (nuevos), mapa orbital `[M]` |

**Orden recomendado**:
1. **Bugs primero**: N1, N2 (HUD), N3, N4 (Tierra) — son lo que más molesta ahora mismo.
2. **VFX**: N5+N7 (llama realista por presión) — un solo frente.
3. **Funciones nuevas grandes** (más diseño): N8 (warp), N9 (sistemas/recursos), N10 (interplanetario).
Los frentes tienen archivos disjuntos → paralelizables con agentes (ojo: N8 y N1/N2 comparten
HUD → coordinar el panel del warp). Verificar con harness headless y commitear por tarea. 0/0 siempre.

## Criterios de "hecho"
- [ ] Rejilla enciende 33 (SH) / 6 (Starship) según throttle; tally correcto.
- [ ] Navball sin cuadro marrón, recortado limpio al disco, a cualquier actitud.
- [ ] Tierra nítida desde ~150 km; nubes con detalle/sombra; sin bloques.
- [ ] Llama realista: despegue corta+brillante+diamantes+nube enorme; vacío larga+tenue; transición por presión.
- [ ] Botón/teclas de warp x1…x1000+ con on-rails en órbita y límites cerca del suelo/propulsado.
- [ ] Recursos (O2/CO2/agua/energía/térmico/señal) se consumen y se muestran con alertas y consecuencias.
- [ ] Selección de planeta destino → nodo Hohmann editable + autopiloto que ejecuta el burn.
- [ ] Ambos proyectos 0/0; harness temporal eliminado; commits por tarea.
