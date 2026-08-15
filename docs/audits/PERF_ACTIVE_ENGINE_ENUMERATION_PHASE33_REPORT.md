# Fase 33 — enumeración interna de motores sin boxing por tick

Fecha: 2026-08-14  
Área: `PartGraph.ActiveEngineList`, `Vessel.Tick` y autoridad de control

## Hallazgo

Tras eliminar el closure de fallos programados, el fixture Flight 7 runtime todavía registraba
allocations pequeñas y constantes: `240 B/tick` con motores apagados, `280 B/tick` con motores
encendidos y `360 B/tick` con TVC.

`PartGraph.ActiveEngines` exponía el buffer reutilizable como `IEnumerable<Part>`. En los
callers internos de alta frecuencia, `foreach` debía usar el enumerador de la interfaz; para un
`List<Part>` eso puede materializar el boxing del enumerador struct en cada recorrido.

## Cambio

Se añadió `internal List<Part> ActiveEngineList`, que conserva exactamente la selección y el
cache de motores activos. Se migraron a esa ruta concreta:

- `Vessel.ApplyThrottle` y la aplicación de comandos de gimbal;
- `ControlAuthority.Evaluate`;
- cálculos de torque, aceleración angular, envelope TVC y thrust en `PartGraph`;
- lecturas internas que ya estaban dentro del límite de la simulación.

`public IEnumerable<Part> ActiveEngines` continúa disponible y devuelve el mismo buffer para
compatibilidad con HUD, tests y callers externos.

## Medición

Fixture: Flight 7 con modelos runtime resueltos por `PartCatalog`, 32 ticks de warm-up y 128
ticks medidos.

| Escenario | Antes | Después | Reducción |
|---|---:|---:|---:|
| Motores apagados | 240 B/tick | 80 B/tick | 66.67% |
| Motores encendidos | 280 B/tick | 120 B/tick | 57.14% |
| Motores encendidos + TVC | 360 B/tick | 120 B/tick | 66.67% |

El residual actual es `80–120 B/tick`, sin NaN, sin masa inválida y sin cambios funcionales.

## Verificación

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --no-restore \
  --filter 'FullyQualifiedName~RuntimeFlight7AllocationBreakdownReportsControlHotPaths|FullyQualifiedName~ControlAuthorityTests|FullyQualifiedName~StarshipFlight7DataTests'
```

Resultado focalizado: `19/19 PASS`. La regresión de allocations mantiene `<=1,000 B/tick` en
los tres escenarios. La suite completa pasó `602/602`; `ci_check.sh` pasó con contratos `34/34`,
builds sin warnings y startup quick-check PASS.

## Decisión

Promover el buffer concreto interno. No se cambia la API pública ni se introducen saltos de
simulación, hibernación por distancia o reducción de cadencia. El siguiente trabajo que pueda
justificar una modificación mayor sigue necesitando EventPipe y GPU física.
