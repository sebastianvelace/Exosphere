# Plan maestro de realismo físico y percepción de astronauta

**Fecha:** 2026-07-09 · **Estado:** rector activo  
**Alcance:** simulación, datos, control, render, cámara IVA, VFX y validación

## Veredicto honesto

Exosphere ya posee una base seria: estado inercial en doble precisión, gravedad de
varios cuerpos, RK4, masa y centro de masa variables, empuje dependiente de presión,
atmósfera rotante, drag/lift por actitud, escala angular planetaria y floating origin.

Todavía no es una simulación «totalmente realista». Los bloqueos restantes no son
detalles cosméticos: marcos planetarios inconsistentes, actuadores 6-DoF agregados,
atmósferas planetarias aproximadas, terreno esférico liso y ausencia de fallo estructural
real. Este documento
separa lo validado de lo aproximado y define evidencia para cerrar cada brecha.

## Implementado en esta ola

- Escudo visual/térmico en la misma cara local `-X`; EDL presenta esas losetas al flujo.
- Momento aerodinámico desde drag, centro de presión, q e inercia, no tasa angular fija.
- Pitch/yaw/roll mezclados sobre los ejes físicos de una nave cuyo eje largo es `+Y`.
- Gimbal acoplado a torque y vector de empuje lateral.
- Piloto de ascenso por torque/gimbal, sin sobrescribir quaternion durante el vuelo.
- Cuatro flaps visuales gobernadas por q, actitud windward y mezcla de control.
- Cuatro flaps físicas con momento proporcional a q, área, brazo e inercia; entrada lift-up a 70° AoA.
- EDL manda actuadores: flip finito, encendido al mínimo estable y selección discreta de 1/2/3 Raptors.
- Rails prohibidos con thrust, atmósfera, calentamiento o contacto; periapsis atmosférico limita el warp.
- Ascent y ejecutores de maniobra mandan torque; los setters de actitud quedan solo en inicialización/debug.
- Plumas, flaps y carbonizado dependen del cuerpo dominante y presión real.
- Sutton-Graves incluye radio de nariz `sqrt(rho/Rn)·V³`; daño TPS irreversible.
- Aceleración propia unificada: ~1 g apoyado, ~0 g en caída libre, thrust+aero en vuelo.
- Ojo IVA en el mismo deck antes/después de staging (`36u → 10.64u`).
- Movimiento IVA limitado a centímetros, fuerza propia y FOV fijo.
- Un solo campo estelar y un solo Sol físico a escala angular.
- Luz/terminador siguen el Sol simulado; eclipse usa discos finitos y penumbra continua.
- Cuerpos sin atmósfera muestran cielo negro desde la superficie.
- Propagación Sol→planeta→luna jerárquica, independiente del orden de JSON.
- Etapa separada hereda posición, velocidad, actitud y velocidad angular.
- Touchdown usa seis pies con resorte, amortiguador, fricción, torque y límites de
  recorrido/carga; aterrizar ya no congela posición, velocidad ni actitud.
- El pad se planta en las coordenadas geodésicas reales del sitio de lanzamiento, así que
  hereda el empuje de rotación que le corresponde a su latitud (ω·R·cos φ). El gravity turn
  persigue el este definido por el eje de giro, no por un eje inercial fijo.

## Hallazgos priorizados

