# Atmósfera física y óptica planetaria

**Fecha:** 2026-08-09 · **Estado:** V9 LUT de densidad termodinámica integrada en la vista; auditoría visual completa pendiente

## Veredicto honesto

La atmósfera anterior interpolaba cuatro colores fijos entre 10 y 80 km. No conocía radio
planetario, posición solar, longitud de onda, aerosoles, ozono ni profundidad óptica.

La base integra en GPU una atmósfera esférica desde superficie hasta órbita: Rayleigh
RGB, Mie con fase Henyey–Greenstein, absorción aerosol/ozono, sombra planetaria, extinción
de estrellas y transmitancia solar. Tierra, Marte y Venus cargan perfiles distintos desde
los mismos JSON que usa la simulación.

La V6 añade una LUT de transmitancia solar esférica, construida con el mismo integrador
determinista que usan las pruebas y la exposición. La tabla concentra resolución en el
horizonte y la troposfera, se cachea por cuerpo y reemplaza la cuadratura solar repetida en
cada píxel por una interpolación HDR estable. La tabla cubre además `sin(elevación) ∈ [-0,04, 1]`,
por lo que el horizonte aparente no se recorta al cruzar cero geométrico. También añade una LUT
global que transporta la fuente difusa a través de toda la columna: la primera pasada produce
orden dos y una segunda integral añade un rebote isotrópico de orden tres, reemplazando el cierre
S₂ local cuando está disponible. `DirectSolarTransmittance` resuelve la elevación aparente con
Snell, integra la rama refractada y rechaza ductos no atravesables en vez de inventar energía.
Cuando el observador está sobre un mínimo de `n·r`, el trazador V7 encuentra el punto de retorno
y suma las dos ramas sólo si el camino despeja la superficie; el perfil Venus a 60 km ya está
cubierto por una prueba de transmisión y profundidad óptica finitas.
Sobre esa semilla, `AtmosphereAngularMultipleScatteringLut` añade un atlas 4D empaquetado
(altura, elevación solar, vista cenital y `μ=view·sun`). La fase Rayleigh/Mie y el cociente
de escape esférico sustituyen la vieja ganancia isotrópica hacia el cenit; las vistas hacia el
suelo quedan a cero y la dispersión Mie hacia delante aparece sólo cuando la geometría lo pide.
La V8 añade a la cáscara nubosa un campo de ruido 3D determinista de baja frecuencia. El mapa
meteorológico equirectangular sigue definiendo cobertura geográfica y detalle de erosión, mientras
el campo macro varía dentro del volumen para producir billows sin una textura 3D adicional. La
advección comparte el desplazamiento longitudinal existente. El rayo de vista usa la densidad
macro+weather; la autosombra solar conserva el mismo perfil vertical y mapa meteorológico, evitando
recalcular el ruido costoso siete veces por muestra en llvmpipe. Así la sombra sigue siendo coherente
con la cobertura observada, con una aproximación explícita y acotada.

V9 añade `AtmosphereDensityLut`, una tabla vertical de 256 texels que comparte con el shader
el perfil de especies atmosféricas. El canal Rayleigh usa `P/T` para seguir la densidad numérica
del estado termodinámico; cuando la presión deja de estar definida en la cola de la termósfera,
usa la densidad másica residual como fallback continuo. El canal Mie combina la envolvente de
aerosoles con un límite de masa disponible y el canal O₃ conserva la forma normalizada del
perfil de ozono. La tabla usa un warp vertical cuadrático al construirla y `sqrt` al muestrearla,
reservando resolución para la atmósfera baja sin descartar la cola termósferica.

`SkyController` genera la tabla desde el mismo `AtmosphereModel`, la cachea por cuerpo y la
publica como `density_lut` junto con `density_lut_top_altitude`. El shader sólo habilita esta
fuente cuando el binding está listo y conserva el perfil exponencial como fallback. El techo de
la LUT se mantiene separado de `atmosphere_height`: en V9 la cáscara geométrica visible sigue
en el techo óptico actual (140 km para el perfil terrestre), aunque la tabla pueda representar
una termósfera hasta 1.000 km. No se debe interpretar esa cola tabulada como volumen visible
hasta que las LUTs de transmitancia solar y scattering se extiendan de forma consistente.

El límite importante de esta iteración es la paridad: la densidad termodinámica ya alimenta el
camino de vista, pero `AtmosphereTransmittanceLut` y `AtmosphereMultipleScatteringLut` siguen
usando el perfil exponencial de `AtmosphereOptics`. Por ello V9 mejora la plausibilidad vertical
del cielo, pero todavía no representa un modelo termodinámico único para todos los caminos
radiativos.

La V2 ya había añadido dos fenómenos perceptuales ausentes: una fuente difusa isotrópica de segundo
orden, limitada por el albedo de dispersión por banda, y adaptación ocular temporal. El ojo
reduce sensibilidad con constante de 0,7 s ante luz intensa y la recupera en 9 s en oscuridad;
la exposición pre-tonemap queda entre 0,65 y 6. Las estrellas obedecen tanto a la luminancia
instantánea del cielo como a esta adaptación lenta, por lo que no aparecen de inmediato al
entrar en eclipse.

