# Corrección — restos expulsados de la Tierra tras un impacto

## Síntoma

Después de chocar contra la Tierra, un vehículo destruido podía parecer que
rebotaba y salía disparado al espacio, incluso con energía suficiente para
abandonar la esfera de influencia terrestre.

## Causa

La resolución del impacto hacía dos cosas correctas en el instante del choque:

1. fijaba el vehículo sobre el radio de la superficie;
2. igualaba su velocidad a la traslación y rotación de la Tierra.

Sin embargo, los ticks siguientes omitían todo vehículo con `IsDestroyed`.
Su posición quedaba congelada en el marco inercial heliocéntrico mientras la
Tierra continuaba recorriendo su órbita a aproximadamente 30 km/s. En el marco
terrestre, esa separación parecía un rebote de velocidad extrema. El camino de
alta aceleración temporal también intentaba volver a propagar restos destruidos
como una órbita Kepleriana.

Los amerizajes suaves tenían una variante menor del mismo problema: el solver
los colocaba sobre la superficie, pero no los dejaba en un estado persistente.
En el frame siguiente volvían a caer dentro de la esfera y repetían el impacto.

## Corrección

- Todo `GroundImpact` termina en un estado de superficie persistente.
- Se guarda el cuerpo de referencia, la normal local y la altura del wreck.
- En cada tick se actualiza la normal con la rotación del cuerpo y se reconstruye
  la posición y velocidad inerciales desde el cuerpo móvil.
- Los restos anclados nunca vuelven a `on-rails` ni reciben una nueva órbita.
- Los aterrizajes y amerizajes suaves usan el mismo estado dormido, pero pueden
  despertarse si el jugador vuelve a ordenar empuje.
- `SaveGameV2` conserva `IsSurfaceSettled` y su temporizador, de modo que cargar
  una partida no reactiva el fallo.

El anclaje se aplica únicamente a impactos contra la superficie. Una ruptura
térmica o estructural en vuelo no se pega artificialmente a un planeta.

## Invariantes de regresión

`SurfaceImpactAnchoringTests` reproduce un impacto a 250 m/s contra una Tierra
que orbita y rota, guarda/carga el resultado y avanza 10 000 segundos con warp
100 000×. Verifica que:

- el wreck permanece a 0,5 m del datum;
- su velocidad relativa a la superficie es cero;
- su energía específica respecto de la Tierra sigue siendo negativa;
- no reaparece un `OrbitalState`.

También se ampliaron las pruebas de amerizaje suave, contacto de seis patas y
persistencia V2.
