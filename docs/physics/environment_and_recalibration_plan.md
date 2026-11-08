# Plan: entorno fechado (tiempo, orientación, efemérides, gravedad, atmósfera y viento) y cierre de la recalibración histórica

Estado: plan revisado 2026-11-08 (versión 2), listo para ejecutar. Nada está implementado todavía.
Física primero, visual al final. Este plan reemplaza a la versión 1 del mismo día: incorpora
acceso verificado a las APIs y archivos de NASA/JPL, prototipos medidos y una arquitectura de
datos escalable.

## 0. Diagnóstico del código actual (verificado)

| Tema | Estado | Consecuencia |
| --- | --- | --- |
| Tiempo | `Universe.CurrentTime` = segundos desde J2000 sin escala declarada | Ninguna misión arranca en su fecha |
| Cuerpos | Elementos keplerianos fijos de dos cuerpos (`data/bodies/*.json`, `epoch` 0); el Sol fijo en el origen (`Universe.BodyStateAt`) | La Luna sin perturbación solar (nodo 18,6 años, apsis 8,85 años); geometría Tierra–Luna–Sol arbitraria |
| Orientación terrestre | `RotationAxis` fijo (eclíptica J2000 inclinada hacia +Y); meridiano cero = convención de la simulación en `t = 0` | El polo de 1969 está a 0,169° del de J2000 (medido con SPICE) y la precesión en longitud acumula ~0,42° en el ángulo sidéreo entre 1969 y 2000: sitios y planos fechados no pueden ser exactos |
| Luna | Mismo acimut de polo que todos los cuerpos, periodo sidéreo, sin libraciones | Sin marco de ejes principales no hay gravedad lunar real |
| Gravedad | Punto masa + J2 de primer orden en una base ecuatorial que no rota (`CelestialBody.GetGravityAt`) | Sin teserales terrestres ni mascons lunares |
| Atmósfera | Perfil USSA-76 fijo + cola termosférica | Sin variación diurna/solar; decaimiento LEO sin clima espacial |
| Viento | No existe; aire relativo `v − v_cuerpo − ω×r` calculado en ≥ 8 sitios (`Vessel.cs` ~537, 826, 936, 1042, 1080, 1108; `Universe.cs` ~2039; `GravityTurnGuidance`) | Sin cargas de viento ni deriva; no hay un punto único donde agregarlo |

## 1. Acceso a datos verificado (2026-11-08)

| Recurso | Acceso desde la sesión | Uso |
| --- | --- | --- |
| JPL Horizons API (`ssd.jpl.nasa.gov/api/horizons.api`) | Probado: Luna geocéntrica ICRF en 1969-07-16 13:32 UTC, `source: DE441` | Oráculo independiente para pruebas |
| NAIF generic kernels | Descargados y con SHA-256: `de440s.bsp` (32,7 MB), `earth_620120_260806.bpc` (12,3 MB, orientación terrestre desde 1962-01-20 con EOP), `moon_pa_de440_200625.bpc`, `moon_de440_250416.tf`, `naif0012.tls`, `pck00011.tpc`, `gm_de440.tpc` | Fuente de efemérides y orientación lunar; oráculo de la Tierra |
| SpiceyPy 8.3.0 (CSPICE N0067) y jplephem | Instalados con pip | Herramientas de generación offline |
| NASA NTRS API (`ntrs.nasa.gov/api/citations/search`) | Probado (38 resultados para AS-506) | Localizar y citar informes por ID |
| PDS Geosciences (GRAIL) | Probado: `gggrx_1200a_sha.tab`, encabezado R = 1738 km, GM = 4902,8001224453 km³/s², normalización 4π, filas `n, m, C, S, σC, σS`; el marco es el de ejes principales **DE430** según la etiqueta | Gravedad lunar |
| NOAA NCEI IGRA v2 | Probado: `USM00074794` CAPE KENNEDY (1950–2026) y `USM00072250` BROWNSVILLE/INT. (1929–2026); sondeo del 1969-07-16 14Z con 17 niveles de viento, superficie 180°/1 m/s = FER tabla A-I | Viento medido por fecha |
| FER AS-506 (MPR-SAT-FE-69-9, archive.org) | Texto extraído: apéndice A (viento), tabla 11-2 (parámetros de control S-IC) | Compuerta histórica de viento |

