# Fase 35 — vistas estables y consumo sin boxing por tick

Fecha: 2026-08-15  
Área: `Universe` collection views, `PartGraph.ConsumePropellantFromPool` y scheduler de
engines activos

## Hallazgos

Las tres colecciones públicas del universo estaban implementadas así:

```csharp
public IReadOnlyList<CelestialBody> Bodies => _bodies.AsReadOnly();
```

Cada acceso creaba un nuevo wrapper `ReadOnlyCollection<T>`. El juego consulta estas
propiedades desde varios controladores durante el vuelo y el cambio de escena, incluyendo
puentes de simulación, mapa, cielo, comunicaciones y captura de Starship. No había una
mutación concurrente que justificara recrear la fachada: las listas ya pertenecían al mismo
`Universe` y sus operaciones de escritura están centralizadas.

El perfilado del fixture Flight 7 reprodujo además un segundo allocation estable: el loop de
reparto de combustible recorría `tankPool`, declarado como `IReadOnlyList<Part>`, mediante
`foreach`. El boxing sólo aparecía cuando había demandas líquidas financiadas, por eso el
desglose era `0 B/tick` con motores apagados y `40 B/tick` con motores encendidos, con o sin
TVC.

## Cambio

`Universe` ahora conserva una vista estable por lista:

```csharp
private readonly IReadOnlyList<CelestialBody> _bodiesView;

public Universe()
{
    _bodiesView = _bodies.AsReadOnly();
}

public IReadOnlyList<CelestialBody> Bodies => _bodiesView;
```

El mismo patrón se aplica a vessels y docking connections. Se conserva la API pública y la
protección read-only; sólo se elimina la creación repetida del wrapper.

El reparto de tanques ahora usa un loop indexado sobre `tankPool`, preservando el orden y las
fórmulas de distribución de LF/Ox.

Además, `Universe.GetMixedPhysicsStepCap` y `Universe.RequiresOffRailsPhysics` sustituyen
`ActiveEngines.Any(...)` por un loop indexado sobre `ActiveEngineList`. Esto elimina la
enumeración por la fachada `IEnumerable<Part>` sin cambiar ningún umbral de throttle ni la
clasificación de rails.

## Verificación

La regresión `UniverseCollectionViewsAreStableAndAllocationFreeAfterConstruction` comprueba:

- `Universe.Bodies`, `Vessels` y `DockingConnections` devuelven siempre la misma referencia.
- 10.000 lecturas de cada propiedad después de GC no asignan bytes administrados.

Resultados observados:

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
Build succeeded. 0 Warning(s). 0 Error(s).

Focused PerformanceAcceptanceTests + PhysicsSchedulerPerformanceTests: 26/26 PASS
optimization_phase23_contract_test: 38/38 PASS
```

El desglose Flight 7 posterior al loop indexado quedó:

```text
engines_off      0.00 B/tick
engines_on       0.00 B/tick
engines_on_tvc   0.00 B/tick
```

La regresión focalizada de control, datos Starship, scheduler, vistas y allocations pasó `20/20`; la
suite completa posterior pasó `603/603`.

El benchmark standalone fue reproducido por la auditoría delegada con `8/8 PASS`. Dos
intentos locales anteriores de ese benchmark abortaron antes de descubrir tests por
`System.Net.Sockets.SocketException (13): Permission denied`; no se registra como fallo
funcional ni se usa para afirmar una mejora de tiempo.

## Límites y decisión

Este cambio reduce allocations de presentación/acceso y del consumo de propelente, pero no
altera el scheduler ni la física. No se afirma una ganancia de FPS sin framebuffer y profiler
del hardware objetivo. El fixture Flight 7 ya no registra allocations administradas en los
tres escenarios de control medidos.

Decisión: promover la implementación. Mantener pendientes las mediciones de GPU, EventPipe y
framebuffer orbital porque el host actual usa llvmpipe y no expone `/dev/dri`.
