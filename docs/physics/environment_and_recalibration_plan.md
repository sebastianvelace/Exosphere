# Plan: entorno fechado (tiempo, efemérides, gravedad, viento) y recalibración histórica

Estado: plan, 2026-11-08. Nada de esto está implementado todavía. Sigue a F0–F5 (cerradas) y se
intercala con el cierre de F6. Orden de prioridad: física primero, visual al final. Cada fase
cierra con una compuerta analítica y otra histórica registradas en `fidelity_gates.md`, con
procedencia en `data/provenance/*.json`, sin aflojar tolerancias existentes y con el
interruptor nuevo apagado hasta pasar paridad.

## 0. Diagnóstico del código actual (verificado 2026-11-08)

| Tema | Estado en el código | Consecuencia |
| --- | --- | --- |
| Tiempo | `Universe.CurrentTime` = segundos desde J2000, sin escala declarada (TT/TDB/UTC) | Ninguna misión puede arrancar en su fecha real |
| Efemérides | Cada cuerpo tiene elementos keplerianos fijos de dos cuerpos (`data/bodies/*.json`, `epoch` 0); el Sol está fijo en el origen (`Universe.BodyStateAt`) | La Luna no tiene perturbaciones solares (regresión nodal 18,6 años, precesión apsidal 8,85 años, errores de miles de km); geometría Tierra–Luna–Sol arbitraria en Apollo 8/11 |
| Rotación terrestre | `RotationAxis` es fijo (eclíptica J2000 inclinada `axial_tilt` hacia +Y); el meridiano cero es una convención de la simulación en `t = 0`, no Greenwich (`GetSurfacePositionAtTime`) | Entre 1969 y 2000 la precesión movió el polo ~0,43°: sitios de lanzamiento y planos orbitales fechados no pueden ser correctos |
| Luna como cuerpo | Polo aproximado (mismo acimut de inclinación que todos), periodo sidéreo, sin libraciones | Sin marco de ejes principales, no se puede usar un campo de gravedad lunar real |
| Gravedad | Punto masa para todos los cuerpos + J2 de primer orden (`CelestialBody.GetGravityAt`) en una base ecuatorial que no rota | Sin J3/J4 ni teserales terrestres; sin mascons lunares (SP-238: el potencial lunar movió la órbita de Apollo 11 ~2,5 mi náuticas) |
| Atmósfera | Un perfil fijo por cuerpo (capas USSA-76 en `data/bodies/earth.json`) y cola termosférica | Sin variación diurna, estacional ni solar; decaimiento en LEO sin clima espacial |
| Viento | No existe. La velocidad aérea es `v − v_cuerpo − ω×r`, calculada por separado en al menos 8 sitios (`Vessel.cs` líneas ~537, 826, 936, 1042, 1080, 1108; `Universe.cs` ~2039; `GravityTurnGuidance`) | Ninguna carga de viento, cizalladura ni deriva; no hay un punto único donde agregarlo |

## 1. Principios de implementación (aplican a todas las fases)

1. **Un solo punto de verdad por magnitud.** Escala de tiempo, estado de cuerpos, orientación
   de cuerpos, gravedad y aire relativo se consultan con una sola función cada uno.
2. **Datos fuera del código.** Coeficientes y tablas se generan con herramientas Python
   reproducibles en `tools/ephemeris/` a partir de los archivos primarios, se guardan en `data/`
   con hash SHA-256 del archivo fuente, y cada valor de fidelidad tiene registro de procedencia.
3. **Interruptor por fase, apagado por defecto** (`Universe.DatedEnvironmentEnabled`,
   `Universe.WindEnabled`, etc.) hasta que la paridad con el comportamiento actual esté probada
   con el interruptor apagado y las compuertas nuevas pasen con él encendido.
4. **Determinismo y guardado.** Toda entrada nueva (época de misión, perfil de viento, semilla de
   ráfagas) va en `SaveGameV2` con campos opcionales; un guardado viejo carga con el
   comportamiento viejo.