**Prototipos medidos** (ajuste de Chebyshev contra SPICE, 1962–2035, error máximo en 60 tramos
aleatorios × 40 puntos):

| Serie | Tramo / grado | Error máximo | Tamaño |
| --- | --- | --- | --- |
| Luna geocéntrica | 8 d / 16 | 0,7 mm | 1,38 MB |
| Baricentro Tierra–Luna | 16 d / 13 | 5,4 mm | 0,57 MB |
| Sol (baricéntrico) | 32 d / 11 | 25 mm | 0,24 MB |
| Orientación de la Luna (ejes principales DE440) | 8 d / 12 | 2 µm en superficie | 2,75 MB |
| Orientación terrestre compactada desde SPICE | cualquier tramo | ~35–40 m | — |

La orientación terrestre **no** se compacta bien desde SPICE: el movimiento del polo, fijo en el
marco terrestre, hace que el polo ITRF gire a diario alrededor del polo celeste y acopla los
ángulos (verificado con segundas diferencias). La Tierra se implementa con el modelo IERS
estándar y SPICE queda como oráculo.

## 2. Arquitectura escalable

### 2.1 Canal de datos (`tools/envdata/`, Python, versiones fijadas en `requirements.txt`)

- `data/environment/manifest.json`: una entrada por dataset externo con URL, versión, licencia,
  fecha de obtención y SHA-256 del archivo fuente; una entrada por producto generado con el
  comando que lo produce y el SHA-256 de su salida.
- `envdata fetch` descarga a una caché local (fuera de git) y verifica los hashes.
- `envdata build <producto>` genera binarios deterministas (little-endian, cabecera con versión,
  marco, escala de tiempo, intervalo, grado y hash de la fuente). Reconstruir debe dar el mismo
  SHA-256.
- `envdata provenance` escribe los registros de `data/provenance/` a partir del manifiesto, de
  modo que ningún dato llega sin fuente.
- `envdata fixtures` consulta Horizons y SPICE y guarda vectores de referencia en
  `ExosphereSimulation.Tests/Fixtures/environment/` con la consulta exacta y la fecha.
- Contrato de CI (sin red): cada producto en `data/environment/` coincide con el SHA-256 del
  manifiesto y tiene procedencia. La red solo se usa al regenerar.

**Escalabilidad:** agregar un cuerpo, una misión o un perfil de viento es agregar una entrada al
manifiesto y a la configuración JSON, sin código nuevo.

### 2.2 Servicios en la simulación (`ExosphereSimulation/Environment/`, sin Godot)

| Interfaz | Implementaciones | Configuración |
| --- | --- | --- |
| `ITimeScale` | `TimeScales` (UTC↔TAI↔TT↔TDB, UT1 desde EOP) | `data/environment/time/*.bin` |
| `IEphemerisProvider` | `ChebyshevEphemeris` (DE440), `KeplerEphemeris` (actual, respaldo y sandbox) | `ephemeris` en `data/bodies/*.json` |
| `IBodyOrientation` | `IersEarthOrientation`, `ChebyshevOrientation` (Luna), `IauRotationModel` (resto), `FixedAxisRotation` (actual) | `orientation` en el JSON del cuerpo |
| `IGravityField` | `PointMass`, `ZonalJ2` (actual, bit a bit), `SphericalHarmonics` (Pines) | `gravity: {model, file, max_degree}` |
| `IAtmosphere` | `LayeredAtmosphere` (actual), `NrlMsis2` | `atmosphere` en el JSON del cuerpo |
| `IWindField` | `NoWind`, `ProfileWind` (tabla por altitud), `Hwm14Wind`, `CompositeWind`, `DrydenGusts` | `wind` en la misión |

`EnvironmentContext` agrupa los proveedores y la época de la misión; `Universe` lo consulta a
través de un solo punto por magnitud: `BodyStateAt`, `BodyOrientationAt`, `GravityAt`,
`AirVelocityAt`, `AtmosphereAt`.

### 2.3 Reglas transversales

1. Interruptor por capacidad (`Universe.Environment.DatedEphemerides`, `.EarthOrientation`,
   `.SphericalGravity`, `.Wind`, `.DatedAtmosphere`), apagado hasta pasar paridad y compuertas.
