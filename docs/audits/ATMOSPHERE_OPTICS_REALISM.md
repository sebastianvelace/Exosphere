# Atmósfera física y óptica planetaria

**Fecha:** 2026-07-11 · **Estado:** V2 óptica en tiempo real verificada

## Veredicto honesto

La atmósfera anterior interpolaba cuatro colores fijos entre 10 y 80 km. No conocía radio
planetario, posición solar, longitud de onda, aerosoles, ozono ni profundidad óptica.

La base integra en GPU una atmósfera esférica desde superficie hasta órbita: Rayleigh
RGB, Mie con fase Henyey–Greenstein, absorción aerosol/ozono, sombra planetaria, extinción
de estrellas y transmitancia solar. Tierra, Marte y Venus cargan perfiles distintos desde
los mismos JSON que usa la simulación.

La V2 añade dos fenómenos perceptuales ausentes: una fuente difusa isotrópica de segundo
orden, limitada por el albedo de dispersión por banda, y adaptación ocular temporal. El ojo
reduce sensibilidad con constante de 0,7 s ante luz intensa y la recupera en 9 s en oscuridad;
la exposición pre-tonemap queda entre 0,65 y 6. Las estrellas obedecen tanto a la luminancia
instantánea del cielo como a esta adaptación lenta, por lo que no aparecen de inmediato al
entrar en eclipse.

No es todavía «totalmente realista»: falta multiple scattering precomputado, nubes
volumétricas, clima/aerosoles variables, polarización, refracción, airglow y calibración
espectral. Venus necesita un modelo multicapa de nubes H₂SO₄; la V1 es aproximada.

## Modelo

```text
ρR(h)=exp(-h/HR), ρM(h)=exp(-h/HM)
βext=βRρR+(βM,sca+βM,abs)ρM+βO3ρO3
T(a→b)=exp(-∫βext ds)
L=∫Tview·Tsun·(βRρR PR(μ)+βMρM PM(μ,g)) ds
S₂≈βsca·ω·(1−Tsun)·k/(4π),  Ldisplay=f(L+∫Tview·S₂ ds)
```

El rayo intersecta las esferas de superficie y techo atmosférico. Doce muestras de vista y
seis solares producen limb, twilight y transición órbita/superficie sin bandas de altitud.
La sombra del planeta corta el Sol directo y también anula explícitamente S₂: una superficie
opaca no se interpreta como luz dispersada. `AtmosphereOptics` replica profundidad óptica y
transmitancia en C#; la luz usa masa de aire Kasten–Young para sunsets rojos.

S₂ es un cierre local y acotado, no un transporte global: recupera parte del relleno perdido
por single scattering sin exceder su límite local, pero no garantiza conservación energética
global, no transporta rebotes espaciales/angulares,
no reproduce correctamente el twilight profundo ni sustituye las LUTs 4D de Bruneton. Sus
intensidades son datos por planeta: Tierra 0,25, Marte 0,08 y Venus 0,40.

| Cuerpo | Rayleigh HR | Aerosol HM | Rasgo V1 |
|---|---:|---:|---|
| Tierra | 8,0 km | 1,2 km | N₂/O₂ + aerosol + ozono estratosférico |
| Marte | 11,1 km | 11,0 km | CO₂ tenue + polvo absorbente en azul |
| Venus | 15,0 km | 15,0 km | CO₂/nube agregada fuertemente absorbente |

Los coeficientes terrestres RGB siguen el modelo Bruneton. Marte/Venus son hipótesis
calibrables en datos, no mediciones oficiales.

## Evidencia

- 43 pruebas atmosféricas focales: USSA-76, termosfera, JSON, profundidad óptica,
  transmitancia, ozono, enrojecimiento del Sol bajo y fuente difusa acotada/sombreada.
- 5 pruebas de adaptación ocular: asimetría luz/oscuridad, monotonía, límites e
  independencia de la partición temporal.
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

1. LUTs 4D con multiple scattering y unidades espectrales; reemplazar el cierre S₂ local.
2. Nubes volumétricas con optical depth, self-shadow y viento.
3. Aerosoles por clima/latitud y capas Venus validadas.
4. Refracción, polarización y airglow.
5. Golden por cuerpo: mediodía, sunset, 20/50/80/150/400 km y eclipse.
