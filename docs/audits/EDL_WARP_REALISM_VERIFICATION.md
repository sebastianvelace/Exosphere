# Verificación de realismo EDL, motores y warp

**Fecha:** 2026-07-10
**Rama de trabajo:** `integrate/jul2026-realism-loop`

## Objetivo y veredicto

Esta ola elimina tres atajos que cambiaban lo que vería o sentiría un astronauta:
la actitud impuesta por quaternion, el cluster de seis motores tratado como un motor
infinitamente regulable y la desaparición de aero/térmica/contacto bajo warp.

El resultado es un modelo 6-DoF agregado y coherente, no una réplica certificable de
Starship. Sigue faltando CFD/coeficientes publicados, motores como cuerpos individuales,
tensor de inercia completo, slosh, contacto por patas y un TPS multicapa. Por ello la
palabra «totalmente realista» no se usa como criterio de aprobación.

## Cambios físicos

- `AttitudeGuidance` convierte error de quaternion y velocidad angular en comandos
  pitch/yaw/roll. No modifica orientación.
- Entry vuela lift-up a 70° AoA. El modelo `CL = CLmax·sin(2α)` produce L/D aproximado
  de 0,3 y evita la trayectoria balística de lift cero a 90°.
- Las cuatro flaps agregadas producen momento desde `q·S·L·Cn / I`; la autoridad cae
  de forma natural al desaparecer la atmósfera.
- El flip usa torque de flaps/gimbal y una tasa máxima de 0,35 rad/s. Los Raptors se
  encienden inicialmente al mínimo estable en vez de aplicar empuje completo mal orientado.
- El aterrizaje selecciona 1, 2 o 3 motores centrales y nunca manda un Raptor por debajo
  de su mínimo estable declarado (40%). El visual muestra el subconjunto seleccionado.
- La ley de descenso cierra tanto velocidad vertical como lateral y solo ordena actitud
  vertical cuando la deriva ya está controlada.
- Cualquier thrust/spool, atmósfera, flujo térmico o proximidad de contacto saca la nave
  de rails. Una periapsis atmosférica fuerza propagación acotada antes de cruzar la interfaz.
- La ruta mixta de warp reutiliza cargas estructurales, calentamiento y choque de x1.

## Ensayos que fallaron y qué demostraron

El harness `tools/visual_playtest.sh --edl` no acepta solo una imagen: exige PNG de
entry, retro burn, fin de flip y touchdown, más `SUMMARY reason=LANDED`.

| Iteración | Retro burn | Resultado | Diagnóstico |
|---|---:|---:|---|
| Base | 970 m/s @ 6,5 km | impacto 524 m/s | broadside exacto: CL=0, entrada demasiado balística |
| Lift-up + flaps V1 | 171 m/s @ 1,7 km | impacto 41 m/s | throttle ignoraba velocidad lateral; verticalización prematura |
| Ley lateral, full flip ignition | 176 m/s @ 2,3 km | pérdida 527 m/s | empuje alto antes de alinear el eje |
| Ignición mínima | 161 m/s @ 2,6 km | pérdida 523 m/s | flaps V1 no vencían el momento estático a q≈12 kPa |
| Eje de thrust separado del roll | 105 m/s @ 1,6 km | flip 9,16 s; impacto 59 m/s | throttle vertical ignoraba spool/deriva |
| Flip anticipado + damping | 102 m/s @ 2,8 km | hover y reserva agotada | singularidad al usar `-velocidad` cerca de cero |
| Feed-forward por eje real | 102 m/s @ 2,8 km | llegó a 35–70 m | error lateral contado también como thrust vertical |

Estas corridas se conservan como evidencia de diseño: cada gate falló por una causa
física observable y no se relajó el umbral de impacto para hacerlo pasar.

### Corredor aprobado

Comando: `bash tools/visual_playtest.sh --edl --skip-build`

| Hito | Evidencia final |
|---|---|
| Entry | 69,7 km; 1.805 m/s; estado lift-up visible |
| Retro burn | 2,77 km; 101,8 m/s; tres motores seleccionados |
| Flip completo | 15,33 s; alineación 0,99713; ω=0,0408 rad/s; dos motores |
| Final descent | deriva 0,2–1,5 m/s; transición física 2→1 motores |
| Touchdown | 5,5 m de contacto; −1,5 m/s vertical; 0,8 m/s lateral; upright=0,9994 |

El harness terminó con `SUMMARY reason=LANDED`, creó cinco PNG válidos (222–391 KiB)
y no utilizó setter de orientación durante la trayectoria. La siembra a 70 km sigue
siendo una condición de prueba; no sustituye el golden pad→órbita→aterrizaje.

## Evidencia automatizada

- `AerodynamicLiftTests`: dirección/continuidad de lift, L/D a 70°, eje lift-up,
  escala q de flaps y autoridad frente al momento estático nominal.
- `AttitudeGuidanceTests`: mapeo de ejes +Y, damping, roll opcional y ausencia de snap.
- `StarshipRealismTests`: selección discreta con thrust y flujo proporcionales.
- `WarpPhysicsParityTests`: salida de rails, heating bajo warp, paridad corta x1/x100
  y detección anticipada de periapsis atmosférica.

## Límites y próximos gates

1. Repetir un golden pad→órbita→entry sin siembra de estado ni fallback.
2. Barrer masa, dispersión atmosférica, AoA, reserva y viento; una única trayectoria
   nominal no prueba robustez.
3. Reemplazar coeficientes agregados por tablas Mach×AoA×flap validadas.
4. Modelar patas, fricción, pendiente y vuelco en contacto.
5. Comparar peak-q, heating y estado final x1/x10/x1000 en una reentrada completa.