5. **Presupuesto de rendimiento.** Cada evaluación nueva se mide por paso RK4 a 50 Hz; objetivo
   menor a 10 µs por evaluación de gravedad y menor a 2 µs por estado de cuerpo. Los modelos se
   evalúan en raíles solo donde importan.
6. **Pruebas rápidas primero.** Las compuertas analíticas no pueden tardar más de segundos; las
   históricas largas se marcan en `tools/slow_tests.txt`.

## 2. Fase E1 — Tiempo y orientación de la Tierra

**Objetivo.** Que `CurrentTime` sea TDB en segundos desde J2000.0 (2000-01-01 12:00 TT) y que
la Tierra tenga orientación fechada (precesión, nutación, rotación con UT1).

**Fuentes primarias.**
- IERS Conventions 2010 (TN36), capítulos 5 (transformación celeste–terrestre) y 10 (escalas).
- IAU SOFA (rutinas de referencia para validar; revisar su licencia antes de portar código;
  la alternativa es implementar desde TN36 y usar SOFA solo como oráculo en pruebas).
- IERS EOP 14 C04 / 20 C04: UT1−UTC y movimiento del polo diarios desde 1962.
- Tabla TAI−UTC (USNO/IERS), incluida la UTC "elástica" de 1961–1971.

**Diseño.**
- `Time/TimeScales`: UTC↔TAI↔TT↔TDB (TDB−TT por la serie de Fairhead–Bretagnon truncada,
  ~2 ms) y UT1 desde la tabla EOP. Entrada de misión en UTC civil; conversión una vez.
- `Earth/EarthOrientation`: precesión IAU 2006, nutación IAU 2000B (error < 1 mas, suficiente),
  ERA(UT1); movimiento del polo opcional (≤ 15 m en superficie). Devuelve la rotación del marco
  terrestre (ITRS) al celeste (GCRS).
- Marco de la simulación: hoy es eclíptico J2000. Se mantiene como marco inercial de trabajo
  (rotación fija por la oblicuidad IAU 2006 ε0 = 84381,406″ desde ICRF; el sesgo de marco de
  ~23 mas se documenta y se ignora). `CelestialBody.RotationAxis` y la base de superficie pasan
  a depender del tiempo.
- `GetSurfacePositionAtTime`, `ToBodyFixedDirection`, `GetSurfaceVelocity` y la base de J2
  consumen la misma orientación; la longitud pasa a ser Greenwich real.
- Época por misión: `MissionDefinition.LaunchEpochUtc` (por ejemplo Apollo 11
  1969-07-16 13:32:00 UTC; SP-238 tabla 3-I) y `SaveGameV2.EpochScale`.

**Compuertas.**
- Analíticas: ida y vuelta de escalas (error < 1 µs); casos de prueba publicados de SOFA para
  precesión-nutación (< 1 mas) y ERA; la velocidad de superficie sigue siendo la derivada
  exacta de la posición de superficie (la prueba actual se conserva).
- Histórica: posición inercial de LC-39A en el lanzamiento de Apollo 11 contra una referencia
  independiente (Horizons/SPICE con ITRF); el acimut de lanzamiento de 72,058° del AS-506 se
  reproduce con el plano de la Luna fechado (enlaza con E2).

**Riesgos.** Los perfiles que hoy dependen de la convención `t = 0` (sitios, ventanas lunares,
Flight 14) cambian de geometría: se recalibran solo con el interruptor encendido, conservando
sus tolerancias originales.

## 3. Fase E2 — Efemérides fechadas

**Objetivo.** Estados del Sol, la Luna, los planetas y el baricentro Tierra–Luna desde JPL
DE440, más los ángulos de libración lunar, para cualquier fecha entre 1957 y 2035.

