# Fase 25 — diagnóstico numérico del cruce SOI (N11)

Estado de N11: **PASS** para la explicación causal y la corrección de continuidad
observada en el árbol de trabajo.

Fecha: 2026-08-14
Alcance: diagnóstico reproducible; N11 no modificó runtime, tests, benchmarks ni
`project.godot`. El único archivo creado por N11 es este informe.

## Conclusión

La divergencia de `6082.762530297994 m` y `1999.9999999995025 m/s` no procede del
reframe de `Universe` ni de una referencia activa incomparable. Procede de la
conversión `OrbitalElements.FromStateVector` → `GetStateAtTime` en un caso singular:

- órbita equatorial retrógrada (`h.Z < 0`, `Inclination = π`);
- `LongitudeOfAscendingNode = 0` por nodo indefinido;
- `ArgumentOfPeriapsis` almacenado como el ángulo directo del vector de excentricidad;
- la matriz perifocal→inercial aplicada con `Inclination = π` refleja el eje Y en el
  plano orbital.

El conic resultante es finito, pero no representa el estado cartesiano usado para
construirlo. Por tanto, es un fallo de representación/reconstrucción en
`OrbitalElements` (consumido mediante el thin wrapper `KeplerPropagator`), no una
discontinuidad introducida por `ReframeVesselToBody`.

La corrección inequívoca para el caso geométrico es tratar explícitamente la rama
equatorial retrógrada y usar el signo de argumento compatible con la transformación
inercial (el experimento externo que sólo niega `ArgumentOfPeriapsis` reduce el error de
epoch a `5.165128695828703e-13 m` y `1.670996555883932e-13 m/s`). La salvaguarda
conservadora que ya estaba presente en el árbol concurrente —comprobar la identidad
estado→elementos en el epoch y usar un RK4 acotado si falla— también evita el salto sin
promover una conica inválida. N11 no aplicó ni editó esa salvaguarda.

## Fixture exacto

Se reprodujo el mismo fixture sintético de
`ExosphereSimulation.Tests/PhysicsSchedulerPerformanceTests.cs`:

```text
Earth: id=soi-earth, GM=1, radius=1000 m, SOI=1e9 m,
       position=(0, 0, 0), velocity=(0, 0, 0)
Moon:  id=soi-moon,  GM=1e-6, radius=100 m, SOI=5000 m,
       position=(100000, 0, 0), velocity=(0, 0, 0)
Vessel: position=(94000, 0, 0), velocity=(1000, 1000, 0),
        initial reference=soi-earth, on rails=true
Universe: TimeScale=5, Tick(realDeltaTime=0.5)
```

El tick avanza `2.5 s`. El primer slice de rails es de `2.0 s`; en ese instante la
nave está en:

```text
inertial position = (96000, 2000, 0)
inertial velocity = (1000, 1000, 0)
Moon position     = (100000, 0, 0)
Moon-relative p   = (-4000, 2000, 0)
Moon-relative v   = (1000, 1000, 0)
```

La distancia lunar es `4472.13595499958 m`, dentro de la SOI de `5000 m`. El reframe
recibe exactamente el estado de la Luna en `t=2.0` y calcula las diferencias relativas
anteriores; no usa la posición de la Luna al final del tick.

## Reconstrucción en el epoch

Entrada al reframe:

```text
relative position = (-4000, 2000, 0)
relative velocity = (1000, 1000, 0)
GM               = 1e-6
epoch            = 2
```

Elementos obtenidos:

```text
a       = -5E-13
e       = 8485281374238570
i       = 3.141592653589793
aop     = 2.356194490192345
M0      = -2828427124746191
radial  = false
h.Z     = -6000000
```

Al volver a evaluar el conic en el mismo epoch (`t=2.0`), sin avanzar tiempo:

```text
expected position = (-4000, 2000, 0)
actual position   = (-1999.999999999999, -4000.0000000000005,
                     4.898587196589414E-13)
position error    = 6324.555320336759 m

expected velocity = (1000, 1000, 0)
actual velocity   = (-1000.0000000000001, 1000,
                     -1.224646799147353E-13)
velocity error    = 2000 m/s
```

Este resultado descarta un error de fase acumulado, de propagación durante 0.5 s o de
la posición de la Luna: el estado ya es incorrecto en el epoch de construcción.

## Propagación de 0.5 s con el nuevo GM

Usando esos mismos elementos lunares y `GM=1e-6` para `t=2.5`:

```text
Moon-relative position = (-2499.999999999999, -3500.0000000000005,
                          4.2862637970157373E-13)
Moon-relative velocity = (-1000.0000000000001, 1000,
                          -1.224646799147353E-13)
```

Al sumar la posición de la Luna, la salida inercial es:

```text
rails position = (97500, -3500, 4.286263797015543E-13)
rails velocity = (-1000, 1000, -1.2246467991473507E-13)
```

Coincide con la divergencia documentada en la Fase 24. `KeplerPropagator` no añade una
segunda transformación: `PropagateToTime` delega directamente en
`OrbitalElements.GetStateAtTime`. El owner numérico es, por tanto, la
reconstrucción/convención de elementos.

## Controles geométricos

Se ejecutó el mismo programa temporal con controles que sólo cambian la geometría:

| Caso | `h.Z` | `i` | Error de posición en epoch | Error de velocidad en epoch |
|---|---:|---:|---:|---:|
| Fixture retrógrado equatorial | `-6000000` | `π` | `6324.555320336759 m` | `2000 m/s` |
| Control prograde | `2000000` | `0` | `5.303219854654618e-12 m` | `4.687428401686771e-13 m/s` |
| Retrógrado no-equatorial (`v.Z=1`) | `-6000000` | `3.140847297735394` | `9.094947085491919e-13 m` | `9.647951400386499e-11 m/s` |

El control prograde demuestra que la magnitud pequeña de `GM` por sí sola no explica el
salto. El control retrógrado no-equatorial demuestra que el problema aparece en la
representación singular `i=π`, donde el nodo ascendente deja de definir un eje único.

Como comprobación de la corrección geométrica, el programa temporal construyó una copia
de los elementos y negó sólo `ArgumentOfPeriapsis`, sin cambiar el estado de entrada:

```text
epoch position error = 5.165128695828703e-13 m
epoch velocity error = 1.670996555883932e-13 m/s
```

Esto no constituye un cambio aplicado al runtime; prueba que la convención angular es la
fuente del error y define una ruta de corrección concreta para una futura modificación
aislada de `OrbitalElements`.

## ¿La referencia activa es comparable?

Sí, para este fixture y para la métrica que se está probando: continuidad del estado
inercial en el cruce. La referencia activa conserva Earth como cuerpo dominante y usa la
ruta RK4; tras `t=2.5` produjo:

```text
active position = (96499.99999999965, 2499.999999999997, 0)
active velocity = (999.9999999997247, 999.9999999999965, 0)
```

La referencia balística sin fuerzas sería exactamente `(96500, 2500, 0)` y
`(1000, 1000, 0)`. La diferencia es compatible con el `GM=1` de Earth a unos 96 km; el
`GM=1e-6` de la Luna produce una aceleración de sólo aproximadamente `5e-14 m/s²` a la
distancia del cruce. El salto original de `6082.762530297994 m` y `1999.9999999995025
m/s` es, por tanto, cuatro a catorce órdenes de magnitud mayor que el efecto físico del
fixture y no puede atribuirse a que la referencia permanezca Earth durante 0.5 s.

La comparación no pretende validar una trayectoria patched-conic larga entre Earth y
Moon; sólo valida que el cambio de marco no cree una discontinuidad instantánea.

## Validación de la salvaguarda concurrente

Al momento del diagnóstico había cambios no pertenecientes a N11 en `Universe.cs` y
`PhysicsSchedulerPerformanceTests.cs`. N11 no los editó. Esos cambios:

1. comprueban si el conic reconstruye el estado cartesiano en su propio epoch;
2. conservan el estado de cruce como autoridad;
3. usan un RK4 de dos cuerpos acotado cuando la reconstrucción no es identidad;
4. reactivan el test SOI que antes estaba marcado `Skip`.

Con ese árbol concurrente, la corrida focalizada fue:

```text
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~SoiCrossingDeadlinePathMatchesAlwaysCheckedReferenceWithoutInertialJump

Passed: 1, Failed: 0, Skipped: 0
```

La reproducción externa de la ruta rails con esa salvaguarda obtuvo:

```text
rails position = (96499.9999999996, 2499.9999999999845, 0)
rails velocity = (999.9999999997783, 999.999999999998, 0)
reference     = soi-moon (rails), soi-earth (active)
position error = 4.534950714131263e-11 m
velocity error = 5.368053638053659e-11 m/s
```

Cumple las tolerancias del diagnóstico SOI (`1e-6 m`, `1e-9 m/s`). Esta validación no
promueve automáticamente la salvaguarda a una decisión de producto; sólo confirma que
es una corrección conservadora del fixture y que no oculta NaN, infinito ni un estado
no reconstruible.

## Comandos y artefactos

Programa temporal fuera del repositorio:

```text
/tmp/exosphere-n11-soi/Program.cs
/tmp/exosphere-n11-soi/exosphere-n11-soi.csproj
```

Comandos ejecutados:

```bash
DOTNET_CLI_HOME=/tmp/exosphere-n11-dotnet-cli \
  dotnet run --project /tmp/exosphere-n11-soi/exosphere-n11-soi.csproj --no-restore

dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~SoiCrossingDeadlinePathMatchesAlwaysCheckedReferenceWithoutInertialJump

git diff --check
```

Resultados: experimento reproducible, test focalizado `1/1 PASS`, `git diff --check`
`PASS`. No se hizo commit y no se modificaron runtime, tests, benchmarks ni
`project.godot` por N11.

## Decisión para la siguiente fase

La equivalencia SOI puede salir del estado **BLOCKED** del diagnóstico sintético porque
la causa está aislada y la salvaguarda conserva continuidad. Antes de promover una
optimización general de rails conviene cubrir, en una fase separada y con ownership
explícito, la rama equatorial retrógrada de `OrbitalElements` con un test de round-trip
en epoch. La salvaguarda de runtime debe permanecer como fallback para otros estados
hiperbólicos mal condicionados, aunque la convención angular se corrija.