| ID | Brecha | Estado | Evidencia / aceptación |
|---|---|---|---|
| RF-01 | Marco eclíptico `+Z` vs rotación planetaria `+Y` | **Cerrado V1** | Pad en lat/lon reales de `data/launch_sites`; Kennedy aporta `407,9 m/s` al este (antes 184,8 desde `+Y`, latitud efectiva 66,6°). Eje único `RotationAxis` deriva superficie/latitud/este; 13 tests. Falta unificar el marco orbital heliocéntrico con la eclíptica |
| RF-02 | Propagación lunar dependía del orden de archivos | **Cerrado** | Test invierte Sol/planeta/luna y exige estados idénticos |
| RF-03 | Gimbal no alteraba fuerza traslacional | **Parcial** | Acoplado en cluster; faltan motores individuales y tensor completo |
| RF-04 | EDL/maniobras escriben orientación | **Cerrado V1** | Ningún controlador runtime asigna `Orientation`; corredor sembrado mide flip 15,33 s |
| RF-05 | Resultado cambia con warp | **Parcial** | Fuerzas/thermal/contacto comparten ruta; paridad corta x1/x100 cubierta, falta golden x1000 |
| RF-06 | Planetas comparten supuestos de gas/gravedad | **P0 pendiente** | Tablas por planeta; Earth P/rho/T <5% de error |
| RF-07 | TPS de un nodo/área fija | **P0 parcial** | Rn/daño corregidos; falta tile+estructura, conducción y ablación |
| RF-08 | Cluster agregado permite throttle EDL imposible | **Cerrado V1** | Selección 0–6 proporcional; EDL 1/2/3 + mínimo 40%; falta relight probabilístico |
| RF-09 | Hot staging atómico y sin impulso | **P1 pendiente** | Ship encendida ≥0.5 s antes; momento conservado |
| RF-10 | Cargas estructurales no rompen | **P1 pendiente** | Joint sobre límite divide el grafo |
| RF-11 | Colisión punto-esfera | **Cerrado V1** | Seis patas, carga/fricción/torque y vuelco; faltan DEM, pendiente y casco |
| RF-12 | Rails sin J2/drag/SRP; escape radial incorrecto | **P1 pendiente** | Escape radial válido y precesión J2 <1%/día |
| AV-01 | Cabina fuera de Starship al separar | **Cerrado** | Ojo relativo a base cambia <0.1u |
| AV-02 | Dos soles y luz fija | **Cerrado** | Sol ~0.53° cerca de Tierra y penumbra continua |
| AV-03 | Cielo terrestre en cuerpos sin aire | **Parcial** | Airless negro; faltan perfiles Venus/gigantes y `local_up` |
| AV-04 | Dos campos estelares/adaptación fija | **Parcial** | Un panorama; falta autoexposición temporal |
| AV-05 | Shake IVA de metros/FOV por g | **Cerrado V1** | <4.2 cm, vibración <1.7 cm, rotación <0.23°, FOV fijo |
| AV-06 | Cabina abierta y VFX atraviesan casco | **P1 pendiente** | Ventanas, vidrio y capas de render |
| AV-07 | Plasma cambia iluminación solar global | **P1 pendiente** | Luz local del shock, Sol estable, ruido continuo |
| AV-08 | Max-Q toroidal sin Mach/humedad | **P1 pendiente** | Condensación física o desactivada |

## Arquitectura física objetivo

### Marco planetario único

- Inercial: `+Z` normal a eclíptica, `+X` hacia referencia de época.
- Cuerpo fijo: quaternion `body→inertial(t)` con tilt y rotación sidérea.
- Sitio: latitud/longitud/altitud desde `data/launch_sites`.
- Atmósfera: `Vbody + omega×r + viento`; airspeed respecto a esa masa de aire.
- Render usa el mismo quaternion, nunca un tilt compartido entre planetas.

### Newton–Euler 6-DoF

```text
m·dv/dt = Σ(Fgravedad + Fmotor + Faero + Fcontacto)
I·dω/dt + ω×(I·ω) = Σ(torque motor + RCS + flaps + aero + contacto)
```

- Tensor corporal y masa/inercia actualizados con propelente.
- Cada motor produce fuerza en su posición y dirección gimballed.
- SAS/autopilotos mandan actuadores limitados; nunca teletransportan actitud.

### Atmósfera y aerotermodinámica

- Tierra: U.S. Standard Atmosphere 1976 hasta 86 km y tabla termosférica.
- Marte/Venus: masa molar, gas específico, gravedad variable y tablas propias.
- Aero: coeficientes por Mach, Reynolds, Knudsen, AoA y flap.
- Térmica: Sutton-Graves por zona/radio local, TPS+estructura, conducción,
  radiación, espesor y daño/ablación irreversible.

### Eventos conservativos

- Hot-stage: ignition Ship → overlap → release → separación.
- Impulso igual/opuesto; conservación de momento lineal/angular.
- Fallo de joint crea cuerpos nuevos; contacto usa forma, patas, fricción y terreno.

## Contrato visual «como astronauta»

