# Exosphere — Plan de mejoras (Ronda 13)

> Documento de trabajo súper específico. Recoge TODO lo pedido + las respuestas a las
> preguntas de alcance. No es código todavía: es el contrato de lo que hay que hacer,
> con archivos, enfoque, criterios de aceptación y reparto por agentes.

---

## 0. Contexto técnico (leer antes de tocar nada)

- **Motor**: Godot 4.6.3 mono (C# / .NET 8). Binario:
  `/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64`
  Ejecutar headless: `--path . --rendering-driver opengl3`.
- **Escala de render**: `1 unidad = 2.8 m` (`MetresPerUnit = 2.8`). El stack completo
  (Super Heavy + Starship) ≈ 121 m ≈ 43 unidades; diámetro de núcleo 9 m ≈ 1.15 u radio.
- **Origen flotante**: el vessel activo se renderiza SIEMPRE en el origen
  (`scripts/FloatingOrigin.cs`). Los planetas son "scaled-space": esferas unitarias
  colocadas a `BackdropDistance = 50_000` u y escaladas al tamaño angular correcto.
  La Tierra de fondo se desvanece según la altitud de la CÁMARA (`planet_alpha`).
- **Sitio de lanzamiento**: Florida (27.5°N, 80.7°O) → orientado a +Y por
  `FloatingOrigin.PlanetTilt`. Verificado sobre tierra firme.
- **Builds (los dos deben quedar 0 errores / 0 warnings):**
  - `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet`
  - `dotnet build Exosphere.csproj --nologo -v quiet`
- **Patrón de prueba visual headless**: crear un autoload temporal `scripts/_XxxShot.cs`,
  añadirlo a `[autoload]` en `project.godot`, correr Godot headless, guardar PNGs en `/tmp`,
  revisarlos, y AL TERMINAR borrar el helper + `git checkout project.godot`. Nunca commitear
  el harness ni el autoload.
- **Reglas duras**: builds 0/0 antes de commitear · limpiar todo el código temporal ·
  no romper el camino [G] autopiloto a órbita (debe seguir llegando a órbita) ·
  mantener el estilo del archivo (comentarios mezcla ES/EN).

### Respuestas de alcance del usuario (incorporadas)
- **"El planeta se ve vacío"** → (a) el espacio negro está vacío: faltan estrellas, Vía
  Láctea, Sol y Luna visibles; (b) la superficie se ve sin detalle/vida; (c) **la nave se ve
  demasiado GRANDE en relación al planeta** (problema de escala/percepción); (d) **se llega de
  la Tierra a órbita baja demasiado rápido** (tiempo de ascenso poco realista).
- **Manejabilidad** → "mejórala en general" (respuesta de control + feedback visual).
- **Datos del HUD prioritarios** → **Motores en vivo**, **Cargas y trayectoria**,
  **Etapas y Δv** (además de la estética SpaceX y mejor cuenta atrás).

---

## T1 · Cohete suave y realista (no "figuras geométricas pegadas")

**Problema**: el modelo se ve como cilindros/cajas pegados, con cortes duros.
**Objetivo**: una Starship/Super Heavy que se lea como un vehículo real: cuerpo de acero
inoxidable continuo y suave, transiciones limpias, nariz ojival, aletas/flaps de Starship,
rejilla de 33 Raptors en el booster, anillos de soldadura sutiles, sin discontinuidades.

- **Archivos**: `scripts/VesselRenderer.cs` (modelo), opcional
  `assets/shaders/` para un shader de acero PBR (anisotrópico, reflejos).
- **Enfoque**:
  - Subir la subdivisión de los cilindros del cuerpo y unir secciones con bordes biselados
    (sin "tapas" visibles entre tramos). Nariz como ojiva tangente suave.
  - Booster: anillo realista de 33 motores (3 anillos: 13+10+10 o layout real) con campanas
    suaves; Starship: 3 sea-level + 3 vacuum (campanas grandes).
  - Aletas/flaps de Starship (2 delanteras + 2 traseras) y rejillas del booster.
  - Material acero inoxidable PBR (metallic alto, roughness variable, ligera anisotropía y
    reflejo del cielo/Tierra), con tizne cerca de motores.
  - NO romper la API de plumas (`_plumes.SetupSH/SetupStarship/Update`).
- **Aceptación**: a 50–100 m la nave se ve continua y "metálica de verdad", sin escalones
  geométricos; se distinguen flaps, rejilla de motores y campanas suaves. Builds 0/0.
- **Agente sugerido**: Agente A (modelado), aislado en `VesselRenderer.cs` (+ shader nuevo).

---

## T2 · Físicas de motores — investigar en internet y aplicar

**Problema**: el comportamiento de los motores es simplificado; el usuario quiere que se
investigue el Raptor real y se aplique fielmente.
**Objetivo**: comportamiento del Raptor 2 (metalox, full-flow staged combustion) realista.

- **Investigar (web) y documentar las fuentes** en el commit:
  - Empuje SL ≈ 230 tf (~2.26 MN) y vacío ≈ 258 tf; Isp ≈ 327 s SL / 350 s vac
    (RaptorVac ≈ 363–380 s). Presión de cámara ~300 bar. O/F (metalox) ≈ 3.6.
  - **Throttle real 40–100 %** (no por debajo de ~40 %).
  - **Transitorios de arranque/parada**: spool-up no instantáneo (ya hay rampa de 3 s; afinar
    con curva realista), apagado, y curva empuje-vs-presión-ambiente (sobre-expansión a nivel
    del mar → ganancia de empuje con la altitud).
  - Gimbal (~15°), número de motores por etapa, secuencia de encendido escalonado.
- **Archivos**: `ExosphereSimulation/Parts/Part.cs`, `data/parts/*.json`,
  `ExosphereSimulation/Vessel.cs`, `scripts/AscentController.cs` (throttle floor / Max-Q),
  `scripts/MissionManager.cs`.
- **Aceptación**: F = ṁ·Isp·g₀ coherente (ya lo es) + suelo de throttle 40 % respetado donde
  aplique (cuidado: la EDL usa throttle continuo bajo para aterrizar — no romperla) + curva
  empuje/Isp por presión documentada con fuentes en el mensaje de commit. Builds 0/0.
- **Agente sugerido**: Agente B (físicas/sim), aislado en `ExosphereSimulation/**` + JSON.
  **Tiene permiso de WebSearch/WebFetch** para investigar y citar fuentes.

> Relacionado con **T6 (tiempo a órbita)**: el mismo agente de física debe ajustar el perfil
> para que el ascenso a LEO dure de forma realista (ver T6).

---

## T3 · Gases del cohete (plumas + nube de despegue) — mucho más, realista

**Problema**: en los primeros segundos sale muchísimo menos humo/llama de lo real; las plumas
y la nube de diluvio son pobres.
**Objetivo**: despegue con MUCHO más volumen de gases, físicamente plausible.

- **Primeros segundos (0–10 s)**: nube de diluvio/escape ENORME que envuelve la base —
  vapor de agua del sistema de diluvio + polvo + escape, expandiéndose radialmente y subiendo
  en columna; debe dominar la pantalla al inicio y disiparse al subir.
- **Plumas de motor**: a nivel del mar, llama brillante sobre-expandida con discos de Mach;
  conforme sube y baja la presión, la pluma se ALARGA y ensancha; en vacío, pluma larga,
  tenue y muy expandida (casi invisible salvo el núcleo). 33 plumas en el booster.
- **Archivos**: `scripts/LaunchEffectsController.cs` (nube de diluvio/polvo),
  `assets/shaders/raptor_plume.gdshader` + sistema de plumas en `VesselRenderer.cs`/
  `scripts/Plumes*` (no romper `SetupSH/SetupStarship/Update`). GPUParticles3D.
- **Enfoque**: más partículas/turbulencia y emisión escalada por (throttle × densidad de
  motores) y por altitud (presión); columna de humo persistente; discos de Mach como geometría
  emisiva en la pluma; transición SL→vacío por presión ambiente.
- **Aceptación**: el despegue se ve "violento" y lleno de gases; la pluma cambia con la
  altitud; en órbita la pluma es la versión expandida de vacío. Rendimiento aceptable
  (cientos de partículas, no miles). Builds 0/0.
- **Agente sugerido**: Agente C (VFX), aislado en `LaunchEffectsController.cs`,
  `raptor_plume.gdshader` y el sistema de plumas. ⚠️ Coordinar con Agente A: ambos tocan
  `VesselRenderer.cs`. **Decisión**: el sistema de plumas se mueve/edita SOLO por el Agente C;
  el Agente A no toca las llamadas de plumas. Si hay riesgo de colisión, el Agente C trabaja
  después de A o en archivos de plumas separados.

---

## T4 · Visual del espacio (después de salir de la atmósfera)

**Problema**: el espacio se ve negro y vacío; la transición a espacio es sosa.
**Objetivo**: espacio "vivo" y cinematográfico (responde al punto (a) del usuario).

- **Estrellas reales**: skybox/starfield de campo estelar + **Vía Láctea** (textura
  equirectangular de cielo nocturno, p. ej. NASA/ESO, cargada como las texturas de Tierra).
- **Sol**: disco brillante con glare/halo (lens flare sutil), dirección consistente con
  `SunController`.
- **Luna**: cuerpo visible con su textura (ya existe `moon` en el universo) — asegurar que
  el backdrop la renderice con tamaño angular correcto y textura.
- **Tierra desde el espacio**: borde con dispersión atmosférica (glow azul en el limbo,
  airglow), terminador suave (ya mejorado), nubes con más presencia.
- **Archivos**: `scripts/SkyController.cs` (WorldEnvironment/Sky), `scripts/SunController.cs`,
  `scripts/PlanetMaterials.cs`, `assets/shaders/earth_surface.gdshader`,
  `assets/shaders/atmosphere.gdshader` (¿reactivar una cáscara de atmósfera para el limbo?),
  `scripts/FloatingOrigin.cs` (backdrop), nueva textura de estrellas en `assets/textures/`.
- **Aceptación**: al pasar ~80 km el fondo muestra estrellas + Vía Láctea, Sol con glare y la
  Luna; la Tierra tiene halo atmosférico en el limbo. Builds 0/0; sin coste de memoria
  excesivo (la textura de estrellas idealmente ≤ 8k).
- **Agente sugerido**: Agente D (espacio/cielo), aislado en `SkyController.cs`,
  `SunController.cs`, `atmosphere.gdshader`, textura de estrellas. ⚠️ Toca
  `PlanetMaterials.cs`/`earth_surface.gdshader`/`FloatingOrigin.cs` que también afectan a
  otros — **coordinar**: el limbo atmosférico de la Tierra lo hace D; el resto de
  `FloatingOrigin` no se toca salvo registro del starfield.

---

## T5 · Interfaz (HUD) + cuenta atrás — estética SpaceX + más datos

**Problema**: la UI se ve muy simple; la cuenta atrás es pobre.
**Objetivo**: HUD estilo transmisión SpaceX (oscuro, limpio, tipografía condensada) con los
datos que el piloto pidió.

- **Datos a mostrar (prioridad del usuario)**:
  - **Motores en vivo**: empuje total, **% por motor / rejilla de 33 motores encendidos**,
    TWR, Isp, consumo (t/s).
  - **Cargas y trayectoria**: fuerza **G**, **q dinámica** y aviso **Max-Q**,
    apoapsis/periapsis, ángulo de vuelo (pitch/heading), **downrange**, velocidad vertical.
  - **Etapas y Δv**: combustible por etapa, **Δv restante**, masa, eventos (cuenta de
    MECO/Separación/SECO).
  - Telemetría grande tipo webcast: velocidad + altitud + T+ centradas abajo.
- **Cuenta atrás**: secuencia tipo SpaceX (T- con hitos: "Startup", "Engine Chill",
  "Ignition", "Liftoff"), números grandes, beeps ya existentes (`AudioManager`).
- **Estética**: paneles oscuros translúcidos, acentos (azul/cian SpaceX), barras de combustible
  por etapa, líneas finas, esquinas marcadas; coherente en todas las pantallas.
- **Archivos**: `scripts/HUDController.cs`, `scripts/MissionManager.cs` (secuencia de
  cuenta atrás), `scripts/AscentController.cs` (ya expone q/TWR), posible nuevo
  `scripts/EngineGridHUD.cs`. Datos de motores/Δv desde `Vessel`/`PartGraph` (puede requerir
  exponer helpers de solo-lectura — coordinar con Agente B para no chocar).
- **Aceptación**: HUD claramente "SpaceX", con rejilla de motores, G, q, Max-Q, Ap/Pe,
  downrange, Δv por etapa y telemetría webcast; cuenta atrás con hitos. Builds 0/0.
- **Agente sugerido**: Agente E (UI), aislado en `HUDController.cs` + nuevo HUD de motores.
  ⚠️ Si necesita nuevos getters en `Vessel`/`PartGraph`, los pide al Agente B o se añaden en
  una fase de integración para evitar colisiones en `ExosphereSimulation/**`.

---

## T6 · Tiempo a órbita realista (se llega demasiado rápido) + escala

**Problema A (tiempo)**: se llega a LEO en ~4 min de tiempo de misión; real ≈ 8–9 min.
**Problema B (escala)**: la nave se percibe demasiado grande respecto al planeta.

- **Tiempo a órbita**:
  - Revisar el perfil del `AscentController`: gravity turn, throttle, y sobre todo el
    **auto-warp** (warp=2 en ascenso, 4 en coast) que acorta el reloj. Objetivo: que el
    **tiempo de misión** del ascenso sea realista (~8–9 min a SECO), reduciendo/eliminando el
    auto-warp durante el ascenso propulsado y/o ajustando el perfil de empuje.
  - Validar con telemetría: T+ a 150 km debería rondar 8–9 min, no 4.
  - **Archivos**: `scripts/AscentController.cs`, `ExosphereSimulation/Universe.cs`
    (TimeScale), `scripts/MissionManager.cs`.
- **Escala/percepción**:
  - Verificar que el backdrop subtiende el tamaño angular correcto (`FloatingOrigin`:
    `rBackdrop = BackdropDistance·sin(asin(R/d))`) y que `MetresPerUnit=2.8` es coherente en
    nave, suelo y plataforma. Confirmar el **FOV de la cámara** (`Camera3D.Fov`) — un FOV muy
    estrecho agranda la nave respecto al fondo.
  - Si la proporción es físicamente correcta pero "se siente" grande, ajustar FOV y/o el
    encuadre de cámara para una sensación realista, SIN romper la proporción física.
  - **Archivos**: `scripts/CameraController.cs`, `scripts/FloatingOrigin.cs`.
- **Aceptación**: T+ a 150 km ≈ 8–9 min; la nave se ve proporcional al planeta (no
  desproporcionadamente grande) manteniendo la escala física. Builds 0/0; el [G] sigue
  llegando a órbita.
- **Agente sugerido**: lo hace el **agente principal (yo)** porque toca el lazo de ascenso y
  cámara (delicado, interactúa con T2/T5) — no en paralelo ciego.

---

## T7 · Manejabilidad de la nave (mejorar en general: respuesta + feedback)

**Problema**: el control manual no se siente bien; falta feedback.
**Objetivo**: control preciso y con instrumentos.

- **Respuesta de control**: afinar `ControlAuthority` (hoy 0.6 rad/s²), límite de velocidad
  angular, y la amortiguación del SAS; considerar respuesta proporcional con rampa.
- **Feedback visual**: **navball / indicador de actitud** (pitch/yaw/roll), marcadores
  **prograde/retrograde**, vector de empuje, indicador de horizonte, heading.
- **Modos SAS**: hold de actitud, prograde-hold, retrograde-hold, radial, normal.
- **Archivos**: `scripts/Vessel.cs` (control), `scripts/CameraController.cs`,
  `scripts/HUDController.cs` o nuevo `scripts/NavballController.cs`.
- **Aceptación**: con la nave se puede apuntar con precisión, hay navball + marcadores
  prograde/retrograde, y al menos prograde-hold funcional. Builds 0/0.
- **Agente sugerido**: parte del **Agente E (UI)** para el navball/feedback + el agente
  principal para el tuning de control en `Vessel.cs` (coordinar con T2).

---

## Reparto por agentes (archivos no solapados)

| Agente | Tareas | Archivos exclusivos | Permisos |
|--------|--------|---------------------|----------|
| **A — Modelo nave** | T1 | `scripts/VesselRenderer.cs` (sin tocar plumas), shader de acero nuevo | — |
| **B — Físicas motor/sim** | T2, apoyo T6 | `ExosphereSimulation/**`, `data/parts/*.json` | **WebSearch/WebFetch** |
| **C — VFX gases/plumas** | T3 | `LaunchEffectsController.cs`, `raptor_plume.gdshader`, sistema de plumas | — |
| **D — Espacio/cielo** | T4 | `SkyController.cs`, `SunController.cs`, `atmosphere.gdshader`, textura estrellas | descarga texturas |
| **E — UI/HUD/navball** | T5, parte T7 | `HUDController.cs`, nuevos `EngineGridHUD.cs`/`NavballController.cs` | — |
| **Principal (yo)** | T6 (tiempo+escala), tuning control T7, integración y verificación | `AscentController.cs`, `CameraController.cs`, `Vessel.cs` (control), integración | — |

**Conflictos a vigilar**:
- `VesselRenderer.cs`: A (modelo) vs C (plumas) → C solo el sistema de plumas, A no toca plumas.
- `Vessel.cs`/`PartGraph`: B (física) vs E (getters para HUD) vs principal (control) →
  los getters de solo-lectura que pida E se añaden en la fase de integración por el principal.
- `FloatingOrigin.cs`/`PlanetMaterials.cs`/`earth_surface.gdshader`: D (espacio) vs principal
  (escala) → D solo limbo atmosférico + registro de starfield; principal solo backdrop/escala.

**Orden sugerido**: lanzar A, B, C, D en paralelo (archivos disjuntos). E después de definir
los getters de datos con B. T6/T7-tuning e integración final por el principal, con verificación
headless (harness temporal) y commit por tarea. Builds 0/0 obligatorio en cada paso.

---

## Criterios de "hecho" (global)
- [ ] Ambos proyectos compilan 0 errores / 0 warnings.
- [ ] El [G] autopiloto sigue llegando a órbita; T+ a 150 km ≈ 8–9 min.
- [ ] Verificación headless con capturas para cada cambio visual (despegue, ascenso, espacio,
      órbita, HUD, navball) y revisadas.
- [ ] Todo el código/harness temporal eliminado; `project.godot` sin el autoload de prueba.
- [ ] Fuentes de la investigación de motores citadas en el commit de T2.
- [ ] Commits separados y descriptivos por tarea.
