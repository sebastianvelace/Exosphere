# Contrato físico de la óptica atmosférica

**Estado:** V6 implementado en la simulación y validado numéricamente
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
resuelve por bisección la elevación aparente que conecta con la dirección geométrica del Sol.
La integral angular incluye el tramo en vacío desde el techo; después la profundidad refractada
usa la coordenada radial transformada `r=r₀+(Rtop-r₀)u²`. Por ello un Sol ligeramente por debajo
del horizonte geométrico conserva un rayo si la trayectoria despeja el planeta, mientras que una
noche más profunda devuelve cero. El número de muestras se fuerza a ser par y nunca baja de ocho.
Esta curvatura es esencial cerca del
horizonte: `1 / cos(zenith)` supone una columna plana infinita y sobre-extingue la luz en
amaneceres, atardeceres, planetas pequeños y cámaras a gran altitud.

Una elevación geométrica por debajo del horizonte no implica automáticamente cero: el solver
refractado acepta el intervalo subhorizonte visible y rechaza direcciones sin camino físico. El
crepúsculo y el airglow siguen siendo fuentes atmosféricas separadas del rayo solar directo.
Las entradas no finitas tampoco pueden convertirse en luz sin atenuar: devuelven cero para que
un frame de inicialización no ilumine la escena por accidente.

La sobrecarga histórica `DirectSolarTransmittance(altitude, sunElevationSin)` conserva un
perfil genérico terrestre para compatibilidad. Los consumidores nuevos deben pasar siempre el
radio del cuerpo y el techo de su atmósfera; de lo contrario Marte, Venus o una luna usarían
la curvatura de la Tierra.

## LUT de transmitancia en tiempo real

`AtmosphereTransmittanceLut` precalcula la misma transmitancia esférica en una tabla RGB de
altitud × seno de elevación solar. Las coordenadas se deforman como `u²` y `v²`; la segunda
abarca `sin(elevación) ∈ [-0,04, 1]` para conservar el horizonte refractado sin gastar texels
en la noche profunda. Se reserva resolución para la troposfera y para los rayos rasantes, donde
la función cambia con mayor rapidez. La tabla se construye una sola vez por cuerpo y se sube como textura lineal HDR al
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
capa. La primera pasada produce el orden dos; una segunda integral anidada vuelve a transportar
ese campo isotrópico y añade un rebote de orden tres, limitado por los coeficientes de scattering.
El resultado es radiancia lineal por unidad de `SunIlluminanceScale`; el shader la
aplica como relleno isotrópico con una leve ponderación hacia el cenit y desactiva el antiguo
S₂ por rayo cuando la tabla está disponible. Así el segundo rebote atraviesa toda la columna
en lugar de depender de los segmentos visibles de una sola dirección.

La tabla global sigue siendo 2D: no representa todavía la dependencia completa del ángulo de visión,
polarización ni rebotes de nube/terreno. Es un transporte global de orden dos verificable, no
una afirmación de que el problema 4D ya esté resuelto.

## Transporte angular 4D empaquetado

`AtmosphereAngularMultipleScatteringLut` toma la semilla global de órdenes dos/tres y la
reproyecta en cuatro coordenadas físicas: altura del observador, elevación solar, coseno
cenital de la cámara y `μ = dot(view, sun)`. La fase Rayleigh normalizada
`3(1+μ²)/4` y la fase Mie de Henyey–Greenstein se mezclan por banda según los coeficientes
locales; una razón `exp(-(τview−τvertical))` aplica la curvatura y la longitud de escape
esférica sin volver a integrar cada píxel.

Godot empaqueta `[μ][view][solar]` en una textura 2D HDR y reconstruye las dos dimensiones
angulares por interpolación lineal. Los rayos groundward se anulan, la rama forward Mie se
conserva y el dominio solar subhorizonte comparte la misma cota refractada de `[-0,04, 1]`.
Es una versión de baja resolución del bloque angular de Bruneton/Neyret: elimina la antigua
ponderación artística cenital, pero todavía no incluye polarización ni una discretización
espectral de muchas longitudes de onda.

## Refracción visible del horizonte

Cada perfil declara la refractividad superficial `n − 1` y su escala vertical. `HorizonRefractionRadians`
integra la pendiente esférica de un perfil exponencial, la limita a 0,035 rad y la usa para
desplazar el disco solar aparente cerca del horizonte. `TrySolveRefractedSolarElevation` integra
la curvatura angular y añade la cola de vacío para encontrar la rama aparente saliente; también
detecta perfiles donde `n·r` forma un ducto y rechaza una raíz imaginaria. En la Tierra produce
aproximadamente 0,56° al nivel del mar y decae exponencialmente con altura; Marte y Venus usan
refractividades específicas de sus columnas de CO₂. No se aplica fuerza ni se cambia la navegación.

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
- que el transporte global de órdenes dos y tres permanece finito, disminuye sobre la columna y
  conserva la dependencia del radio planetario;
- que el atlas angular anula vistas hacia el suelo, conserva el lóbulo forward de Mie y reduce
  el escape en rayos rasantes sin perder la fuente refractada;
- que el solver refractado eleva una fuente subhorizonte visible, rechaza la noche profunda y
  vuelve a la regla geométrica cuando la refractividad es cero;
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
directa, dos rebotes globales y una envolvente angular 4D de baja resolución, y el shader incluye
una corrección acotada del disco solar y de la rama refractada visible, pero aún no modela polarización,
variación espectral por temperatura, perfil de humedad/aerosoles por clima ni el acoplamiento
radiativo entre nubes y terreno, ni una solución de dos ramas para ductos refractivos densos.
El siguiente nivel es una LUT de scattering múltiple de órdenes superiores dependiente de altura,
ángulo solar y ángulo de visión (Bruneton/Neyret), más un integrador refractivo completo para el
disco, el horizonte y las estrellas; esta implementación
C# seguirá siendo el oráculo numérico.
