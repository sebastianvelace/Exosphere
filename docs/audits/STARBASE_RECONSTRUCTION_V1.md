# Reconstrucción Starbase V1 — escala, datum y masas principales

**Fecha:** 2026-07-12 · **Epoch visual:** Pad A posterior al sistema de deluge, vehículo clásico de 121 m

## Qué cambió

La estación ya no es un diorama “Starbase-inspired” colocado en Kennedy. El sitio predeterminado
es Boca Chica (`25,9972° N, 97,1566° W`) y dispone de un perfil de ingeniería separado de la
geodesia. Todas las dimensiones se expresan primero en metros y se convierten a unidades Godot
solamente en el renderer.

Se eliminó del assembly activo la pila de slabs duplicados, carreteras superpuestas, flame trench
lateral y tres torres de rayos propias de otro tipo de complejo. Un único recinto civil contiene
relleno costero, apron, cimentación OLM, acceso equivalente a Highway 4 y vía del tank farm.

El datum activo es único:

| Elemento | Elevación sobre grade |
|---|---:|
| Grade civil | 0,000 m |
| Interfaz booster/OLM | 19,812 m |
| Tope estructural OLIT | 146,304 m |
| Punta del pararrayos | 149,352 m |

El OLM conserva seis patas y un diámetro exterior provisional de 21 m. Sus paredes interior/exterior
son ahora segmentos abiertos: antes varios `CylinderMesh` sólidos tapaban por completo el supuesto
hueco de motores. La placa water-cooled queda junto al grade y el transporte de vapor puede escapar
radialmente en lugar de una trinchera ficticia.

La OLIT usa la cota oficial de 480 ft más pararrayos de 10 ft. Se retiró la grúa superior inventada.
Los chopsticks mantienen una longitud estimada de 30 m, pero su abertura interior pasó de 7 a 10 m,
por encima del diámetro de 9 m del booster. El QD genérico duplicado se eliminó; permanecen BQD y
Ship QD con funciones diferenciadas.

El commodity farm crece de una cuadrícula genérica de ocho depósitos a quince siluetas verticales
con alturas hasta 100 ft, headers por fila y containment bund. El layout exacto sigue siendo una
reconstrucción parametrizada, no una afirmación de planos propietarios.

## Fuentes y nivel de confianza

La FAA Final PEA 2022 documenta: mount redundante de aproximadamente 65 ft con layout similar a
Pad A, torres de 480 ft más pararrayos de 10 ft, y aproximadamente quince tanques de hasta 100 ft.
Fuente primaria: https://www.faa.gov/sites/faa.gov/files/2022-06/PEA_for_SpaceX_Starship_Super_Heavy_at_Boca_Chica_FINAL.pdf

La FAA documenta la placa perforada de acero inoxidable, descarga de agua y nozzles del ring tras
Flight 1: https://www.faa.gov/media/72791

- Confianza alta: alturas verticales FAA, ubicación Boca Chica, vehículo de 9 m, deluge moderno.
- Confianza media: diámetro OLM de 21 m, separación OLM–OLIT de 35 m, footprint OLIT de 12 m.
- Estimación parametrizada: geometría exacta de brazos, nozzles, tuberías y coordenadas internas
  del commodity farm.

## Gates siguientes

1. Convertir OLIT lattice y repetidos a `MultiMesh`/mesh cache, con objetivo menor a 100 nodos.
2. Crear `CatchArms` y `ShipQD` como assemblies articulados con pivotes y estados operativos.
3. Modelar la placa perforada mediante material/normal map y manifold real del deluge.
4. Rehacer el layout del tank farm desde el site plan FAA y separar tipos de commodity.
5. Añadir costa, humedal, retention ponds, Highway 4 completo y edificios de soporte bajos.
6. Capturas golden lateral/cenital con reglas de 9 m, 121 m, 146 m y 19,8 m.
