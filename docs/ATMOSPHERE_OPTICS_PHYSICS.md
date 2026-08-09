# Contrato físico de la óptica atmosférica

**Estado:** V3 implementado en la simulación y validado numéricamente
**Alcance:** `ExosphereSimulation/AtmosphereOptics.cs` y sus consumidores de iluminación/exposición

Este documento describe el contrato que debe respetar cualquier renderer o sistema de
exposición que use la atmósfera. La simulación trabaja en SI: altitudes y radios en metros,
coeficientes de extinción en `m⁻¹` y longitudes de escala en metros. Los vectores RGB son
lineales; no son colores sRGB listos para presentar en pantalla.

## Perfil vertical

Cada perfil contiene coeficientes de nivel del mar y distribuciones normalizadas:

```text
ρR(h)   = exp(-h / HR)
ρM(h)   = exp(-h / HM)
ρO3(h)  = triangular(h, center, halfWidth)
βext(h) = βR ρR(h) + (βM,sca + βM,abs) ρM(h) + βO3 ρO3(h)
```

`VerticalOpticalDepth(h)` integra `βext` desde `h` hasta el vacío. `VerticalTransmittance`
aplica Beer–Lambert por banda:

```text
T(h) = exp(-τ(h))
```

Por tanto, una profundidad óptica no puede ser negativa y una transmitancia válida debe estar
entre cero y uno. La absorción de Mie y ozono solo extingue; `MieScattering` es la parte que
puede volver a inyectar radiancia en el cielo.

## Luz solar y geometría esférica

Para el Sol directo se debe usar la sobrecarga:

```csharp
optics.DirectSolarTransmittance(
    altitude, sunElevationSin, body.Radius,
    body.Atmosphere!.MaxAltitude, sampleCount: 32);
```

`sunElevationSin` es el producto punto entre la dirección al Sol y la vertical local, es
decir, el seno de la elevación solar (no el ángulo en grados). La implementación convierte
ese valor en `cosZenith` y recorre el rayo desde el observador hasta la esfera atmosférica
exterior. Para un observador de radio `r₀ = R + h` y una dirección con `μ = cos(zenith)`:

```text
b       = r₀ μ
D       = b² − (r₀² − Rtop²)
s_space = −b + √D
r(s)    = √(r₀² + s² + 2 r₀ μ s)
```

La profundidad se obtiene integrando `βext(r(s) − R)` con Simpson determinista. Si el perfil
declara refractividad, `DirectSolarTransmittance` usa además el invariante esférico de Snell y
la coordenada radial transformada `r=r₀+(Rtop-r₀)u²`; esto cambia la trayectoria saliente sin
alterar la física de vuelo. El número de muestras se fuerza a ser par y nunca baja de ocho.
Esta curvatura es esencial cerca del
horizonte: `1 / cos(zenith)` supone una columna plana infinita y sobre-extingue la luz en
amaneceres, atardeceres, planetas pequeños y cámaras a gran altitud.

La dirección solar con elevación no positiva no tiene Sol directo en este contrato y devuelve
`Vector3d.Zero`; el crepúsculo y el airglow son fuentes atmosféricas separadas del rayo solar
directo. Las entradas no finitas tampoco pueden convertirse en luz sin atenuar: devuelven
cero para que un frame de inicialización no ilumine la escena por accidente.

La sobrecarga histórica `DirectSolarTransmittance(altitude, sunElevationSin)` conserva un
perfil genérico terrestre para compatibilidad. Los consumidores nuevos deben pasar siempre el
radio del cuerpo y el techo de su atmósfera; de lo contrario Marte, Venus o una luna usarían
la curvatura de la Tierra.

## LUT de transmitancia en tiempo real

`AtmosphereTransmittanceLut` precalcula la misma transmitancia esférica en una tabla RGB de
altitud × seno de elevación solar. Las coordenadas se deforman como `u²` y `v²`: se reserva
resolución para la troposfera y para los rayos rasantes, donde la función cambia con mayor
rapidez. La tabla se construye una sola vez por cuerpo y se sube como textura lineal HDR al
shader `space_sky`; cada muestra de scattering solar hace una interpolación bilineal en vez de
repetir una cuadratura solar ruidosa por píxel. Si la textura no está disponible, el shader
mantiene la integración esférica como fallback.