**Fuentes primarias.**
- Park et al. 2021, AJ 161, 105 (DE440/DE441). DE440 cubre 1550–2650; DE440s (≈32 MB) cubre
  1849–2150. NAIF recomienda DE440 para datos modernos; DE441 para épocas muy antiguas.
- Formato: polinomios de Chebyshev por intervalos; incluye libraciones lunares (ángulos de Euler
  del manto) integradas junto con las órbitas.

**Diseño.**
- `tools/ephemeris/extract_de440.py` (jplephem o el formato ASCII de JPL) extrae 1957–2035 para
  Sol, Mercurio–Saturno, BTL, Luna geocéntrica y libraciones; escribe un binario compacto
  `data/ephemerides/de440_1957_2035.bin` (estimado < 8 MB; verificarlo al generar) con su hash.
- `Ephemeris/ChebyshevEphemeris`: evaluación de posición y velocidad (derivada analítica de
  Chebyshev), sin asignaciones por llamada.
- `Universe.BodyStateAt` usa la efeméride cuando el interruptor está encendido; los elementos
  keplerianos quedan como respaldo fuera del rango y para el modo sandbox sin fecha.
- La Tierra deja de orbitar el Sol con dos cuerpos: Tierra = BTL − Luna·μL/(μT+μL).
- El Sol deja de estar fijo en el origen: origen = baricentro del sistema solar (ICRF), rotado
  al marco de trabajo. Comprobar que la precisión doble alcanza (1 AU ≈ 1,5e11 m: resolución
  ~3e-5 m; suficiente).
- Marcos lunares: orientación del manto desde las libraciones de DE440 (marco de ejes
  principales); IAU WGCCRE como respaldo.
- Raíles y SOI: `KeplerPropagator` para naves sigue siendo cónico respecto a su cuerpo, pero los
  cuerpos se mueven por la efeméride; revisar el cruce de SOI con cuerpos no keplerianos (el
  contrato `BodyStateAt(body, t)` ya existe).

**Compuertas.**
- Analíticas: la evaluación reproduce los vectores de prueba que publica JPL para DE440
  (archivo `testpo`) a mejor que 1 mm; continuidad de posición y velocidad entre intervalos.
- Históricas: (a) la geometría Tierra–Luna en la TLI de Apollo 11 contra SP-238 tabla 7-II;
  (b) con guiado fechado, la hora de LOI de Apollo 11 (75:49:50 GET) y la de Apollo 8 dentro de
  una banda justificada por el error de la TLI simulada.

**Riesgos.** Las misiones lunares hoy calibradas con la Luna kepleriana cambiarán; las
compuertas de Apollo se re-evalúan con su fecha real, no se ajustan para conservar números viejos.

## 4. Fase E3 — Gravedad

**Objetivo.** Gravedad terrestre con armónicos hasta grado configurable y gravedad lunar con
mascons, en marcos fijos al cuerpo fechados (E1/E2).

**Fuentes primarias.**
- Tierra: EGM2008 (NGA, completo a grado y orden 2159; archivo `EGM2008_to2190_TideFree`);
  usar grado ≤ 20 para LEO y ≤ 8 para cálculo rápido. Alternativa histórica: JGM-3 20×20.
- Luna: GRAIL GRGM1200A (PDS Geosciences, `GGGRX_1200A_SHA.TAB`, coeficientes normalizados 4π,
  radio de referencia 1738 km). Confirmar en la etiqueta PDS el marco (ejes principales de la
  efeméride usada por GRAIL) y transformarlo al marco de DE440 si difiere. Truncar a grado
  50–100 según la altitud mínima (para 100 km, comparar las aceleraciones de grado 50, 70 y 100
  y elegir el corte por error y no por costo).

**Diseño.**
- `Physics/SphericalHarmonicGravity`: recursión de Pines (sin singularidad en los polos) o de
  Cunningham normalizada, coeficientes en un arreglo plano, aceleración en el marco fijo al cuerpo
  rotada al marco inercial. Precomputar factores; cero asignaciones.
