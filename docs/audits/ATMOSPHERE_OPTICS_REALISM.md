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

## V10 — paridad termodinámica de las LUT (2026-08-10)

Esta iteración cierra el desacoplamiento más visible de V9: la vista primaria usaba la
densidad `P/T`, pero las tablas de transmitancia solar y dispersión múltiple seguían
integrando las exponenciales de `AtmosphereOptics`. El renderer ahora construye un
`AtmosphereDensityProfile` por cuerpo y lo comparte con:

- `AtmosphereTransmittanceLut` (rayos solares diurnos);
- `AtmosphereMultipleScatteringLut` (fuente de órdenes 2/3 y transporte vertical);
- `AtmosphereAngularMultipleScatteringLut` (beta local, tau vertical y escape angular);
- `AtmosphereDensityLut` (perfil filtrado publicado al shader).

El perfil integra la columna vertical con Simpson y warp cuadrático, preservando la
resolución en la troposfera y la cola termósferica. En la implementación V10 las elevaciones
solares subhorizonte usaban una elevación aparente derivada de la refractividad molecular
`P/T` y la misma densidad profile-aware para la profundidad óptica; ese límite histórico
quedó cubierto por el trazador esférico completo de V11.

### Evidencia reproducible

| Gate | Resultado |
| --- | --- |
| `dotnet test ... --filter AtmosphereProfileTransportTests` | **3/3** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 81/81** |
| `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore` | **514/514** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `visual_playtest.sh --atmosphere --run-id atmo-profile-v1 --skip-build` | **ATMOSPHERE_OK** |

La matriz visual V10 produjo capturas en suelo día/amanecer/atardecer/noche, 10/30/70/120
km, 400 km y cockpit día/noche en `/tmp/exo_atmo_profile/`. No reportó `GAP`, `FALLBACK` ni
clipping fuera de los umbrales del runner. El framebuffer confirmó que el cielo se vuelve
negro y recupera estrellas sólo con adaptación nocturna en 400 km/noche, mientras que la
atmósfera conserva el limbo azul en 120 km/día y el gradiente cálido de puesta de sol.

Tras activar el levantamiento de horizonte profile-aware se repitió la matriz como
`atmo-profile-v2`: `ATMOSPHERE_OK`, sin `GAP`/`FALLBACK`, con las 16 capturas en
`/tmp/exo_atmo_profile_v2/`. El test dedicado de rayo subhorizonte confirma que una elevación
geométrica de −0,005 rad conserva transmitancia positiva por refracción; por debajo del
levantamiento calculado el solver devuelve vacío, evitando inventar luz nocturna.

### Verificación posterior al merge en `main`

El merge de la rama de producto expuso una divergencia de API: `main` conservaba la
integración óptica heredada pero no la sobrecarga que recibe `AtmosphereDensityProfile`.
Se restauró una única rutina Simpson (`OpticalDepthAlongRayCore`) y ambos caminos —perfil
termodinámico y compatibilidad heredada— comparten ahora el mismo integrador, cambiando
únicamente el muestreador de densidad. Esto evita que la iluminación directa, las LUT y
las pruebas vuelvan a mezclar perfiles.

Evidencia reproducible en `main`:

| Comprobación | Resultado |
|---|---:|
| `dotnet test ... --no-restore` | **514/514** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings / 0 errors** |
| `tools/atmosphere_quick_check.sh` | **79/79, PASS** |
| `visual_playtest.sh --atmosphere --run-id atmo-main-v1 --skip-build` | **ATMOSPHERE_OK** |

La matriz de `main` conserva las 16 capturas (suelo día/amanecer/atardecer/noche,
10/30/70/120/400 km día/noche y cabina día/noche) en `/tmp/exo_atmo_main/`. La captura
de 120 km muestra un limbo azul fino con transición continua hacia la superficie; la
captura de atardecer mantiene el gradiente naranja y el oscurecimiento nocturno. El
renderizador llvmpipe registra aproximadamente 160 ms por frame en esta VM, por lo que
la lentitud del playtest es de infraestructura y no un bucle físico bloqueado.