2. Con todo apagado la suite completa queda **bit a bit igual** (compuerta de paridad).
3. `SaveGameV2`: campos opcionales para la época, la escala y los identificadores de dataset;
   un guardado viejo carga con el entorno viejo.
4. Rendimiento (medido en la suite y en Godot): estado de cuerpo < 1 µs, orientación < 1 µs,
   gravedad grado 50 < 15 µs, viento por perfil < 0,5 µs; caché por paso RK4.
5. Pruebas: analíticas en segundos; históricas largas en `tools/slow_tests.txt`.
6. Precisión doble: con origen en el baricentro del sistema solar, 1 AU tiene resolución
   ~3·10⁻⁵ m; el integrador de la nave sigue trabajando relativo a su cuerpo de referencia.

## 3. Unidades de trabajo (cada una es un PR verificable)

### E0 — Fundaciones (sin cambio de comportamiento)

- **E0.1 Aire relativo en un punto.** `CelestialBody.GetAirVelocity(position, time)` (hoy:
  rotación) y `Vessel.GetAirRelativeVelocity(body)`; reemplazar los ≥ 8 cálculos.
  *Salida:* suite completa idéntica; `ci_quick` + CI completa verdes.
  **Hecho (2026-11-08).** `CelestialBody.GetAirVelocity(position)` y
  `GetAirRelativeVelocity(position, velocity)`, `Vessel.GetAirRelativeVelocity(body)`. Usan el
  aire: arrastre, sustentación y paracaídas (`Vessel`), aerodinámica distribuida, flaps y momento
  aerodinámico del 6-DoF (`RigidBodyForceEvaluator`), presión dinámica, calentamiento y cargas
  térmicas, límites de warp y de costa residual (`Universe`), guiado de giro gravitatorio,
  trimado de Flight 14, guiado de cápsula, estado de interfaz de entrada, diagnósticos de entrada,
  y en la capa de juego plasma, ruptura, sonido, vibración, luces, estrellas, Max-Q y térmica del
  EDL. Siguen con la superficie: contacto, agua, aterrizaje, sujeción en el pad, lecturas de
  navegación y HUD. Huella de Flight 14 en 6000 pasos idéntica antes y después
  (`AirRelativeVelocityTests`). **Revisar al llegar el viento (E4):** los controladores de
  descenso/retorno de Flight 14, el EDL y `SasHold` usan una sola velocidad de superficie para la
  dirección del flujo y para la tasa vertical; con viento hay que separarlas.
- **E0.2 Canal `envdata`** con manifiesto, fetch, build determinista, provenance, fixtures y el
  contrato de CI sin red. *Salida:* reconstrucción reproduce el SHA-256; contrato verde.
  **Hecho (2026-11-08).** `tools/envdata/` (`envdata.py fetch|build|verify|provenance`, formato
  `envfile.py`, guía en `tools/envdata/README.md`), `data/environment/manifest.json` y la
  procedencia generada `data/provenance/environment_products.json`. Primer producto:
  `time/tai_utc.bin` desde USNO `tai-utc.dat` (incluye los términos de ritmo 1961–1971 que el
  núcleo NAIF no trae), cruzado en la construcción con NAIF `naif0012.tls`; TAI−UTC = 7,5635 s
  en el lanzamiento del Apollo 11. `tools/tests/envdata_test.py` corre en `contract_checks.sh`
  (verificación sin red, detección de producto alterado o no declarado). Los *fixtures* de
  Horizons/SPICE llegan con su primer consumidor (E1/E2).
- **E0.3 `EnvironmentContext`** con los proveedores actuales envueltos (Kepler, eje fijo, J2,
  USSA-76, sin viento). *Salida:* paridad bit a bit.
  **Hecho (2026-11-08).** `ExosphereSimulation/Environment/`: `IEphemerisProvider`
  (`KeplerEphemeris`, a nivel de `Universe.Environment`), `IBodyOrientation`
  (`FixedAxisRotation`, en `CelestialBody.Orientation`) e `IGravityField` (`ZonalJ2Gravity`, en
  `CelestialBody.Gravity`); `EnvironmentContext.Describe` nombra los proveedores para registros
  y guardados. Todo estado de cuerpo, eje, velocidad y ángulo de giro y toda gravedad pasan por
  ellos. Huella de Flight 14 idéntica y la misma asignación por paso; `EnvironmentContextTests`
  fija las fórmulas originales y la coherencia de la cadena Sol–Tierra–Luna. La atmósfera y el
  viento reciben su contrato en E4, donde se define el argumento de tiempo que necesitan.

