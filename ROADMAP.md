# Exosphere Roadmap

Estado de cierre actual:
- Builds .NET/Godot pasan con 0 warnings y 0 errores.
- `ExosphereSimulation.Tests` cubre gravedad, RK4, Kepler, radial/suborbital, rails-impact, motores, termica de escudo, aerodinamica y SOI.
- Godot headless carga la escena principal sin errores.
- VAB V1.5 existe como nucleo testeable (`ExosphereSimulation/Construction`), escena `Construction.tscn`, preview 3D, craft files y flujo VAB -> launch.

## Estado De Implementacion (tanda actual)

- **VAB UX — navegador de craft files**: HECHO en `main`. Panel "Saved Crafts" en el VAB
  (`ConstructionController`) que lista `user://crafts` y carga al clickear. Pendiente aun:
  manipulacion directa de nodos en la preview 3D.
- **Reentry visual avanzado**: HECHO en `main`. Plasma concentrado en la cara windward,
  oscurecimiento de tiles por dano termico (`VesselRenderer`), y breakup VFX al destruirse por
  calor (`ReentryBreakupController`). Pendiente: perdida de control por fallo estructural.
- **Interplanetario — patched conics / transiciones de SOI**: HECHO en `main`. El vessel on-rails
  re-encuadra su conico al cruzar una frontera de SOI (`ReframeVesselToBody` + `BodyStateAt` +
  `GetDominantBodyAt` en `Universe.cs`), con continuidad inercial independiente de la resolucion del
  warp. Causa raiz corregida: tanto el conico INICIAL como la reconstruccion del cruce deben usar el
  estado del cuerpo de referencia en el epoch/instante del cruce (`BodyStateAt`), no su posicion de
  fin-de-tick — usar la de fin-de-tick sesgaba la orbita por (velocidad x dt) (~60000 km a max warp:
  una orbita erronea al instante de enganchar warp). Tests: salida SOI Tierra->Sol, entrada SOI Luna,
  cruise Tierra->Marte, no-regresion LEO, y continuidad a max-warp Tick. Pendiente aun: solver
  hiperbolico ya cubre escape; falta validacion de cruise muy largo y UX de nodos arrastrables.

- **Starship visual fidelity**: HECHO en `main`. Casco a diámetro real 9 m (`RScale` escala solo lo
  radial; la altura ~121 m no se toca, asi que camara/cabina no se rompen) y el Super Heavy separado
  tras staging muestra el anillo hot-stage expuesto con vent slots quemados (`VesselRenderer`).
  Convive con el charring de tiles por dano termico. Pendiente: capturas de aceptacion con
  framebuffer real; engine-out real (requiere abandonar el contrato de una parte-motor por etapa).
- **CI / headless**: HECHO en `main`. `.github/workflows/ci.yml` descarga+cachea Godot 4.6.3 mono,
  corre build de juego + smoke headless (escena principal y VAB) de forma estricta, y prepara
  `xvfb-run` para captura con framebuffer. `tools/ci_check.sh` y un step de CI fallan si se cuela un
  harness temporal (`scripts/_*Shot.cs` trackeado o autoload `_*Shot` en `project.godot`). Pendiente:
  completar la captura visual PNG end-to-end en CI.

No quedan frentes mayores en cola del roadmap original; siguientes pasos posibles: save/load de mision,
recursos de vida/energia conectados a fases, engine-out real, y manipulacion de nodos en la preview del VAB.

## VAB / Construccion De Naves

Estado V1.5: implementado el flujo minimo data-driven, testeado y conectado al vuelo.

- Hecho:
  - escena `scenes/construction/Construction.tscn`,
  - `ConstructionController`,
  - catalogo desde `data/parts/*.json`,
  - seleccionar pieza, adjuntar a nodo compatible, borrar subarbol,
  - recalcular masa, propelente, TWR y delta-v,
  - exportar a `Vessel`/`PartGraph`,
  - `SimulationBridge.PlaceConstructedVesselOnPad(...)`,
  - preview 3D con `VesselRenderer`,
  - save/load de craft JSON en `user://crafts`,
  - tecla `V` desde vuelo al VAB,
  - boton `Launch` desde VAB a `Flight.tscn`,
  - tests de catalogo, nodos, metricas, conexiones incompatibles y export.
- Pendiente:
  - manipular attachment nodes directo en la preview,
  - lista visual de craft files,
  - menu principal dedicado.

## Reentry Fisico Y Visual Avanzado

Estado incremental: reentry ya separa causa termica vs impacto, acumula `ThermalDamage` por pieza y tiene tests de entrada nominal, sin escudo, mala orientacion y ley `sqrt(rho) * v^3`.

- Pendiente:
  - plasma por flujo termico,
  - brillo en zona windward,
  - dano/oscurecimiento de tiles,
  - breakup si no hay escudo o actitud correcta.
  - perdida de control si falla estructura critica,
  - EDL belly-first mas robusto antes de flip-and-burn.

## Starship Visual Fidelity

Estado incremental: la nave ya tiene hot-stage ring, grid fins con lattice, flaps con bisagras, tiles windward con seams, motores 33/6 visuales y acero procedural.

- Pendiente:
  - proporciones de 9 m y altura realista,
  - separar visualmente Super Heavy y Ship despues de staging con damage/sep details,
  - capturas de aceptacion con framebuffer real,
  - engine-out real en una fase futura si se abandona el contrato de una parte-motor por etapa.

## Interplanetario Real

Estado incremental: el calculo Hohmann vive en `ExosphereSimulation/Navigation`, tiene tests de Tierra-Marte/Tierra-Venus y `TransferPlanner` consume ese nucleo con phase angle.

- Hecho:
  - selector de destino en mapa,
  - nodos Hohmann,
  - signos correctos para burns exteriores/interiores,
  - tiempo de vuelo y phase angle testeados,
  - `ManeuverExecutor` orienta y ejecuta burns.
- Pendiente:
  - patched conics reales en transiciones Tierra/Luna/Sol,
  - trayectoria/intercepcion visual mas precisa,
  - tests de cruise largo y cambio de SOI,
  - UX de nodos arrastrables mas clara.

## CI / Headless Tests

Estado: CI basico agregado con `.github/workflows/ci.yml` y check local estricto `tools/ci_check.sh`.

- Hecho:
  - `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet`,
  - `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`,
  - build Godot + Godot headless smoke local,
  - build Godot + Godot smoke opcional en CI si `GODOT_BIN` existe.
- Pendiente:
  - instalar/proveer Godot en CI remoto,
  - definir estrategia de capturas visuales con framebuffer real,
  - evitar que harness/autoload temporales entren a commits.
