# Fase 34 — enumeración interna de partes sin boxing por tick

Fecha: 2026-08-14  
Área: `PartGraph.PartList`, autoridad estructural, paracaídas y torque aerodinámico

## Hallazgo

Después de la fase 33 el fixture Flight 7 todavía registraba `80 B/tick` con motores
apagados y `120 B/tick` con motores encendidos. La inspección de la simulación encontró
recorridos de `Parts.Parts`, una vista `IReadOnlyList<Part>`, en tres rutas ejecutadas durante
la física: `ControlAuthority.Evaluate`, `Vessel.GetDeployedParachuteDragArea` y el cálculo de
centro aerodinámico/flaps de `Vessel.Tick`.

Aunque el almacenamiento subyacente es una `List<Part>`, enumerarlo desde la interfaz puede
boxear el enumerador struct de `List<T>`. En un tick esto convierte una consulta estructural
que debería ser sólo lectura en garbage repetitivo.

## Cambio

Se añadió el acceso interno estable:

```csharp
internal List<Part> PartList => _parts;
```

La API pública se mantiene sin cambios:

```csharp
public IReadOnlyList<Part> Parts => _partsView;
```

Sólo se migraron callers de física de alta frecuencia. Se conservaron las consultas de
presentación, staging y teletransporte sobre la fachada pública para limitar el alcance y
preservar compatibilidad.

## Medición

Fixture Flight 7 con modelos runtime resueltos por `PartCatalog`, 32 ticks de warm-up y 128
ticks medidos:

| Escenario | Fase 33 | Fase 34 | Reducción |
|---|---:|---:|---:|
| Motores apagados | 80 B/tick | 0 B/tick | 100.00% |
| Motores encendidos | 120 B/tick | 40 B/tick | 66.67% |
| Motores encendidos + TVC | 120 B/tick | 40 B/tick | 66.67% |

No se observaron NaN, masa inválida ni cambios de autoridad, torque, TVC o telemetría. El
residual de `40 B/tick` con motores activos queda abierto para un perfilado posterior.

## Verificación

```text
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-build --no-restore \
  --filter 'FullyQualifiedName~RuntimeFlight7AllocationBreakdownReportsControlHotPaths|FullyQualifiedName~ControlAuthorityTests|FullyQualifiedName~StarshipFlight7DataTests'
bash tools/tests/starship_hotpath_contract_test.sh
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --verbosity minimal
```

Resultados: build `0 warnings / 0 errors`, foco `19/19 PASS`, contrato `PASS`, suite completa
`602/602 PASS` y `0` omitidos.

## Decisión

Promover `PartList` como ruta interna de simulación y conservar `Parts` como fachada pública.
No se cambian scheduler, hibernación, frecuencia física ni modelos aerodinámicos. Las métricas
de FPS, GPU y EventPipe continúan pendientes en este host por la ausencia de dispositivo
`/dev/dri` y el uso de llvmpipe; esta auditoría sólo reclama la reducción de allocations
administradas reproducida por el fixture CPU.