### E1 — Tiempo y orientación terrestre

- **E1.1 Escalas de tiempo.** TT = TAI + 32,184 s; TAI−UTC de la tabla oficial, incluida la UTC
  con fracciones de 1961–1971; TDB−TT por la serie de Fairhead–Bretagnon truncada (< 10 µs);
  UT1 desde EOP 20 C04 (desde 1962). *Analítica:* ida y vuelta < 1 µs; contra `spiceypy.unitim`
  y `et2utc` en 100 instantes 1962–2035 (< 10 µs).
  **Hecho UTC/TAI/TT/TDB (2026-11-08).** `Environment/TimeScales` (segundos desde J2000 en cada
  escala, ~0,1 µs de resolución) y `Environment/EnvironmentProductFile` (lector del contenedor de
  `envdata`). **Corrección del plan:** SPICE usa un modelo TDB de un término (~30 µs del modelo
  completo), así que no sirve de oráculo a 10 µs; el oráculo es ERFA/SOFA (`eraUtctai`,
  `eraTaitt`, `eraDtdb` completo), con el fixture `ExosphereSimulation.Tests/Fixtures/environment/
  time_scales.json` de `tools/envdata/fixtures/time_scales.py` (113 instantes 1961–2035: lanzamientos
  de Freedom 7, Friendship 7, Gemini 8, Apollo 8 y 11, ambos lados de cada salto). Medido: TAI/TT
  0,24 µs, TDB 6,6 µs (serie truncada de la USNO Circular 179), ida y vuelta exacta, SPICE 30 µs
  (informativo). Falta UT1 (EOP 20 C04), que llega con E1.2.
- **E1.2 Orientación terrestre.** Precesión IAU 2006, nutación IAU 2000B, ERA(UT1), movimiento
  del polo (x_p, y_p) y s′; tabla EOP diaria ~0,5 MB. Antes de 1962-01-20 (Freedom 7): modelo
  sin EOP (UT1 ≈ UTC) con la incertidumbre declarada. *Analítica:* contra
  `pxform("ITRF93","J2000")` del kernel `earth_620120_260806.bpc` en 500 instantes: < 0,5 m en
  superficie (IAU 2000B ~1 mas + diferencia ITRF93/ITRF actual). *Histórica:* posición inercial
  de LC-39A en el lanzamiento de Apollo 11 contra Horizons (sitio topocéntrico) < 1 m.
  **Hecho el modelo (2026-11-08); falta conectarlo a `CelestialBody` (E1.2b).**
  `Environment/IersEarthOrientation`: cadena CIO de las IERS Conventions 2010 con X, Y, s del
  IAU 2006/2000A completo tabulados a diario por ERFA (`earth/cip_xys.bin`, 1960–2051, Lagrange
  de 8 puntos) más dX, dY observados, ERA(UT1), movimiento del polo y s′; EOP 20 C04
  (`earth/eop_c04.bin`, 1962-01-01 a 2026-06-30, Lagrange de 4 puntos, UT1 como UT1−TAI) con la σ
  publicada de cada época. El canal admite fuentes vivas fijadas por prefijo (`prefix_through`).
  Medido en 128 instantes 1962–2026: **0,21 mm** contra ERFA con la misma cadena; contra SPICE
  `pxform` (`earth_620120_260806.bpc`) mediana 0,19 m, ≤ 0,27 m desde 1990 y 3,16 m en 1962
  (94 % de 3σ publicado). **Correcciones del plan:** (1) el núcleo de alta precisión de NAIF
  refiere ITRF93 al ICRF aunque el marco se llame J2000, así que no lleva sesgo de marco
  (con `eraBp06` la mediana sube a 0,76 m); (2) el UTC de SPICE asume TAI−UTC = 9 s antes de 1972
  (7 s de error en 1962): para misiones antiguas el tiempo de SPICE se deriva de ERFA; (3) la
  compuerta de < 0,5 m vale desde 1995; antes, la cota es la σ publicada del C04 (≥ 1,5 m).
  **E1.2b hecho (2026-11-08).** `IDatedBodyOrientation` + `IersOrientationProvider`
  (`Universe.UseIersEarthOrientation`, apagado por defecto hasta E5): el marco fijo de la Tierra es
  el ITRS de la fecha, la longitud es la de Greenwich y el tiempo de la simulación se lee como TDB.
  `CelestialBody.StateTime` (lo escribe la efeméride) fija el instante de las consultas sin tiempo;
  las consultas `*AtTime` usan la rotación de su propio instante (en caché por instante). El eje de
  giro es el CIP y la velocidad angular la del ERA corregida por LOD. Compuerta histórica E1
  (`DatedEarthOrientationTests`): LC-39A en el lanzamiento del Apollo 11 a 2,17 m de SPICE (3σ de
  1969: 3,36 m), LC-14 en Friendship 7 a 2,06 m, Starbase en Flight 4 a 0,13 m, velocidad del pad a
  0,15 mm/s. El eje fijo queda bit a bit (huella de Flight 14 idéntica). **Pendiente para E5:** el
  render de la Tierra (`FloatingOrigin` usa ω·t sobre el eje fijo) debe leer la rotación fechada.
