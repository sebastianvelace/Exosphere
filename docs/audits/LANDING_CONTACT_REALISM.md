# Contacto de aterrizaje y percepción del astronauta

**Fecha:** 2026-07-10  
**Rama:** `feat/landing-contact-realism`  
**Alcance:** touchdown de Starship, integración 6-DoF, visual de patas y gates EDL

## Veredicto

El aterrizaje dejó de ser una comparación de velocidad seguida de un snap al suelo.
Cada uno de seis pies muestrea la superficie rotante, genera fuerza normal y fricción,
aplica su momento respecto al centro de masa y puede exceder recorrido o carga. El estado
`LANDED` solo aparece después de que la nave se sostenga físicamente durante 0,50 s.

Es un modelo de ingeniería V1, no una reproducción certificada de hardware de SpaceX.
La geometría y los coeficientes de `starship_landing_gear.json` describen deliberadamente
una variante experimental del simulador; no se presentan como datos de Flight 7. El suelo
sigue siendo la esfera de radio medio del cuerpo, sin DEM, pendiente, deformación, roca,
hundimiento ni interacción pluma-regolito.

## Auditoría del estado anterior

Antes de esta ola, `Universe.HandleSurfaceImpact` trataba la nave como un punto:

- si el datum cruzaba la esfera y la rapidez era menor al umbral, trasladaba el datum a
  `R + 1 m` y anulaba la velocidad relativa al suelo;
- el controlador EDL declaraba touchdown por altitud/velocidad, activaba `IsGroundHeld`,
  anulaba la velocidad angular y fijaba orientación y posición;
- `landing_leg.json` contenía resorte/amortiguador, pero el contrato de partes no los
  deserializaba ni existía un solver que los consumiera;
- no había huella, distribución de carga, fricción lateral, torque de contacto, compresión
  visible ni criterio persistente de asentamiento.

Eso producía una imagen quieta, pero un astronauta no habría sentido el impulso de las
patas, la oscilación amortiguada ni el momento de un contacto asimétrico.

## Modelo implementado

Para cada pie `i`, el punto y su velocidad se calculan en mundo:

```text
pᵢ = p_datum + R(q) rᵢ,datum
vᵢ = v_COM + ω × (pᵢ - p_COM)
δᵢ = max(0, radio_pie - distancia_firmada_superficie)
Fₙ,ᵢ = n · max(0, k δᵢ - c vₙ,ᵢ)
|Fₜ,ᵢ| ≤ μ |Fₙ,ᵢ|
τᵢ = (pᵢ - p_COM) × (Fₙ,ᵢ + Fₜ,ᵢ)
```

La fuerza total se reevalúa en los cuatro estados internos del RK4 traslacional y entra
también en las cargas estructurales y la aceleración
propia. El torque entra en la integración angular después de la autoridad limitada de los
actuadores: un impacto ya no queda escondido por el rate clamp del piloto/SAS. La velocidad
de la superficie incluye traslación y rotación del cuerpo.

El paso se acota a 5 ms al desplegar tren por debajo de 100 m. La fuerza de penalización
no se limita artificialmente: el sistema reporta sobrecarga, sobre-recorrido y exceso de
carrera. Como V1 aún no modela la rigidez no lineal del bump-stop y su carga transferida a
estructura primaria, bottom-out es diagnóstico y la carga última decide la pérdida.

### Parámetros V1 de la variante experimental

| Parámetro | Valor |
|---|---:|
| Pies | 6 |
| Radio de huella | 4,20 m |
| Radio de contacto de cada pie | 0,35 m |
| Rigidez por pie | 2,35 MN/m |
| Amortiguamiento por pie | 0,55 MN·s/m |
| Recorrido máximo | 0,60 m |
| Carga última por pie | 2,50 MN |
| Fricción estática / dinámica declarada | 0,60 / 0,45 |
| Masa agregada del sistema | 3.000 kg |

El límite de 2,50 MN equivale aproximadamente a 6,3 veces la carga estática por pie de
una nave de 244 t en Tierra. No procede de una especificación pública de Starship: es una
hipótesis de carga última calibrada con 24 % de margen sobre el peor pico dinámico medido
del corredor (2,015 MN). El ensayo destructivo usa −7,0 m/s, cuya energía excede
claramente el recorrido disponible; −4,5 m/s resultó absorbible y no se fuerza a fallar.

## Criterio de aterrizaje

La misión no usa altitud como interruptor. Se exige simultáneamente:

- al menos tres contactos geométricos;
- velocidad normal < 0,25 m/s y tangencial < 0,50 m/s;
- velocidad angular < 0,03 rad/s;
- eje largo a menos de 10° de la vertical local;
- reacción normal entre 0,75 y 1,25 veces el peso local;
- persistencia durante 0,50 s.

Al primer contacto en descenso final se deselecciona el Raptor encendido, se ordena shutdown
y se neutralizan gimbal/actuadores; el spool interno puede decaer sin producir empuje y las
patas absorben el resto de la energía. No se activa
el hold-down de lanzamiento ni se escriben posición, velocidad,
orientación o velocidad angular para fabricar el resultado.

## Simulación y render comparten estado

`VesselRenderer` construye seis patas telescópicas en la misma huella y longitud declaradas
por datos. Despliegue proviene de `Part.IsDeployed`; la compresión visible proviene de la
penetración real de cada `ContactPointResult`. El solver distingue explícitamente el datum
visual del brazo respecto al COM, porque el renderer histórico de Starship todavía no usa
el origen del grafo de partes como datum único.

## Validación y diagnóstico

- Ocho pruebas puras cubren ausencia de contacto, ley resorte-amortiguador, no adhesión,
  límite Coulomb, `ω×r`, simetría de seis patas, fallo por recorrido/carga y suelo rotante.