## V11 — trazado refractivo profile-aware (2026-08-11)

La refracción dejó de usar la envolvente exponencial heredada cuando el renderer dispone
de `AtmosphereDensityProfile`. El índice se calcula como `n(r)=1+κ·P/(T·P₀/T₀)` y se
propaga por todo el trazador esférico: búsqueda del mínimo de `n·r`, inversión de la
elevación aparente, integración angular de las ramas y profundidad óptica de cada tramo.
Una atmósfera densa puede conservar ahora una fuente subhorizonte sólo si existe una rama
ductada válida; los rayos que intersectan el cuerpo devuelven transmisión nula.

Para no convertir la construcción de LUT en un bloqueo de arranque, las elevaciones solares
por encima de 0,035 rad (≈2°) usan directamente la integral radial profile-aware —la
corrección angular es subpíxel en ese régimen— y el inversor completo queda reservado al
limbo y a las elevaciones negativas. La extinción no vuelve al modelo exponencial en ese
camino rápido.

Evidencia V11:

| Comprobación | Resultado |
|---|---:|
| `AtmosphereProfileTransportTests` | **6/6** |
| suite `ExosphereSimulation.Tests` | **516/516** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings / 0 errors** |
| `tools/atmosphere_quick_check.sh` | **79/79, PASS** |
| Venus profile: rama ductada a 60 km | **PASS** |
| `visual_playtest.sh --atmosphere --run-id atmo-profile-v4 --skip-build` | **ATMOSPHERE_OK** |

La matriz V11 generó las 16 capturas en `/tmp/exo_atmo_profile_v4/` sin `GAP` ni
`FALLBACK`; se revisaron visualmente el atardecer en superficie y el limbo azul a 120 km.

## V12 — continuidad de gradientes en el limbo (2026-08-11)

La captura de puesta de sol mostró franjas horizontales muy finas en el gradiente de baja
luminancia. La causa no era la física ni el número de muestras del rayo: aumentar la
cuadratura del shader no cambió las métricas. El artefacto aparecía al cuantizar el gradiente
HDR al framebuffer y se mitigó en dos puntos:

- la LUT de transmitancia solar pasó de 96 a 192 filas en el eje solar (con warp cuadrático
  hacia el horizonte);
- el shader aplica sólo en `solar_v < 0,35` un filtro simétrico de tres taps y añade un
  dither determinista de amplitud 0,0015 antes del tonemapping. El dither depende de la
  dirección de vista, no de `TIME`, por lo que no produce shimmer temporal.

Evidencia V12:

| Comprobación | Resultado |
|---|---:|
| `visual_playtest.sh --atmosphere --run-id atmo-dither-v1` | **ATMOSPHERE_OK** |
| Capturas de la matriz | **16/16, >8 KB cada una** |
| `PERF ground_sunset` | **160,00 ms/frame** en llvmpipe |
| Cambios de radiancia media atardecer | **sin deriva física apreciable** |

La captura `ground_sunset` de `/tmp/exo_atmo_dither_v1/` mantiene el gradiente rojo/naranja,
la fuente solar y el oscurecimiento nocturno; la vista de 120 km conserva el limbo azul. El
coste adicional del filtrado es local al tercio de LUT cercano al horizonte.

## V13 — reciprocidad de sombras volumétricas de nubes (2026-08-11)

La ruta de iluminación solar de las nubes tenía una simplificación que ya no era coherente
con la cámara: `cloud_density()` incluía weather map, erosión y el campo de billows 3D, pero
`cloud_sun_transmittance()` integraba sólo `cloud_density_base()`. Por eso un volumen podía
mostrar una protuberancia iluminada sin producir la sombra correspondiente. La ruta solar
ahora integra el mismo campo volumétrico mediante Beer–Lambert; se redujo la cuadratura solar
de siete a cinco nodos warped para conservar el presupuesto del render incremental.

