# Exosphere — Space Mission Simulator

Simulador de misión completo estilo SpaceX Starship: cuenta atrás en Starbase → órbita →
interplanetario → aterrizaje en Marte. Física real (masas, ISP, mecánica orbital), escala del
sistema solar real, pilotaje manual + autopiloto opcional.

**Stack:** Godot **4.6.3** (mono) · C# / **.NET 8** · librería de física propia en doble precisión.

---

## Build & Run (OBLIGATORIO build 0/0 tras cada cambio de C#)

```bash
# 1. Librería de simulación (pura C#, sin Godot)
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet
# 2. Capa de juego (Godot)
dotnet build Exosphere.csproj --nologo -v quiet
```

Ambos builds deben salir **0 errores / 0 warnings**. Si tocas la capa sim, recompila las dos
(la de juego referencia la de sim).

**Ejecutar en Godot (headless):**
```bash
/home/sebasvelace/Downloads/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64 \
  --path . --rendering-driver opengl3
```

- Escena principal: `scenes/flight/Flight.tscn`
- Physics tick: **50 Hz** · sub-paso máximo de física: `0.02 s`
- No hay tests automatizados. La verificación es: **build 0/0** + **captura visual headless** (ver
  skill `visual-testing`).

---

## Arquitectura — dos ensamblados, una frontera

El proyecto se divide en DOS proyectos C# con namespaces distintos. **Respetar la frontera es la
regla número uno.**

### 1. `ExosphereSimulation/` — librería de física PURA
- Namespace: `Exosphere.Simulation` (y sub-namespaces `.Math`, `.Integrators`, `.Physics`,
  `.Systems`, `.Parts`).
- **CERO dependencias de Godot.** Nunca importar `Godot` aquí. Debe poder compilar y testearse sola.
- Doble precisión en todo: tipos propios `Vector3d`, `Quaterniond` (NO los `Vector3`/`Quaternion`
  de Godot, que son float).
- **Unidades SI siempre**: metros, m/s, kg, segundos. Tiempo = **segundos desde J2000**.
  `GM` = μ = GM en m³/s². Ángulos internos en radianes (los JSON usan grados, se convierten al cargar).
- Piezas clave: `Universe` (raíz, posee cuerpos y vessels, avanza el tiempo, despacha al integrador
  según el warp), `Vessel`, `CelestialBody`, `OrbitalElements`, integradores `RK4Integrator` y
  `KeplerPropagator`.

### 2. `scripts/` (proyecto `Exosphere.csproj`) — capa de juego Godot
- Namespace: `Exosphere.Game`. Aquí SÍ se usa `Godot`.
- El `.csproj` excluye `ExosphereSimulation/**/*.cs` y referencia la librería como proyecto.
- Convierte estado de doble precisión → render en float (ver `FloatingOrigin`).

### La frontera: `SimulationBridge`
- Singleton (`SimulationBridge.Instance`). Es el ÚNICO punto de contacto juego↔sim.
- Posee el `Universe`, expone `ActiveVessel`, el API de time-warp, y emite señales
  (`VesselStaged`, `VesselDestroyed`, `SimulationLoaded`).
- Los controladores de la capa de juego LEEN desde `SimulationBridge.Instance`; evitan editar la
  capa sim directamente.

---

## Conceptos clave / GOTCHAS (leer antes de tocar física o render)

- **Escala de render: `1 unidad Godot = 2.8 m`** (`MetresPerUnit`). El vessel activo se renderiza
  en el ORIGEN; `FloatingOrigin` reubica el mundo a su alrededor cada frame para mantener
  coordenadas pequeñas (precisión float, sin z-fighting).
- **Planetas = scaled-space backdrop**: esferas unitarias renderizadas a distancia FIJA
  (`BackdropDistance = 50 000 u`) y escaladas para subtender su tamaño angular correcto. No están a
  su distancia real en el render.
- **Cada ETAPA del cohete es UNA sola parte-motor en el sim.** Super Heavy = 1 parte (≈74 MN),
  Starship = 1 parte. Los "33 Raptors" y "6 motores" son SOLO el modelo VISUAL.
  `Parts.ActiveEngines.Count == 1` por etapa. No confundir el modelo visual con el físico.
- **Impacto con superficie**: hoy `ExosphereSimulation/Universe.cs` hace un "soft-rest" en cualquier
  impacto (si `altitude < 0` reubica a `Radius + 1` y aplica la velocidad de superficie). Por eso un
  cohete "aterriza" derecho sin estrellarse. Tenerlo en cuenta al tocar crash/EDL.
- **Time-warp**: `SimulationBridge.WarpLevels = {1,2,3,5,10,50,100,1000,10000,100000}`. Vessel activo
  integra con RK4; el resto va "on rails" (Kepler exacto). El `MaxAllowedWarpIndex` depende de
  altitud/propulsión.
- **Sitio de lanzamiento**: Cabo Cañaveral ≈ 27.5°N / 80.7°O (verificado sobre tierra firme en la
  textura Blue Marble; el cohete sale volando sobre el Atlántico).

---

## Datos (data-driven, JSON en `data/`)

Cuerpos, piezas y sitios de lanzamiento se cargan desde JSON — **no hardcodear constantes físicas en
código**, van en los datos.
- `data/bodies/*.json` — cuerpos celestes: `mass`, `radius`, `gm`, `soi`, `rotational_period`,
  `axial_tilt`, `atmosphere` (con `layers` de la atmósfera estándar), `orbital_elements`.
- `data/parts/*.json` — piezas: `mass_dry`, `cost`, `drag_coefficient`, `heat_tolerance`,
  `attachment_nodes`, etc.
- `data/launch_sites/*.json` — sitios de lanzamiento (lat/lon).

Al añadir contenido nuevo, copiar el esquema de un archivo existente del mismo tipo. Ver skill
`data-content`.

---

## Estilo de código

- **Comentarios bilingües ES/EN** — sigue lo que ya hay en cada archivo. Los comentarios explican el
  *por qué* (física, decisiones de escala), no el *qué* obvio.
- `Nullable enable` e `ImplicitUsings enable` en ambos proyectos. Anotaciones de nullabilidad reales.
- Alineación vertical de campos/propiedades cuando el archivo vecino ya lo hace (es común aquí).
- XML doc (`/// <summary>`) en tipos y miembros públicos de la librería sim.
- Sim en inglés para nombres públicos (contratos), comentarios pueden ir en ES.

## Commits

Conventional commits con scope, en una línea descriptiva. Ejemplos reales del repo:
`feat: ...`, `fix(fpv): ...`, `polish(fpv): ...`, `feat(map): ...`.
Un commit por tarea. No commitear helpers de captura ni cambios temporales en `project.godot`.

## Flujo de trabajo multi-agente

`PLAN_MEJORAS.md` reparte el trabajo en **agentes con archivos EXCLUSIVOS sin solapamiento** para
correr en paralelo, con contratos de interfaz (nombres EXACTOS) entre ellos. Antes de un cambio
grande, mira si hay un plan vigente ahí y respeta los contratos y la propiedad de archivos.

## No tocar / cuidado

- No romper el ascenso **[G]** a órbita ni la **EDL** de aterrizaje (usa throttle profundo).
- No importar `Godot` dentro de `ExosphereSimulation/`.
- No commitear el harness de captura visual ni el autoload temporal (`scripts/_*Shot.cs`); restaurar
  `project.godot` con `git checkout` después.
- Los `*.cs.uid`, `.godot/`, `bin/`, `obj/` son generados — no editarlos a mano.
