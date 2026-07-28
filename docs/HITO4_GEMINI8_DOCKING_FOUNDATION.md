# Hito 4 — Fundación de docking para Gemini 8

Gemini 8 necesita que el primer acoplamiento orbital sea un estado físico y
persistible, no una animación. Antes de añadir su vehículo y secuencia histórica
se introdujo un contrato de docking independiente de cualquier familia.

## Puertos data-driven

Una pieza puede declarar:

- attachment node usado como interfaz;
- eje local del puerto;
- rango de captura;
- velocidad relativa máxima;
- tolerancia angular.

El puerto se resuelve con la topología real de `PartGraph`, la misma geometría
que usan VAB y centro de masa. La captura falla explícitamente si falta un
vessel o puerto, el puerto está roto u ocupado, los ejes no se enfrentan o se
excede el corredor de distancia o velocidad. El puerto genérico
`docking_port_standard` habilita pruebas sandbox sin atribuirle dimensiones a
un sistema histórico.

## Conservación y vínculo rígido

`Universe.TryDock` calcula las velocidades lineal y angular comunes a partir de
las masas, inercias aproximadas y momento orbital de ambos cuerpos. La conexión
conserva el momento lineal y angular del instante de captura.

Los vessels mantienen sus IDs y grafos de piezas. El primario se integra y el
secundario conserva posición, orientación, velocidad y velocidad angular
relativas como un vínculo rígido. `Undock` elimina el vínculo y puede aplicar
una velocidad de separación con impulsos iguales y opuestos, sin crear momento
lineal.

Esta primera capa admite una pareja rígida. La agregación de tres o más módulos,
la transferencia de recursos y la inercia completa de estaciones pertenecen al
hito posterior de estaciones modulares; no se simulan silenciosamente aquí.

## Persistencia y validación

`SaveGameV2.DockingConnections` conserva:

- ID estable de la conexión;
- IDs de ambos vessels, piezas y nodos;
- pose relativa en doble precisión.

La validación rechaza IDs duplicados, referencias cruzadas, dos conexiones para
el mismo vessel, puertos repetidos, valores no finitos y cuaterniones no
normalizados. Durante restore se construyen y validan todos los vessels y
puertos antes de reemplazar el universo vivo.

## Pruebas

`DockingSystemTests` comprueba:

- rechazo por rango, velocidad y alineación;
- conservación de momento lineal y angular;
- pose rígida durante integración;
- desacople conservativo;
- round-trip JSON de SaveGameV2;
- restore atómico ante piezas que no son puertos.

Esta base permite que Gemini y Agena permanezcan vessels independientes antes,
durante y después del incidente de Gemini 8.
