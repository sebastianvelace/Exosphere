# Atmósfera física y óptica planetaria

**Fecha:** 2026-07-11 · **Estado:** single-scattering V1 verificado

## Veredicto honesto

La atmósfera anterior interpolaba cuatro colores fijos entre 10 y 80 km. No conocía radio
planetario, posición solar, longitud de onda, aerosoles, ozono ni profundidad óptica.

La V1 nueva integra en GPU una atmósfera esférica desde superficie hasta órbita: Rayleigh
RGB, Mie con fase Henyey–Greenstein, absorción aerosol/ozono, sombra planetaria, extinción
de estrellas y transmitancia solar. Tierra, Marte y Venus cargan perfiles distintos desde
los mismos JSON que usa la simulación.

No es todavía «totalmente realista»: falta multiple scattering precomputado, nubes
volumétricas, clima/aerosoles variables, polarización, refracción, airglow y calibración
espectral. Venus necesita un modelo multicapa de nubes H₂SO₄; la V1 es aproximada.

## Modelo

```text
ρR(h)=exp(-h/HR), ρM(h)=exp(-h/HM)
βext=βRρR+(βM,sca+βM,abs)ρM+βO3ρO3
T(a→b)=exp(-∫βext ds)
L=∫Tview·Tsun·(βRρR PR(μ)+βMρM PM(μ,g)) ds
```

El rayo intersecta las esferas de superficie y techo atmosférico. Doce muestras de vista y
seis solares producen limb, twilight y transición órbita/superficie sin bandas de altitud.
La sombra del planeta corta el Sol directo. `AtmosphereOptics` replica profundidad óptica y
transmitancia en C#; la luz usa masa de aire Kasten–Young para sunsets rojos.

| Cuerpo | Rayleigh HR | Aerosol HM | Rasgo V1 |
|---|---:|---:|---|
| Tierra | 8,0 km | 1,2 km | N₂/O₂ + aerosol + ozono estratosférico |
| Marte | 11,1 km | 11,0 km | CO₂ tenue + polvo absorbente en azul |
| Venus | 15,0 km | 15,0 km | CO₂/nube agregada fuertemente absorbente |

Los coeficientes terrestres RGB siguen el modelo Bruneton. Marte/Venus son hipótesis
calibrables en datos, no mediciones oficiales.

## Evidencia

- 40 pruebas focales: USSA-76, termosfera, JSON, profundidad óptica, transmitancia,
  ozono y enrojecimiento del Sol bajo.
- Matriz framebuffer 12 m/20 km/80 km: limb curvo, espacio negro fuera de columna y
  estrellas atenuadas a través de atmósfera.
- Godot carga/compila el shader y los assemblies terminan con 0 warnings.

```bash
bash tools/atmosphere_quick_check.sh
```

## Fuentes primarias

- Bruneton & Neyret: https://doi.org/10.1111/j.1467-8659.2008.01245.x
- Implementación dimensional/testeada: https://ebruneton.github.io/precomputed_atmospheric_scattering/
- NASA GSFC, Rayleigh/Mie: https://acd-ext.gsfc.nasa.gov/anonftp/acd/daac_ozone/Lecture4/Text/Lecture_4/raymie.html
- NASA Ocean Color, Rayleigh/ozono: https://oceancolor.gsfc.nasa.gov/resources/docs/rsr_tables/

## Próximos gates

1. LUTs 4D con multiple scattering y unidades espectrales.
2. Exposición temporal de cámara/IVA y adaptación ocular.
3. Nubes volumétricas con optical depth, self-shadow y viento.
4. Aerosoles por clima/latitud y capas Venus validadas.
5. Refracción, polarización y airglow.
6. Golden por cuerpo: mediodía, sunset, 20/50/80/150/400 km y eclipse.
