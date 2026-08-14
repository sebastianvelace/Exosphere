# Fase 25 — corrección de órbita ecuatorial retrógrada en `OrbitalElements`

Estado: **PASS — corrección matemática aislada validada localmente**
Fecha: 2026-08-14
Ownership: `ExosphereSimulation/OrbitalElements.cs` y este informe
Alcance: representación `FromStateVector` → `GetStateAtTime` en el mismo epoch

## Causa

Para una órbita ecuatorial retrógrada, el momento angular apunta a `-Z`, por lo que
`Inclination = π`. El nodo ascendente no existe y se fija convencionalmente
`LongitudeOfAscendingNode = 0`.

La conversión real usada por el simulador es
`MathUtils.OrbitalToInertialStateVector`. Con `Ω = 0` e `i = π`, su matriz perifocal
aplica:

```text
perifocal +X → inertial +X
perifocal +Y → inertial -Y
```

Por ello, si el vector de excentricidad tiene longitud inercial `α = atan2(eᵧ, eₓ)`,
el argumento compatible con esa transformación es:

```text
ω = -α
```

La implementación anterior almacenaba `ω = +α`. El resultado era finito, pero reflejaba
la componente Y y no reconstruía el estado que había originado los elementos. En el
fixture SOI de la fase anterior esto producía `6324.555320336759 m` y `2000 m/s` de
error ya en el epoch.

## Corrección aplicada

`FromStateVector` detecta exclusivamente la rama degenerada mediante:

```text
h.Z < 0  y  |n| / |h| ≤ 1e-12
```

La razón normalizada evita que la escala de `h` convierta el umbral geométrico en un
umbral dependiente de unidades. En esa rama:

- `Ω` continúa siendo cero porque el nodo no está definido;
- el argumento de periapsis usa `-atan2(eᵧ, eₓ)`;
- el caso circular usa la longitud inercial con signo negativo para el argumento de
  latitud.

Las ramas prograde, no ecuatoriales, hiperbólicas y radiales no fueron reescritas. La
transformación de `MathUtils` sigue siendo la única conversión a estado inercial.

## Validación temporal fuera del repositorio

Se ejecutó un harness efímero en `/tmp` con tolerancia relativa `1e-9`, comprobando
`FromStateVector(state, gm, epoch)` seguido de `GetStateAtTime(epoch, gm)`:

| Caso | Error relativo posición | Error relativo velocidad |
|---|---:|---:|
| Fixture ecuatorial retrógrado SOI | `1.15e-16` | `1.18e-16` |
| Ecuatorial retrógrado, otro cuadrante | `2.34e-16` | `1.18e-16` |
| Control ecuatorial prograde | `2.54e-16` | `8.04e-17` |
| Control retrógrado inclinado | `2.03e-16` | `6.82e-14` |

Resultado del harness: `ROUND_TRIP_OK`.

La prueba inclinada conserva una inclinación de `3.140847297735394 rad` y no entra en
la rama singular; el control prograde conserva `i = 0`. Esto verifica que el signo
especial no se aplica a esas geometrías.

## Build y límites

```text
dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore
Build succeeded — 0 Warning(s), 0 Error(s)
```

La corrección no modifica `Universe.cs`, benchmarks, `visual_playtest.sh` ni
`project.godot`. El diagnóstico SOI de
`PhysicsSchedulerPerformanceTests` fue reactivado después de aplicar este cambio; no se
conservó el fallback RK4 de N10 porque la conversión orbital ya reconstruye el estado en
su epoch.

## Integración y regresión

Con el `Skip` retirado y sin cambios en `Universe.cs`:

```text
SoiCrossingDeadlinePathMatchesAlwaysCheckedReferenceWithoutInertialJump: PASS
PhysicsSchedulerPerformanceTests: 19 PASS, 0 SKIP
Suite xUnit: 576 PASS, 0 SKIP, 0 FAIL
```

La corrección queda limitada a la representación matemática singular; no cambia la
política de rails, los deadlines ni la frecuencia de física.