- `CelestialBody.Gravity` pasa a ser una estrategia: punto masa / J2 actual (paridad) /
  armónicos esféricos. El J2 actual queda como caso particular comprobado.
- Rendimiento: grado n cuesta O(n²); medir y fijar grado por cuerpo en el JSON
  (`gravity_model`, `max_degree`) con procedencia.
- Gradiente de gravedad (F4) sigue usando el punto masa salvo que se demuestre necesario.

**Compuertas.**
- Analíticas: aceleración contra una implementación de referencia independiente (pyshtools o
  evaluación directa de Legendre en Python) en 200 puntos, error relativo < 1e-12; potencial y
  aceleración consistentes (diferencias finitas); J2-only reproduce el modelo actual bit a bit
  o < 1e-15; precesión nodal por J2 de una órbita circular contra la fórmula analítica.
- Históricas lunares: SP-238 sección 7: desde la órbita de circularización de 53,8 × 65,7 mi
  náuticas a las 80:12 GET, la órbita a las 126 h fue de 56,6 × 62,4 mi náuticas (el modelo R2
  de la época predecía 59,3 × 59,9); el modelo debe acercarse a lo observado más que R2. Apollo 8
  (Apollo 8 Mission Report, MSC-PA-R-69-1; verificar la designación) como segundo caso. Bandas a justificar con la incertidumbre del
  estado inicial antes de mirar el resultado.
- Histórica terrestre: deriva nodal y apsidal de una órbita de estacionamiento publicada
  (Gemini/Apollo) durante varias revoluciones.

## 5. Fase E4 — Atmósfera y viento

**Objetivo.** Viento medido o modelado en todas las altitudes donde hay aire, densidad variable
con la fecha y ráfagas opcionales, con un único punto de cálculo del aire relativo.

**Paso previo obligatorio (refactor sin cambio de comportamiento).**
`CelestialBody.GetAirVelocity(position, time)` = rotación del cuerpo + viento. Reemplazar los
~8 cálculos repetidos de `v − v_cuerpo − ω×r` por `vessel.GetAirRelativeVelocity(body)`; con el
viento en cero, toda la suite debe quedar bit a bit igual (compuerta de paridad).

**Fuentes primarias.**
- Viento de lanzamiento histórico: los FER de Saturn V publican viento escalar, dirección,
  componentes de cabeceo/guiñado y cizalladuras en el apéndice A (AS-506: figuras A-1 a A-5,
  tablas A-3 a A-5; viento en superficie 3,3 m/s del sur a 18,3 m; pico 9,6 m/s a 11,40 km).
- Sondeos numéricos: NOAA IGRA v2 (radiosondas desde 1905, viento incluido). Estaciones a
  confirmar en la lista de IGRA: Cabo Cañaveral (USM00074794) y Brownsville (USM00072250, para
  Starbase).
- Termosfera: HWM14 (viento horizontal, NRL; código Fortran y tres archivos binarios de datos).
- Densidad: NRLMSIS 2.0 (suelo a exobase) con F10.7 y Ap históricos de CelesTrak (`SW-All.txt`).
- Ráfagas: espectros de Dryden o von Kármán (MIL-HDBK-1797) con semilla fija.

**Diseño.**
- `Environment/WindField`: interfaz `Vector3d WindAt(geodeticLat, lon, altitude, time)` en el
  marco local ENU convertido a inercial; implementaciones: cero, perfil por altitud tabulado por
  misión (`data/winds/<mission>.json`, digitalizado del FER con procedencia o importado de IGRA),
  HWM14 por encima del perfil medido, mezcla suave entre capas.
- `Environment/AtmosphereModel`: el perfil USSA-76 actual queda como caso por defecto;
  NRLMSIS 2.0 opcional por fecha. La presión que usan los motores y la densidad que usa la
  aerodinámica salen del mismo estado.