No es todavía «totalmente realista»: falta multiple scattering de órdenes superiores a tres, un trazador
refractivo de distancia finita para estrellas, nubes microfísicas, clima/aerosoles variables,
polarización y calibración espectral. Venus necesita
un modelo multicapa de nubes H₂SO₄; el airglow actual es una capa visible calibrada, no un
modelo químico completo.

## Modelo

```text
ρR(h)=exp(-h/HR), ρM(h)=exp(-h/HM)
βext=βRρR+(βM,sca+βM,abs)ρM+βO3ρO3
T(a→b)=exp(-∫βext ds)
L=∫Tview·Tsun·(βRρR PR(μ)+βMρM PM(μ,g)) ds
L₂(h,μs)=∫Tview·βsca·ω·(1−Tsun(μs))/(4π) ds,
L₃(h,μs)=∫Tview·βsca·L₂(h′,μs) ds,
Ldisplay=f(L+L₂+L₃)
```

El rayo intersecta las esferas de superficie y techo atmosférico. Doce muestras de vista y
seis solares producen limb, twilight y transición órbita/superficie sin bandas de altitud.
La sombra del planeta corta el Sol directo y también anula explícitamente S₂: una superficie
opaca no se interpreta como luz dispersada. `AtmosphereOptics` replica profundidad óptica y
transmitancia en C#; la luz usa masa de aire Kasten–Young para sunsets rojos.

El transporte de órdenes dos y tres es global en altura pero isotrópico en ángulo: recupera parte del
relleno perdido por single scattering sin exceder el límite local, pero no garantiza todavía
conservación energética global, no transporta rebotes espaciales/angulares completos y no
sustituye las LUTs 4D de Bruneton. Sus intensidades son datos por planeta: Tierra 0,25, Marte
0,08 y Venus 0,40.

| Cuerpo | Rayleigh HR | Aerosol HM | Rasgo V1 |
|---|---:|---:|---|
| Tierra | 8,0 km | 1,2 km | N₂/O₂ + aerosol + ozono estratosférico |
| Marte | 11,1 km | 11,0 km | CO₂ tenue + polvo absorbente en azul |
| Venus | 15,0 km | 15,0 km | CO₂/nube agregada fuertemente absorbente |

Los coeficientes terrestres RGB siguen el modelo Bruneton. Marte/Venus son hipótesis
calibrables en datos, no mediciones oficiales.

## Evidencia

- 38 pruebas atmosféricas focales: USSA-76, termosfera, JSON, profundidad óptica,
  transmitancia, ozono, enrojecimiento del Sol bajo, fuente difusa acotada/sombreada y
  dieciséis invariantes nuevas de LUT, transporte global, atlas angular, horizonte subhorizonte,
  ductos Venus y refracción.
- 5 pruebas de adaptación ocular: asimetría luz/oscuridad, monotonía, límites e
  independencia de la partición temporal.
- Matriz framebuffer 12 m/20 km/80 km: limb curvo, espacio negro fuera de columna y
  estrellas atenuadas a través de atmósfera.
- Matriz visual V8 completa: 16/16 capturas entre 20 m y 400 km, día/noche y cockpit,
  `ATMOSPHERE_OK`, sin `GAP`/`FALLBACK`; mean frame time estable en ~160 ms en llvmpipe.
- V9: las pruebas de `AtmosphereDensityLut` cubren techo termósferico, normalización finita,
  monotonicidad, fallback de densidad residual, máximo de ozono, warp, vacío fuera de dominio,
  perfiles Tierra/Marte/Venus y el fallback exponencial sin capas (9/9). El chequeo atmosférico
  integrado mantiene 79/79 pruebas y el smoke de Godot pasa.
  La matriz framebuffer completa V9 (16 estados, incluidos 10/30/400 km y cockpit) sigue
  pendiente; la corrida preliminar llega a día, amanecer, atardecer y noche sin `GAP` ni
  `FALLBACK`, pero termina por el presupuesto de llvmpipe antes de completar todos los estados.
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

1. Reemplazar el perfil exponencial de los oráculos de transmitancia solar y scattering múltiple
   por la misma densidad termodinámica de V9; después ampliar de forma coordinada el techo de
   la cáscara, la LUT solar y el scattering para visualizar la termósfera sin discontinuidades.
2. Repetir la matriz framebuffer completa de 16 estados y añadir golden checks para la LUT de
   densidad en Tierra, Marte y Venus; comparar 20/50/80/150/400 km, día/noche, eclipse y cockpit.
3. Aumentar la resolución de la LUT 4D angular y añadir órdenes superiores a tres con unidades
   espectrales calibradas; la versión actual es una envolvente de baja resolución.
4. Evolucionar las nubes volumétricas actuales a weather map dinámica, microfísica por especie,
   sombras proyectadas sobre terreno y aerial perspective segmentada. El ruido macro 3D de V8 es
   un campo geométrico de baja frecuencia, no un modelo de convección ni precipitación.
5. Aerosoles por clima/latitud y capas Venus validadas.
6. Trazador refractivo de distancia finita para estrellas; polarización y química del airglow.