- **E1.3 Época por misión.** `MissionDefinition.LaunchEpochUtc` y conversión única a TDB;
  Apollo 11: 1969-07-16 13:32:00 UTC (SP-238). Sitios en ITRF desde `data/launch_sites` con
  datum declarado. *Salida:* con el interruptor apagado, nada cambia.
  **Hecho (2026-11-08).** `MissionDefinition.LaunchEpochUtc` + `LaunchEpochSourceId` +
  `LaunchEpochEvidence` (validados: ISO-8601 en UTC, en la fecha histórica, citando una fuente de
  la propia misión) y `LaunchEpochTdbSeconds(TimeScales)`. Épocas verificadas en documentos:
  Freedom 7 14:34:13 (SP-4012 v. II), Friendship 7 14:47:39 (informe MA-6), Gemini 8 16:41:02
  (página NASA con UT explícita y cadena de eventos de SP-4012; el informe MSC-G-R-66-4 y la
  cronología SP-4002 imprimen una hora antes con «e.s.t.», contradicho por ambas y documentado),
  Apollo 8 12:51:00 (FER AS-503, range zero) y Apollo 11 13:32:00 (SP-238, range zero). Los
  sitios ya están en coordenadas geodésicas WGS84 sobre el ITRS de la fecha cuando la orientación
  IERS está activa. Nada cambia con el entorno fechado apagado.

### E2 — Efemérides fechadas

- **E2.1 Producto `de440_1957_2035`.** Sol, Mercurio a Saturno (baricentros), BTL y Luna
  geocéntrica, con los tramos y grados de la tabla de prototipos (la Luna de 8 d / 16), marco
  ICRF/J2000 y TDB. Tamaño medido < 4 MB.
- **E2.2 `ChebyshevEphemeris`.** Posición y velocidad (derivada analítica), sin asignaciones;
  Tierra = BTL − Luna·μ_L/(μ_T + μ_L) con los GM de `gm_de440.tpc`; origen en el baricentro.
- **E2.3 Marco de trabajo.** La simulación pasa de eclíptica J2000 a ICRF (rotación fija por
  ε₀ = 84381,406″ en el cambio; sesgo de marco ~23 mas documentado). Se adapta
  `OrbitalElements` y los datos que dependen del marco, con pruebas de equivalencia.
- **E2.4 Integración.** `BodyStateAt` y SOI usan la efeméride; Kepler queda como respaldo fuera
  del rango y para el sandbox.
- *Analítica:* contra los vectores de prueba de JPL y contra fixtures de SPICE (< 1 mm Luna);
  continuidad en los bordes de tramo. *Histórica:* geometría de la TLI y hora de LOI de Apollo 11
  (75:49:50,4 GET, SP-238 tabla 7-II) con guiado fechado; mismo caso para Apollo 8.

### E3 — Gravedad

- **E3.1 Orientación lunar** desde `moon_pa_de440_200625.bpc` (8 d / 12, 2 µm). GRGM1200A está
  en el marco de ejes principales DE430: la rotación DE430→DE440 se toma de NAIF y su efecto
  se declara (< 1″ esperado; medirlo en el build).