Este diseño comparte un único oráculo numérico entre simulación, exposición y GPU: cambiar el
radio o el perfil óptico cambia también los texels y no puede dejar una atmósfera visual con
curvatura terrestre implícita.

## Transporte global de orden dos

`AtmosphereMultipleScatteringLut` integra el cierre difuso desde cada altura del observador
hacia el borde atmosférico. Para cada capa combina la fuente local `LowOrderDiffuseSource`,
la transmitancia solar esférica y la diferencia de profundidad vertical entre observador y
capa. El resultado es radiancia lineal por unidad de `SunIlluminanceScale`; el shader la
aplica como relleno isotrópico con una leve ponderación hacia el cenit y desactiva el antiguo
S₂ por rayo cuando la tabla está disponible. Así el segundo rebote atraviesa toda la columna
en lugar de depender de los segmentos visibles de una sola dirección.

La tabla sigue siendo 2D: no representa todavía la dependencia completa del ángulo de visión,
polarización ni rebotes de nube/terreno. Es un transporte global de orden dos verificable, no
una afirmación de que el problema 4D ya esté resuelto.

## Refracción visible del horizonte

Cada perfil declara la refractividad superficial `n − 1` y su escala vertical. `HorizonRefractionRadians`
integra la pendiente esférica de un perfil exponencial, la limita a 0,035 rad y la usa para
desplazar el disco solar aparente cerca del horizonte. En la Tierra produce aproximadamente
0,56° al nivel del mar y decae exponencialmente con altura; Marte y Venus usan refractividades
específicas de sus columnas de CO₂. La integración refractada cubre la rama saliente; los
rayos subhorizonte con punto de retorno todavía requieren resolver las dos ramas de tangencia.
No se aplica fuerza ni se cambia la navegación.

## Integración local de segundo orden

`LowOrderDiffuseStrength` controla un cierre local (`LowOrderDiffuseSource`), no un sustituto de una LUT de scattering múltiple:

```text
S₂ ≈ βsca · albedo · (1 − Tsolar) · strength / (4π)
```

La función devuelve cero dentro de la sombra planetaria y limita el albedo por banda a
`[0, 1]`. Esto evita inventar luz dentro de un cuerpo opaco y evita que una transmitancia
redondeada mayor que uno genere energía difusa negativa.

## Invariantes cubiertos

`ExosphereSimulation.Tests/AtmosphereOpticsTests.cs` verifica, entre otros:

- el orden cromático Rayleigh de la Tierra y la continuidad de transmitancia al subir;
- que el rayo esférico cenital coincide con la columna vertical analítica;
- que la profundidad es finita, no negativa y decrece monótonamente al acercarse al cenit;
- que duplicar la resolución de Simpson converge en un amanecer de baja elevación;
- que el radio real del cuerpo cambia la extinción (no hay radio terrestre implícito);
- que entradas no finitas no producen luz solar sin atenuar;
- que la LUT coincide con el oráculo en sus texels, conserva la monotonicidad y resuelve el
  enrojecimiento de la columna solar rasante;
- que el transporte global de orden dos permanece finito, disminuye sobre la columna y conserva
  la dependencia del radio planetario;
- que ozono, airglow, nubes y fuente difusa respetan sus soportes y límites.

Ejecutar el conjunto focalizado:

```bash
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj \
  --no-restore --filter 'FullyQualifiedName~AtmosphereOpticsTests'
```

El chequeo integrado añade compilación de ambos ensamblados y smoke test de Godot:

```bash
bash tools/atmosphere_quick_check.sh
```

## Límites conocidos y siguiente nivel de fidelidad

Este contrato sigue siendo una aproximación de scattering: las LUTs resuelven transmitancia
directa y un rebote global isotrópico de orden dos, y el shader incluye una corrección acotada
del disco solar y de la rama saliente, pero aún no modela el tramo subhorizonte completo,
polarización,
variación espectral por temperatura, perfil de humedad/aerosoles por clima ni el acoplamiento
radiativo entre nubes y terreno. El siguiente nivel es una LUT de scattering múltiple de órdenes
superiores dependiente de altura, ángulo solar y ángulo de visión (Bruneton/Neyret), más un
integrador refractivo completo para el disco, el horizonte y las estrellas; esta implementación
C# seguirá siendo el oráculo numérico.
