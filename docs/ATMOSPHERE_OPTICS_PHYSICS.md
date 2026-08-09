# Contrato físico de la óptica atmosférica

**Estado:** implementado en la simulación y validado numéricamente
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

La profundidad se obtiene integrando `βext(r(s) − R)` con Simpson determinista. El número de
muestras se fuerza a ser par y nunca baja de ocho. Esta curvatura es esencial cerca del
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

Este contrato sigue siendo una aproximación de scattering simple: no modela refracción
atmosférica, polarización, dispersión múltiple global, variación espectral por temperatura,
perfil de humedad/aerosoles por clima ni el acoplamiento radiativo entre nubes y terreno.
Para acercarse a una atmósfera de referencia científica, el siguiente paso es precalcular
transmitancia y scattering múltiple en LUTs dependientes de altura, ángulo solar y ángulo de
visión (Bruneton/Neyret), manteniendo esta implementación C# como oráculo numérico de pruebas.