El oráculo CPU `CloudVerticalOpticalDepth()` integra la envolvente vertical y verifica que la
profundidad sea finita, no negativa y monótona con la cobertura del mapa. No pretende sustituir
la textura geográfica ni el ruido 3D del shader: fija los invariantes físicos que ambos deben
respetar.

Evidencia reproducible:

| Comprobación | Resultado |
|---|---:|
| `dotnet test ExosphereSimulation.Tests/...` | **517/517** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 80/80** |
| `visual_playtest.sh --atmosphere --run-id atmo-cloudshadow-v3` | **ATMOSPHERE_OK, 16/16** |
| `PERF ground_day` | **160,12 ms/frame**, máximo 173,50 ms |
| `PERF ground_sunset` | **160,00 ms/frame**, máximo 175,68 ms |

La matriz conserva el gradiente de amanecer/atardecer, el limbo azul a 120 km, el campo
estelar nocturno y la captura de cockpit. La puesta de sol todavía muestra bandas de baja
luminancia del framebuffer; esa cuantización queda explícitamente abierta para la siguiente
iteración y no se atribuye a la microfísica de nubes.

## V14 — transición física de airglow/dayglow (2026-08-11)

El airglow visible ya no se apaga con un interruptor al cruzar el terminador. Cada perfil
puede declarar `airglow_daylight_fraction`; Earth usa 0,12 para representar la fracción débil
de dayglow que permanece bajo excitación solar, mientras que los cuerpos sin un perfil de
emisión conservan cero. La función CPU `AirglowSolarVisibility()` y el shader comparten la
misma curva `smoothstep(-0.10, +0.08)` en seno de elevación: la emisión nocturna cae de forma
continua y el suelo diurno no recibe un halo verde artificial.

Evidencia reproducible:

| Comprobación | Resultado |
|---|---:|
| `dotnet test ExosphereSimulation.Tests/...` | **517/517** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 80/80** |
| `visual_playtest.sh --atmosphere --run-id atmo-dayglow-v1` | **ATMOSPHERE_OK, 16/16** |
| `PERF ground_day` | **160,15 ms/frame**, máximo 179,61 ms |
| `PERF 120km_day` | **160,16 ms/frame**, máximo 176,16 ms |
| `neonGreenFrac` en matriz | **0,000000** en las vistas exteriores |

La captura de 120 km conserva el limbo azul y el nightglow sigue siendo ópticamente fino;
la pequeña fracción diurna no cambia la exposición ni introduce un disco verde visible.

## V15 — prefiltro meteorológico adaptativo en crepúsculo (2026-08-11)

La comparación A/B aisló las franjas que quedaban en la puesta de sol: desaparecían al
desactivar las nubes y no cambiaban al aumentar los pasos de integración ni al retirar las
sombras solares. El origen era la magnificación de filas de latitud del mapa equirectangular
de cobertura (8K) a lo largo de los rayos tangentes de baja elevación. Un `textureLod` fijo
mejoraba el horizonte pero lavaba el cielo diurno, por lo que no se dejó como solución global.

El shader ahora conserva el texel de resolución completa durante el día y mezcla, sólo cuando
el seno de elevación solar cae por debajo de 0,18, un prefiltro latitudinal de cinco muestras
(centro, ±64 y ±128 texels verticales). El peso se desvanece suavemente entre +0,02 y +0,18,
de modo que no hay salto visible al cruzar el terminador. El mismo weather filtrado alimenta
la cámara y la integral solar, preservando reciprocidad de sombras.

Evidencia A/B revisada:

| Escena | Resultado |
|---|---:|
| `ground_day` con prefiltro adaptativo | coincide visualmente con la línea base, sin bandas nuevas |
| `ground_sunset` con prefiltro adaptativo | franjas largas eliminadas; sólo queda dither subpíxel aislado |
| `ground_sunset` sin prefiltro | bandas horizontales continuas claramente visibles |

Evidencia reproducible:

