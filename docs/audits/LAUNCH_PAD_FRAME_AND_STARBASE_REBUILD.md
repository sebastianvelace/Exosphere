# Marco geodésico del pad y plan de reconstrucción de Starbase

**Fecha:** 2026-07-12 · **Estado:** bug de orientación corregido; reconstrucción planificada

## Causa del giro visible

La nave se colocaba con `+Y` alineado al radial terrestre de Kennedy, pero la estación conservaba
`Basis.Identity`: su `+Y` seguía apuntando al norte inercial del render. En Kennedy esos vectores
se separan aproximadamente 60°, por lo que torre, nave, terreno y cámara no podían verse verticales
al mismo tiempo.

Había un segundo defecto: cada frame la estación se trasladaba al punto terrestre directamente
debajo de la nave. Tras el despegue, el complejo perseguía la subtraza del cohete en vez de quedar
en su latitud y longitud.

## Contrato corregido

`LaunchSite.GetLocalFrame` produce una base ortonormal diestra:

```text
local +X = este
local +Y = radial arriba
local -Z = norte geográfico
local +Z = sur
det(Basis) = +1
```

La estación se recalcula desde las coordenadas geodésicas del sitio para acompañar la traslación
orbital de la Tierra, pero nunca desde la posición horizontal de la nave. La cámara exterior recibe
el mismo marco, de modo que sus presets continúan siendo locales al pad y el horizonte queda
horizontal. La cámara de cabina conserva la orientación física de la nave.

Las pruebas fijan ortogonalidad, determinante, vertical compartida, offset pad–vehículo y validez
del frame en varias latitudes/longitudes.

## Auditoría del modelo actual

La geometría existente es “inspirada en Starbase”, pero aún mezcla instalaciones incompatibles:

- el sitio activo predeterminado es Kennedy, no Boca Chica;
- hay simultáneamente flame trench/deflector lateral y elementos del sistema de deluge moderno;
- torre, mount, apron y tank farm no comparten un datum vertical único;
- los brazos de captura y QD son cajas estáticas sin pivotes mecánicos;
- el tank farm es una cuadrícula genérica;
- torres de rayos y blockhouse comunican visualmente Kennedy más que Starbase;
- cientos de `MeshInstance3D` independientes limitan cuánto detalle puede añadirse.

## Próxima reconstrucción

1. Crear un perfil `starbase` en Boca Chica y separar ubicación geográfica de configuración visual.
2. Definir un datum único: `y=0` superficie civil, altura del deck OLM e interfaz del booster.
3. Remodelar OLM, seis patas, mesa de 9 m, clamps/BQD y clearances de 33 motores.
4. Reconstruir OLIT por módulos con carriage, chopsticks y Ship QD sobre pivotes animables.
5. Elegir una configuración histórica explícita del sistema de escape/deluge y eliminar la mezcla.
6. Rehacer tank farm, bermas, headers y pipe racks con layout reconocible.
7. Modelar Highway 4, humedales/costa, límites del recinto y relación espacial real.
8. Agrupar repetidos con `MultiMesh`, añadir LOD y golden lateral/cenital antes del microdetalle.

La referencia primaria será la documentación pública de la FAA para el proyecto Starship/Super
Heavy en Boca Chica: https://www.faa.gov/space/stakeholder_engagement/spacex_starship . El sitio
cambia con rapidez; cada reconstrucción debe declarar su fecha/configuración en lugar de mezclar
elementos de épocas diferentes.