- Ráfagas: filtro de Dryden discreto en el paso de control, determinista por semilla.
- Rendimiento: el perfil se evalúa por interpolación en tabla; NRLMSIS solo por encima de 80 km
  o con caché por paso.

**Compuertas.**
- Analíticas: viento uniforme ⇒ una partícula solo con arrastre alcanza la velocidad del viento
  con la constante de tiempo m/(½ρCdA·v); paridad exacta con viento cero; NRLMSIS contra sus
  vectores de prueba publicados; espectro de la ráfaga sintética contra el de Dryden.
- Históricas (AS-506, FER tabla 11-2 y figura 11-5), con el perfil de viento del día:
  ángulo de ataque máximo 1,82° en cabeceo (T+117,1 s) y 1,42° en guiñado (T+73,0 s); producto
  Qα máximo 4,93 deg·N/cm² (T+91,4 s); deflexión media de gimbal 0,23°/−0,44°; aceleración
  normal −0,331 / 0,306 m/s². Bandas fijadas antes de correr, desde la incertidumbre del perfil
  (resolución de la figura) y del modelo de normal-force.

**Riesgos.** El guiado actual de ascenso no compensa viento; con viento real puede necesitar
el bias de viento que usaban los programas de inclinación (documentar, no inventar ganancias).

## 6. Cierre de F6 (recalibración histórica) — pendientes concretos

Orden por impacto físico; cada uno con su fuente primaria y compuerta:

1. **AS-506 inserción** (+10 s, −6,7 t de S-IVB, −7,6 t de vehículo frente al FER): reconstruir
   flujo y mezcla del F-1 en altura (el par empuje/Isp de vacío implica 2712 kg/s contra 2616 a
   nivel del mar), la época inicial y el PU del J-2. Fuente: FER tablas 5-I y 20-x.
2. **Tanques del SPS**: cuatro tanques fuera del eje (almacenamiento y sumidero por
   constituyente), drenaje almacenamiento→sumidero, residuos no expulsables y criogénicos
   (SP-238 tablas 8-X a 8-XII; CSM Data Book para la geometría). Cierra el residuo de giración
   de +6 % del CSM en el TD&E.
3. **Salida de tobera del SPS** para la configuración CSM-107 (el documento de 1966 es de otra
   versión): buscar el CSM Data Book o el AOH de 1969.
4. **MA-6 Atlas nativo**: sustainer, verniers y PU desde el ledger previo al vuelo ya localizado
   (`atlas_ma6_reconstruction.md`), reemplazando el sustainer efectivo de calibración.
5. **Aerodinámica pendiente**: aletas del S-IC (Saturn V CP/Cmα de túnel de viento NASA),
   grid fins de Super Heavy, modelo newtoniano modificado para cápsulas romas (Mercury, Gemini,
   Apollo CM) con sus datos de túnel.
6. **Estaciones S-II/S-IVB estimadas** → datos del FER o del manual del vehículo.
7. **AS-503**: misma geometría de tanques que AS-506 para que F5 aplique a Apollo 8.
8. **Gemini 8**: lanzamiento con el Agena en su órbita fechada (requiere E1/E2), no colocado
   arriba por geometría de escenario.
9. **Flight 14**: marco y dato de la velocidad mostrada en la transmisión y el desajuste de
   velocidad hipersónica tardía; selección exacta del océano.

## 7. Mejoras pendientes de la física fuera de F6 (después de E1–E4)

| Fase propuesta | Contenido | Fuente / compuerta guía |
| --- | --- | --- |
| G1 Guiado | IGM iterativa para la inserción de S-II/S-IVB y la TLI (hoy prograda) | MSFC IGM equations; TLI residual ~6 m/s en la primera MCC de Apollo |
| F7 Flexión | Modos de viga por etapa acoplados al TVC y a los sensores | FER AS-506 dice efecto despreciable en Saturn V: priorizar Starship |
| F8 Térmica | Ablación y recesión del escudo, conducción interna, apagón de plasma | Datos de vuelo de Apollo 4/6 (calorímetros), FIRE II |
| F9 Motores | Transitorios de encendido/apagado, dinámica del actuador de gimbal, deriva de mezcla, modos de falla | FER tablas de arranque F-1/J-2; Raptor sin datos públicos (estimado) |
| F10 Slosh | Modos superiores y líquido no asentado en costa | SP-106 cap. 11 (baja gravedad) |