| Comprobación | Resultado |
|---|---:|
| `dotnet test ExosphereSimulation.Tests/...` | **517/517** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 80/80** |
| `visual_playtest.sh --atmosphere --run-id adaptivefilter-v1 --skip-build` | **ATMOSPHERE_OK, 16/16** |
| `PERF ground_sunset` | **159,98 ms/frame**, máximo 173,81 ms |
| `PERF ground_night` | **160,02 ms/frame**, máximo 179,70 ms |
| `PERF 120km_day` | **159,77 ms/frame**, máximo 176,17 ms |
| `neonGreenFrac` en matriz exterior | **0,000000** |

La puesta de sol de `/tmp/exo_atmo_adaptivefilter_v1/` conserva el gradiente rojo y el disco
solar sin las franjas continuas de la línea base; `120km_day` mantiene el limbo azul y
`120km_night` el campo estelar nítido. El coste queda dentro del ruido del render incremental
en llvmpipe porque el filtro sólo se activa con el Sol bajo.

## V16 — irradiancia de eclipses con oscurecimiento de borde solar (2026-08-11)

La visibilidad geométrica del disco solar se usaba también como fracción de irradiancia para
la atmósfera. Esa aproximación sobrestima la luz durante una ocultación central: el Sol visible
es más brillante en el centro que en el borde. `MissionGeometry` ahora integra una ley de
oscurecimiento lineal `I(μ)=1-u(1-μ)` (`u=0,60`) mediante cuadratura polar determinista. La
ponderación se aplica al transporte solar atmosférico y a la luz directa del suelo, mientras
que el shader conserva la máscara espacial del ocultador y su limb darkening por píxel.

Evidencia reproducible:

| Comprobación | Resultado |
|---|---:|
| suite `ExosphereSimulation.Tests` | **518/518** |
| `LimbDarkenedDiscVisibility` central vs. borde | **PASS** (ocultar el centro pierde más irradiancia) |
| casos claro/total de disco ponderado | **PASS** (1,0 / 0,0) |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 80/80** |
| `visual_playtest.sh --atmosphere --run-id limbdark-v1 --skip-build` | **ATMOSPHERE_OK, 16/16** |

La matriz `/tmp/exo_atmo_limbdark_v1/` mantiene el atardecer filtrado, el limbo azul a
120 km, el nightglow y el campo estelar; la nueva rama sólo cambia escenas con ocultación
solar y no deriva la exposición en condiciones despejadas.

## V17 — airglow espectral de dos capas (2026-08-11)

El nightglow terrestre estaba representado por una única gaussiana centrada en 97 km. La
implementación ahora conserva esa banda de oxígeno atómico (O₂, verde tenue) y añade una
segunda capa opcional de OH alrededor de 87 km, con emisión roja mucho más débil. Cada perfil
puede declarar `airglow_secondary_emission`, centro y escala independientes; Marte y Venus
mantienen el vector cero por defecto. Ambas capas comparten la visibilidad solar suave del
terminador y la transmitancia de la línea de visión, por lo que no son un halo de postproceso.

Evidencia reproducible:

| Comprobación | Resultado |
|---|---:|
| suite `ExosphereSimulation.Tests` | **518/518** |
| máximos y separación de las dos capas CPU | **PASS** (97 km / 87 km) |
| perfiles JSON Tierra/Marte/Venus | **PASS** |
| `dotnet build Exosphere.csproj --no-restore` | **0 warnings, 0 errors** |
| `bash tools/atmosphere_quick_check.sh` | **PASS, 80/80** |
| `visual_playtest.sh --atmosphere --run-id airglow-v2 --skip-build` | **ATMOSPHERE_OK, 16/16** |
| `neonGreenFrac` en vistas exteriores | **0,000000** |

La matriz `/tmp/exo_atmo_airglow_v2/` conserva el limbo azul, la exposición diurna y el
campo estelar; la banda OH queda deliberadamente por debajo del brillo que produciría un
halo verde artificial, pero aporta una contribución cálida independiente en el modelo lineal.
