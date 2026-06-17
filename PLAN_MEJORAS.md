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

## Reparto sugerido (archivos disjuntos → se puede paralelizar con agentes)
| Frente | Tareas | Archivos exclusivos |
|--------|--------|---------------------|
| HUD | N1, N2, N6(heading) | `scripts/EngineGridHUD.cs`, `scripts/AttitudeNavball.cs`, `scripts/HUDController.cs` |
| Tierra/nubes | N3, N4 | `assets/shaders/earth_surface.gdshader` (+ opcional `earth_ground.gdshader`) |
| VFX | N5 | `scripts/LaunchEffectsController.cs` |
| Ascenso | N6(G-force) | `scripts/AscentController.cs` |

**Orden**: los 4 frentes son disjuntos → paralelizables. Prioridad: **N1 y N2** (bugs visibles del
HUD) y **N3/N4** (la Tierra de cerca). Verificar cada cambio con harness headless (despegue,
ascenso, órbita) y commitear por tarea. Builds 0/0 siempre.

## Criterios de "hecho"
- [ ] Rejilla enciende 33 (SH) / 6 (Starship) según throttle; tally correcto.
- [ ] Navball sin cuadro marrón, recortado limpio al disco, a cualquier actitud.
- [ ] Tierra nítida desde ~150 km; nubes con detalle/sombra; sin bloques.
- [ ] Nube de despegue domina los primeros segundos.
- [ ] Ambos proyectos 0/0; harness temporal eliminado; commits por tarea.