## 8. Visual (después de la física)

1. Composición atmósfera–superficie (Tierra en espacio escalado tapando el casco, bruma
   opaca, capa cian fija).
2. Comparación fotográfica lado a lado: hot-staging, encendido, pluma de vacío, reentrada,
   grid fins, casco, torre.
3. Nubes con silueta y clima coherente con el viento de E4 (mismo dato de la fecha).
4. Reentrada con choque y plasma más ricos; agua y salpicadura; captura en torre.
5. Capturas automáticas en CI con heurísticas.

## 9. Secuencia y criterios de salida

| Paso | Entregable | Salida |
| --- | --- | --- |
| 1 | Refactor del aire relativo a un único punto (E4 paso previo) | Suite bit a bit igual |
| 2 | E1 escalas de tiempo + orientación terrestre | Compuertas E1; interruptor apagado sin cambios |
| 3 | E2 efemérides DE440 | Vectores de prueba JPL; Apollo 11 TLI/LOI fechados |
| 4 | E4 viento AS-506 | Tabla 11-2 del FER |
| 5 | E3 gravedad lunar + terrestre | SP-238 evolución orbital a 126 h |
| 6 | Cierre de F6 (lista de la sección 6) | Residuos documentados dentro de bandas fuente |
| 7 | Encender por defecto el entorno fechado | CI completa verde con el entorno nuevo |
| 8 | G1, F7–F10 | Una fase por vez, con sus compuertas |
| 9 | Visual | Ver sección 8 |

## Fuentes consultadas para este plan

- [JPL: DE440 y DE441](https://ssd.jpl.nasa.gov/doc/de440_de441.html) y
  [NAIF de440_and_de441.pdf](https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/de440_and_de441.pdf);
  [exportación de efemérides JPL](https://ssd.jpl.nasa.gov/planets/eph_export.html);
  [Skyfield: tamaños y rangos](https://rhodesmill.org/skyfield/planets.html);
  Park et al. 2021, [AJ 161, 105](https://ui.adsabs.harvard.edu/abs/2021AJ....161..105P).
- [GRGM1200A (GSFC PGDA)](https://pgda.gsfc.nasa.gov/products/50) y su
  [etiqueta PDS](https://pds-geosciences.wustl.edu/grail/grail-l-lgrs-5-rdr-v1/grail_1001/shadr/gggrx_1200b_sha.xml).
- [NOAA IGRA](https://www.ncei.noaa.gov/products/weather-balloon/integrated-global-radiosonde-archive),
  [descripción IGRA v2](https://www.ncei.noaa.gov/pub/data/igra/igra2-product-description.pdf).
- [EGM2008](https://en.wikipedia.org/wiki/Earth_Gravitational_Model) (archivo NGA
  `EGM2008_to2190_TideFree`), [HWM14 (CCMC)](https://ccmc.gsfc.nasa.gov/models/HWM14~2014),
  [pymsis / NRLMSIS 2.0](https://github.com/SWxTREC/pymsis),
  [formato de clima espacial CelesTrak](https://celestrak.org/SpaceData/SpaceWx-format.php).
- [IERS EOP C04](https://hpiers.obspm.fr/iers/eop/eopc04/readme), [IAU SOFA](https://iausofa.org/current-software).
- MPR-SAT-FE-69-9 (FER del AS-506), apéndice A y tabla 11-2,
  [copia en archive.org](https://archive.org/download/nasa_techdoc_19900066485/19900066485.pdf);
  NASA SP-238 (Apollo 11 Mission Report), sección 7 y apéndice B.