1. Sin FOV por g, rayas de aire limpio, soles duplicados o estrellas sobre planetas.
2. Planeta, limb, Sol y horizonte se derivan de magnitudes angulares `R/d`.
3. Exposición tiene memoria: Tierra/Sol ocultan estrellas; eclipse las recupera gradual.
4. IVA siente aceleración propia y vibración estructural, no gravedad orbital.
5. Plasma/plume/frost/condensación/charring nacen de variables físicas y respetan casco.
6. Cada cuerpo usa su atmósfera, orientación, rotación y luz.

## Plan de ejecución

### Ola A — invariantes P0

1. Marco eclíptico/body-fixed + launch sites.
2. Prohibir rails con thrust, contacto, atmósfera o heat; parity de warp.
3. Extraer controladores de actitud a sim pura y retirar setters directos.
4. Motor individual/cluster discreto y EDL con mínimo throttle real.

### Ola B — entorno físico

1. Tablas atmosféricas terrestres y CO2 planetario.
2. Aero sweeps por Mach-AoA y momentos de flap.
3. TPS de dos nodos y zonas de exposición.
4. J2/SRP/drag secular para propagación larga.

### Ola C — eventos/estructura

Hot-stage conservativo, fallo estructural, contacto geométrico y luego boostback/catch.

### Ola D — percepción

Shader con `local_up`, perfiles ópticos por cuerpo, autoexposición/adaptación,
cabina cerrada/capas de VFX y plasma local.

## Gates de verificación

### Unitarios

- Proper acceleration: pad≈1g, freefall≈0g, burn de 1g≈1g.
- Engine transient: spool, min throttle, starvation, O/F y gimbal force+torque.
- Aero sweep: AoA×Mach, continuidad y direcciones.
- Thermal: invariancia de dt, Rn^-0.5, balance y daño irreversible.
- Body data contract, warp parity, stress, contacto y geometría sim↔render.

### Golden flight sin debug/fallback

| Hito | Banda inicial de aceptación |
|---|---|
| Max-Q real (máximo, no primer cruce) | 11–15 km, 28–38 kPa |
| Hot-stage | 62–68 km, 2.2–2.5 km/s, reserva 5–8% |
| Órbita | ~150 km, 7.6–7.8 km/s |
| Reentrada | peak-q 35–55 kPa; TPS nominal sobrevive |
| Flip | >2 s y actuadores dentro de límites |
| Touchdown | 1–3 m/s con configuración válida |

Estas bandas son hipótesis de calibración del proyecto hasta enlazarlas a telemetría
de vuelo concreta; no deben presentarse como mediciones oficiales.

### Matriz visual

Externa + IVA: pad, ignition, liftoff, Max-Q, separación, órbita diurna, eclipse,
entry, peak heating, flip y touchdown. Registrar altitud, FOV, radio angular,
dirección/visibilidad solar, q, Mach, aceleración propia, heat flux y motores.
El gate falla por `CRASHED`, `TIMEOUT`, `GAP` o `FALLBACK` y valida contenido, no
solo tamaño de PNG.

## Referencias primarias

- NASA, *U.S. Standard Atmosphere 1976*: https://ntrs.nasa.gov/archive/nasa/casi.ntrs.nasa.gov/19770009539.pdf
- NASA, Sutton & Graves, calentamiento convectivo: https://ntrs.nasa.gov/api/citations/19720003329/downloads/19720003329.pdf
- NASA NESC, adaptación visual: https://www.nasa.gov/centers-and-facilities/nesc/characterizing-the-visual-experience-of-astronauts-at-the-lunar-south-pole/
- NASA Science, Tierra/exposición/estrellas: https://science.nasa.gov/blogs/earth-matters/2011/09/28/where-are-the-stars/
- NASA/JSC, estrellas y airglow desde ISS: https://eol.jsc.nasa.gov/Collections/EarthObservatory/articles/Stargazing.htm

## Documentos relacionados

`docs/starship_physics_baseline.md`, `PHYSICS_DEEP_AUDIT.md`,
`VISUAL_DEEP_AUDIT.md`, `CROSSCUTTING_AUDIT.md`, `LANDING_CONTACT_REALISM.md` y
`docs/physics/hot_stage_overlap.md`.
