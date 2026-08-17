# Phase 50 — matriz de paridad para promoción de interés

Fecha: 2026-08-17
Estado: **gate CPU PASS; promoción runtime BLOQUEADA**

## Alcance

Se añadió `InterestPromotionParityTests`, una matriz que compara el candidato de
trabajo diferido con una referencia que fuerza física completa en el mismo epoch de
simulación. El candidato usa el scheduler mixto existente con una nave no activa en
rails analíticos; no se conectó `SimulationInterestPolicy` al dispatcher y
`SimulationInterestPolicy.EnabledByDefault` sigue siendo `false`.

La referencia no se compara por tiempo de pared ni por número de frames. Cada caso usa
el mismo tiempo simulado y comprueba posición, velocidad, orientación, velocidad
angular, estado de destrucción/captura, recursos y finitud numérica cuando aplica.

## Matriz ejecutada

| Caso | Candidato | Referencia | Resultado |
|---|---|---|---|
| Coast | warp 100, nave en rails, decisión `Dormant` | warp 1, nave activa fuera de rails | PASS |
| Save/resume | snapshot V2 + nave en rails | restauración V2 y continuación idéntica | PASS |
| Staging | fragmento separado, comando de throttle, wake a `FullPhysics` | misma separación a warp 1 | PASS |
| Docking | hard-dock y constraint en scheduler mixto | hard-dock y constraint a warp 1 | PASS |
| SOI | cruce de `promotion-earth` a `promotion-moon` | integración RK4 | PASS |
| EDL/catch | aproximación con chopstick pins, sin deferencia | integración RK4 a warp 1 | PASS |

El gate focalizado pasa **6/6**. El test de save/resume también verifica que el log
de callbacks conserva secuencia, entrega (`Delivered`) y el callback pendiente tras
serialize/deserialize.

## Hallazgos de seguridad

- Un fragmento staged sin una pieza de comando pierde `ControlAuthority`; la política
  lo promueve a `Active`, no a `Dormant`. Esto es intencional: una nave no controlable
  no puede hibernarse como si fuera un coast controlado.
- El cruce de SOI puede actualizar `ReferenceBodyId` en un subpaso diferente entre
  patched-conic y RK4. La invariante de promoción es continuidad de posición/velocidad
  y ausencia de salto inercial; exigir igualdad prematura del ID produciría un falso
  positivo o un falso negativo del gate.
- La aproximación EDL/catch siempre queda en `FullPhysics` y en la decisión `Active`;
  la presencia de catch pins y el estado `IsAttemptingTowerCatch` no pueden depender de
  una actualización diferida.

## Decisión

No promover todavía `EventDriven`/`Dormant` al runtime. La matriz demuestra paridad de
los escenarios CPU cubiertos, pero no prueba aún una implementación de hibernación
real ni cubre todo el estado del juego. Antes de cambiar el dispatcher faltan:

1. instanciar y capturar/restaurar `VesselSystemsState` para cada nave que pueda
   quedar diferida, no sólo la nave activa;
2. asociar callbacks, comandos de relay y deadlines a cada vessel sin perder orden;
3. ejecutar la matriz con la ruta diferida real y medir deuda temporal, memoria y
   coste de precálculo en una flota grande;
4. repetir el gate visual de ascenso, SOI, reentrada y catch con framebuffer real.

Hasta cerrar esos puntos, la ruta oficial continúa siendo RGB/FullPhysics o el
scheduler mixto ya existente, sin hibernación automática por distancia.
