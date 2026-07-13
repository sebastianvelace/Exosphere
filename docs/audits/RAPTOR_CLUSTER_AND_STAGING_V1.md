# Cluster Raptor, staging y cámara V1

**Fecha:** 2026-07-12 · **Estado:** errores críticos corregidos; reconstrucción visual iniciada

## Fallos encontrados en la captura

La pared de motores no era únicamente una cámara demasiado cercana. Al separar etapas, Starship y
Super Heavy conservaban la misma posición mundial, pero el renderer de Starship cambiaba su datum
desde la base del stack hasta la base de Ship. Las dos etapas quedaban interpenetradas unos 71 m.

Ahora la posición de Ship se rebasa hasta el plano físico de separación. Un impulso relativo de
1 m/s se distribuye por masa entre ambas etapas, conservando momento lineal. Super Heavy conserva
el datum original y se abre un espacio real entre vehículos.

La cámara chase calcula una distancia mínima desde longitud, diámetro y FOV. El target se desplaza
al centro de la etapa en su eje orientado; el zoom solicitado se conserva, pero nunca puede colocar
la cámara dentro de la envolvente de Starship o del stack.

## Hardware Raptor corregido

- Starship usa tres Raptor sea-level centrales y tres Raptor Vacuum exteriores.
- Las campanas RVac siguen siendo más largas y anchas y sus exits quedan más bajos.
- Los segmentos de campana no tienen tapas: desde abajo se ve el throat oscuro, no una pila de
  discos sólidos como en la captura.
- El layout 33 de Super Heavy permanece 3+10+20 como reconstrucción visual documentada.

SpaceX confirma 33 motores en Super Heavy y seis en Ship; NASA distingue variantes sea-level y
vacuum. Las dimensiones exactas de bell/throat no están publicadas y siguen siendo estimaciones.

Fuentes:

- https://www.spacex.com/launches/starship-flight-2
- https://www.nasa.gov/blogs/artemis/2023/09/14/spacex-completes-engine-tests-for-nasas-artemis-iii-moon-lander/

## Pendiente para V2

1. Un mesh continuo compartido por perfil de campana, en lugar de varios frusta por motor.
2. Familia común de Raptor SL en Super Heavy, gimbal cages para los tres centrales y thrust puck.
3. RVac con stiffeners, weld rings y extensión thin-wall diferenciada.
4. Plumbing reconstruido: feed lines, powerheads, actuadores y shielding del engine bay.
5. Seis plumas individuales en Ship y near-field individual en SH antes de la fusión downstream.
6. Eliminar humo gris nominal de methalox; mantener vapor/polvo anclado al deluge del pad.
7. Flicker determinista y selección visual exacta de 1/2/3 motores para landing burns.

## Evidencia

Las pruebas verifican framing mínimo para Ship/stack, comportamiento con FOV, staging existente,
geografía body-fixed y builds completos. CI termina sin errores ni advertencias.