- **E3.2 `SphericalHarmonics`** (recursión de Pines, coeficientes normalizados 4π, grado
  configurable). *Analítica:* contra evaluación directa de Legendre en Python en 200 puntos
  (error relativo < 1e-12); potencial y gradiente consistentes; `ZonalJ2` reproduce el modelo
  actual bit a bit; precesión nodal de J2 contra la fórmula.
- **E3.3 Datos.** Luna: GRGM1200A truncado (grado a elegir por error de aceleración a 100 km
  entre 50, 70 y 100). Tierra: EGM2008 tide-free hasta grado 20 (LEO) y 8 (rápido).
- *Histórica lunar (SP-238 sección 7):* desde 53,8 × 65,7 mi náuticas tras la circularización
  (80:12 GET), la órbita a las 126 h fue 56,6 × 62,4; el modelo R2 de la época predecía
  59,3 × 59,9. Compuerta: el modelo nuevo se acerca a lo observado más que R2, con banda fijada
  antes de correr desde la incertidumbre del estado inicial. Apollo 8 como segundo caso.
- *Histórica terrestre:* evolución de una órbita de estacionamiento publicada (Gemini/Apollo).

### E4 — Atmósfera y viento

- **E4.1 `ProfileWind`.** Perfil por misión (`data/winds/<misión>.json`): altitud, velocidad,
  dirección, con fuente por tramo. AS-506: estructura fina digitalizada del FER (figuras A-1 a
  A-4, pico 9,6 m/s a 11,40 km; superficie 3,3 m/s del sur a 18,3 m) y control cruzado con IGRA
  14Z (17 niveles). Herramienta de digitalización con la incertidumbre de lectura registrada.
- **E4.2 `Hwm14Wind`** por encima del perfil medido, con mezcla suave.
- **E4.3 `NrlMsis2`** con F10.7/Ap de CelesTrak `SW-All.txt` por fecha; presión de motores y
  densidad aerodinámica del mismo estado.
- **E4.4 `DrydenGusts`** (MIL-HDBK-1797) con semilla guardada.
- *Analíticas:* viento uniforme ⇒ deriva con la constante de tiempo de arrastre; viento cero ⇒
  paridad exacta; NRLMSIS contra sus vectores de prueba; espectro de Dryden.
- *Histórica (FER AS-506 tabla 11-2):* ángulo de ataque máximo 1,82° en cabeceo (T+117,1 s) y
  1,42° en guiñado (T+73,0 s); Qα máximo 4,93 deg·N/cm² (T+91,4 s); gimbal medio 0,23°/−0,44°;
  aceleración normal −0,331/0,306 m/s². Bandas desde la incertidumbre del perfil y del
  coeficiente de fuerza normal, fijadas antes de correr.

### E5 — Encender el entorno fechado por defecto

Recalibrar los perfiles históricos con el entorno nuevo **sin aflojar ninguna tolerancia**;
los que fallen se investigan como hallazgos físicos. CI completa verde y Godot verificado.

## 4. Cierre de F6 (recalibración histórica)

Orden por impacto físico, cada uno con fuente primaria y compuerta:

1. **Inserción del AS-506** (+10 s, −6,7 t de S-IVB, −7,6 t de vehículo frente al FER): flujo
   y mezcla del F-1 en altura (el par empuje/Isp de vacío implica 2712 kg/s contra 2616 a nivel
   del mar), PU del J-2 y época real (E1/E2). Fuente: FER tablas 5-I y 20-x.
2. **Tanques del SPS:** cuatro tanques fuera del eje, drenaje almacenamiento→sumidero, residuos
   no expulsables y criogénicos (SP-238 tablas 8-X a 8-XII; CSM Data Book vía NTRS). Cierra el
   +6 % de giración del CSM en el TD&E.
3. **Salida de tobera del SPS** para CSM-107 (buscar CSM Data Book/AOH 1969 en NTRS).
4. **MA-6 Atlas nativo:** sustainer, verniers y PU desde el ledger previo al vuelo
   (`atlas_ma6_reconstruction.md`).
5. **Aerodinámica:** aletas del S-IC (CP/Cmα de túnel de viento NASA), grid fins, Newton
   modificado para cápsulas romas con sus datos de túnel.