- Integración cubre asentamiento de una nave de 244 t a −1,5 m/s con 0,8 m/s lateral,
  el estado medido del corredor EDL (−1,8 / 1,5 m/s) y fallo severo a −7,0 m/s.
- El harness registra contactos, compresión máxima, carga pico, despliegue, sobrecarga,
  sobre-recorrido y settlement. Touchdown solo es válido con ≥3 contactos y `settled=True`.
- Dos golden completos revelaron límites de 1,80 y 2,00 MN demasiado ajustados: la
  dispersión del controlador produjo 0,491–0,539 m de carrera y picos de 1,820–2,015 MN.
  Los gates fallaron correctamente. La carga última se separó del recorrido operativo,
  se dejó margen explícito, se añadió regresión y se repitió la trayectoria.
- Un tercer golden agotó 0,609 m pese a no sobrecargar el pie. El diagnóstico mostró que el
  último comando pitch/yaw seguía activo durante el spool-down: se neutralizó el actuador al
  contacto sin anular la velocidad angular ni el torque externo del suelo.
- El perfil anterior pedía 1,5 m/s a altitud datum cero, aunque el pie toca a 7,85 m. El
  setpoint final ahora se referencia al plano geométrico de contacto y pide 1,2 m/s allí.
- La corrección lateral inclinaba la nave hasta tocar y gastaba recorrido en el pie bajo.
  Una flare desde 30 m mezcla progresivamente el eje de empuje a la vertical de contacto.
- Con la actitud corregida apareció un segundo impacto subamortiguado. Para 244 t, los seis
  dampers pasan de 2,4 a 3,3 MN·s/m total (ζ≈0,89 frente a ~3,7 MN·s/m crítico), sin ampliar
  los 0,60 m de recorrido.
- La política inicial destruía por cualquier exceso de carrera. Golden sucesivos llegaron
  a 0,602–0,700 m sin superar 2,50 MN. El exceso ahora se registra por separado y la carga
  última gobierna el fallo, evitando que una tolerancia geométrica inventada equivalga a
  pérdida estructural total. Un bump-stop no lineal queda explícitamente pendiente.
- El Raptor agregado usaba 0,5 s tanto para startup como para shutdown y reimpulsaba la nave
  después del contacto. El apagado ahora cae a 0 en ~0,2 s (5/s), conservando ~0,5 s (2/s)
  para encendido; una prueba fija ambas pendientes.
- La primera integración congelaba la fuerza de contacto durante cada paso de 5 ms. Como
  `F(δ,v)` cambia dentro del paso, ahora cada etapa RK4 vuelve a muestrear resorte/damper;
  esto evita inyectar energía numérica en compresión y rebote.
- Cuando tres o más pies comparten carga, las articulaciones introducen damping rotacional
  pasivo de 2,5/s. No fija actitud ni velocidad: solo disipa el giro residual que, sin un
  modelo flexible de puntales, haría que una única pata tomara todo el peso. La V1 usa 8/s;
  calibrar este coeficiente contra drop-tests queda pendiente.
- Un golden confirmó contacto inicial sano (0,147 m; 0,632 MN), pero el Raptor seguía
  seleccionado al 93 % durante spool-down y relanzaba la nave. El cutoff ahora selecciona
  cero motores inmediatamente; bajar solo el throttle no equivalía a cerrar el motor.
- El cutoff queda enclavado desde el primer contacto. Sin ese latch, un rebote que separaba
  los pies durante un frame rearmaba el Raptor y creaba un ciclo encendido-contacto-apagado.
- Un primer `LANDED` visual ocurrió en un cruce de velocidad cero con ~5,9 g elásticos. El
  settlement ahora exige además reacción normal cercana al peso durante toda la persistencia.
- El damper tangencial inicial (`0,25·c`) frenaba con un brazo COM de 28,7 m y trasladaba
  lentamente toda la carga a una pata. Se redujo a `0,05·c`; Coulomb sigue limitando la fuerza.

## Referencias primarias y decisiones

- NASA TN D-2027 estudia experimentalmente configuraciones lunares de múltiples patas y
  respalda tratar geometría, estabilidad y reparto de contactos como variables del vehículo:
  https://ntrs.nasa.gov/citations/19640005067
- El trabajo de dinámica de aterrizaje del LM documenta que la estabilidad al vuelco y la
  adecuación del tren se validaban con dinámica de touchdown, no con un simple umbral:
  https://ntrs.nasa.gov/citations/19720018253
- NASA TN D-7301 incluye masa no suspendida, fricción pie-superficie y modelos de terreno
  entre las entradas relevantes de simulación:
  https://ntrs.nasa.gov/citations/19740004422
- NASA CR-201601 desarrolla un método de dinámica de tren y refuerza la separación entre
  modelo de contacto, integración y validación:
  https://ntrs.nasa.gov/citations/19960049759

Estas fuentes justifican la arquitectura y los observables, no los números específicos del
tren ficticio de Starship. Los valores V1 permanecen en datos para poder sustituirlos cuando
exista una fuente autorizada o una campaña de calibración.

## Brechas siguientes

1. Unificar datum de física, grafo de partes y renderer; calcular COM real en ese marco.
2. Compartir un proveedor de terreno render/física con DEM, normal local y material.
3. Separar fricción estática/dinámica con estado stick-slip y fuerzas longitudinal/lateral.
4. Añadir contacto de casco/motores, deformación/fallo progresivo y cuerpos tras rotura.
5. Barrer masa, inclinación, dispersión atmosférica, viento y coeficientes; guardar envelopes.
6. Validar reproducción x1/x10/x100 y golden pad→órbita→touchdown sin siembra a 70 km.
