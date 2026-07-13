# Corrección del marco rotante de lanzamiento

**Fecha:** 2026-07-12 · **Severidad:** crítica

## Síntoma

Al liberar los clamps, el cohete se desplazaba lateralmente respecto de Starbase aunque su pitch
fuera 90°. La estación V0 perseguía la subtraza de la nave y ocultaba el fallo; al fijarla en Boca
Chica durante la reconstrucción V1, la divergencia se hizo visible.

## Causa

La nave heredaba correctamente `ω × r` —unos 400 m/s hacia el este—, pero las coordenadas del
launch site se calculaban con una longitud congelada. Físicamente, nave y pad deben compartir esa
velocidad antes del despegue. En el juego solo se movía la nave: el terreno quedaba inmóvil.

El ground hold también conservaba una normal y una orientación inerciales fijas durante countdown,
aunque actualizaba la velocidad como si la superficie rotara.

## Solución

- `CelestialBody.GetSurfacePositionAtTime` avanza la longitud con `AngularSpeed × simulationTime`.
- `LaunchSite` expone posición y frame local dependientes del tiempo.
- El pad usa esas coordenadas body-fixed cada frame.
- Mientras los clamps están activos, `Universe` rota normal y orientación del vehículo en cada
  substep, y luego deriva posición/velocidad del mismo punto superficial.
- Al liberar, la velocidad heredada coincide con la derivada temporal del pad; el thrust radial
  produce ascenso vertical relativo al suelo sin eliminar la ventaja rotacional terrestre.

## Evidencia

Las pruebas comparan la derivada finita de la posición del sitio con `ω × r`, verifican lateralidad
del frame tras un día sideral y prueban que, durante el primer segundo tras liberar, la separación
este/norte entre vehículo y pad permanece por debajo de 5 cm.
