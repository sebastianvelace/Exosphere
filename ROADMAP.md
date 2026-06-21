# Exosphere Roadmap

Estado de cierre actual:
- Builds .NET/Godot pasan con 0 warnings y 0 errores.
- `ExosphereSimulation.Tests` cubre gravedad, RK4, Kepler, radial/suborbital, rails-impact, motores, termica de escudo, aerodinamica y SOI.
- Godot headless carga la escena principal sin errores.

## VAB / Construccion De Naves

Objetivo: un VAB minimo pero usable, data-driven y compatible con el `SimulationBridge` actual.

- Crear `scenes/construction/Construction.tscn`.
- Anadir `ConstructionController`.
- Cargar catalogo desde `data/parts/*.json`, sin hardcodear piezas.
- Permitir ensamblaje vertical inicial:
  - seleccionar pieza,
  - adjuntar a nodo compatible,
  - borrar pieza,
  - recalcular masa, propelente, TWR y delta-v.
- Exportar una definicion de vessel que `SimulationBridge` pueda instanciar en plataforma.
- Mantener V1 bajo el contrato actual de una parte-motor por etapa.
- Tests minimos:
  - carga catalogo,
  - valida nodos de attachment,
  - calcula masa/propelente/delta-v,
  - rechaza conexiones incompatibles.

## Reentry Fisico Y Visual Avanzado

Objetivo: convertir reentry en un sistema de dano, control y VFX, no solo en una condicion de destruccion.

- Separar dano termico de ground crash.
- Anadir estado de dano termico progresivo por pieza:
  - temperatura,
  - ratio sobre tolerancia,
  - pieza quemada,
  - perdida de control si falla estructura critica.
- Conectar VFX a heat flux real:
  - plasma por flujo termico,
  - brillo en zona windward,
  - dano/oscurecimiento de tiles,
  - breakup si no hay escudo o actitud correcta.
- EDL debe orientar belly-first con el heat shield al flujo y luego hacer flip-and-burn.
- Tests:
  - entrada nominal sobrevive,
  - entrada sin escudo destruye,
  - entrada mal orientada destruye,
  - heating crece con `sqrt(rho) * v^3`.

## Starship Visual Fidelity

Objetivo: que la nave se lea mas claramente como Starship/Super Heavy sin cambiar todavia el contrato fisico de etapas.

- Mejorar geometria procedural:
  - proporciones de 9 m y altura realista,
  - nariz ojival mas limpia,
  - hot-stage ring,
  - grid fins,
  - flaps de Starship,
  - tiles negros en cara windward,
  - acero inoxidable con variacion sutil.
- Separar visualmente Super Heavy y Ship despues de staging.
- Mantener motores 33/6 como visuales hasta una fase futura.
- Capturas de aceptacion:
  - pad lateral,
  - despegue con pluma,
  - staging,
  - Starship sola en orbita,
  - belly-first reentry.

## Interplanetario Real

Objetivo: que las transferencias Tierra-Luna/Marte sean jugables y fisicamente coherentes.

- Validar floating-origin y scaled-space en cruceros largos.
- Endurecer patched conics en transiciones Tierra/Luna/Sol.
- Permitir selector de destino en mapa.
- Generar nodos Hohmann editables.
- Mostrar trayectoria e intercepcion estimada.
- Anadir `ManeuverExecutor` robusto para orientar y ejecutar burns.
- Tests:
  - SOI dominante en fronteras,
  - Kepler/rails estable en coast largo,
  - maniobra aplica delta-v esperado,
  - trayectoria objetivo no produce NaN ni saltos de referencia.

## CI / Headless Tests

Objetivo: hacer que el cierre actual sea repetible por push.

- Pipeline con:
  - `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet`,
  - `dotnet build Exosphere.csproj --nologo -v quiet`,
  - `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`,
  - Godot headless smoke test de `scenes/flight/Flight.tscn`.
- Definir estrategia de capturas visuales con framebuffer real. El modo `--headless` actual usa renderer dummy y no expone textura de viewport para PNG.
- Evitar que harness/autoload temporales entren a commits.
