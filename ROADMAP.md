# Exosphere Roadmap

Estado de cierre actual:
- Builds .NET/Godot pasan con 0 warnings y 0 errores.
- `ExosphereSimulation.Tests` cubre gravedad, RK4, Kepler, radial/suborbital, rails-impact, motores, termica de escudo, aerodinamica y SOI.
- Godot headless carga la escena principal sin errores.
- VAB V1.5 existe como nucleo testeable (`ExosphereSimulation/Construction`), escena `Construction.tscn`, preview 3D, craft files y flujo VAB -> launch.

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

Objetivo: hacer que el cierre actual sea repetible por push.

- Pipeline con:
  - `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --nologo -v quiet`,
  - `dotnet build Exosphere.csproj --nologo -v quiet`,
  - `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --nologo`,
  - Godot headless smoke test de `scenes/flight/Flight.tscn`.
- Definir estrategia de capturas visuales con framebuffer real. El modo `--headless` actual usa renderer dummy y no expone textura de viewport para PNG.
- Evitar que harness/autoload temporales entren a commits.