6. **Estaciones S-II/S-IVB** desde FER o manuales.
7. **AS-503** con geometría de tanques para que F5 aplique a Apollo 8.
8. **Gemini 8** con el Agena en su órbita fechada (requiere E1/E2).
9. **Flight 14:** marco de la velocidad de transmisión, desajuste hipersónico tardío, océano.

## 5. Física posterior (cada una como fase con compuertas)

| Fase | Contenido | Fuente o compuerta guía |
| --- | --- | --- |
| G1 Guiado | IGM iterativa para S-II/S-IVB y la TLI (hoy prograda) | Ecuaciones IGM de MSFC; primera MCC de Apollo ~6 m/s |
| F7 Flexión | Modos de viga acoplados al TVC y sensores | El FER del AS-506 dice efecto despreciable en Saturn V: priorizar Starship |
| F8 Térmica | Ablación, recesión, conducción interna, apagón de plasma | Apollo 4/6, FIRE II |
| F9 Motores | Transitorios, actuador de gimbal, deriva de mezcla, fallas | FER (arranque F-1/J-2) |
| F10 Slosh | Modos superiores y baja gravedad | SP-106 cap. 11 |

## 6. Visual (después de la física)

1. Composición atmósfera–superficie (Tierra escalada tapando el casco, bruma opaca, capa cian fija).
2. Comparación fotográfica lado a lado: hot-staging, encendido, pluma de vacío, reentrada, grid fins, casco, torre.
3. Nubes y clima coherentes con el viento y la fecha de E4.
4. Reentrada (choque y plasma), agua y salpicadura, captura en torre.
5. Capturas automáticas en CI con heurísticas.

## 7. Secuencia y estimación

| Paso | Unidades | Bloquea a |
| --- | --- | --- |
| 1 | E0.1, E0.2, E0.3 | todo lo demás |
| 2 | E1.1 → E1.2 → E1.3 | E2, E3, E4.3 |
| 3 | E2.1 → E2.4 | E3.1, F6.8 |
| 4 | E4.1 (AS-506) → E4.2 → E4.3 → E4.4 | E5 |
| 5 | E3.1 → E3.3 | E5 |
| 6 | E5 | — |
| 7 | F6.1–F6.9 (F6.2–F6.7 pueden ir en paralelo con E1–E4) | — |
| 8 | G1, F7–F10 | — |
| 9 | Visual | — |

## Fuentes

- Efemérides: [JPL DE440/DE441](https://ssd.jpl.nasa.gov/doc/de440_de441.html),
  [NAIF de440_and_de441.pdf](https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/de440_and_de441.pdf),
  [NAIF PCK](https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/),
  [Horizons API](https://ssd.jpl.nasa.gov/api/horizons.api),
  Park et al. 2021, [AJ 161, 105](https://ui.adsabs.harvard.edu/abs/2021AJ....161..105P).
- Orientación y tiempo: [IERS EOP C04](https://hpiers.obspm.fr/iers/eop/eopc04/readme),
  IERS Conventions 2010 (TN36), [IAU SOFA](https://iausofa.org/current-software).
- Gravedad: [GRGM1200A](https://pgda.gsfc.nasa.gov/products/50) y su
  [etiqueta PDS](https://pds-geosciences.wustl.edu/grail/grail-l-lgrs-5-rdr-v1/grail_1001/shadr/gggrx_1200a_sha.xml);
  EGM2008 (NGA, `EGM2008_to2190_TideFree`).
- Atmósfera y viento: [NOAA IGRA v2](https://www.ncei.noaa.gov/products/weather-balloon/integrated-global-radiosonde-archive),
  [HWM14](https://ccmc.gsfc.nasa.gov/models/HWM14~2014), [pymsis / NRLMSIS 2.0](https://github.com/SWxTREC/pymsis),
  [CelesTrak clima espacial](https://celestrak.org/SpaceData/SpaceWx-format.php).
- Vuelo: MPR-SAT-FE-69-9 (FER AS-506, [archive.org](https://archive.org/download/nasa_techdoc_19900066485/19900066485.pdf)),
  NASA SP-238 (Apollo 11 Mission Report), [NTRS API](https://ntrs.nasa.gov/api/citations/search).
