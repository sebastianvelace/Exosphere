# Frecuencia de actualización del cockpit — fase 18

Estado: promovida con validación visual; sin promesa de FPS de hardware  
Fecha: 2026-08-13  
Alcance: `CockpitInstruments` y sus tres `SubViewport` 512×512

## Problema

Las tres pantallas se mantenían en `SubViewport.UpdateMode.Always` durante IVA y cada frame
se llamaba `QueueRedraw()` para cada panel. El snapshot fuente es una representación de HUD,
no un sensor de alta frecuencia: renderizar tres targets 512² a la frecuencia del juego
repetía trabajo de presentación sin aumentar la fidelidad de la simulación.

## Cambio

Los viewports siguen pausados con `Disabled` fuera del cockpit. Al entrar en IVA se solicitan
actualizaciones `UpdateMode.Once` cada 1/30 s; cada solicitud redibuja los tres paneles juntos.
El scheduler no toca `Vessel.Tick`, entradas, controles, física, snapshots ni el tamaño 512².
El primer frame activo se fuerza con un acumulador inicializado al periodo completo.

## Gates

- El contrato exige tres viewports 512², pausa exterior, `UpdateMode.Once`, frecuencia explícita
  de 30 Hz y el loop de los tres paneles.
- La captura cockpit debe conservar PFD, actitud, propulsión y estado orbital legibles.
- La prueba exterior smoke debe seguir arrancando y capturando el pad.
- No se declara una ganancia de FPS hasta comparar el probe de render; el host disponible es
  llvmpipe y no representa una GPU objetivo.

## Decisión pendiente

La captura cockpit pasó con PFD, actitud, propulsión y estado orbital legibles; el smoke exterior
pasó con `SMOKE_OK`. El probe opt-in produjo 26 muestras en llvmpipe:

```text
gpu_ms p50/p95/p99 = 135.555 / 332.922 / 954.654
cpu_ms p50/p95/p99 = 136.529 / 334.839 / 969.962
draw_calls p50/p95/p99 = 627 / 2311 / 2312
adapter = llvmpipe (LLVM 20.1.2, 256 bits)
```

Los draw calls no muestran una mejora aislable frente al baseline llvmpipe de la fase 16, por
lo que no se declara una ganancia de FPS. El scheduler se promueve por su límite determinista
de trabajo de presentación en hardware capaz de sostener más de 30 Hz, con la limitación
documentada de que el host software no puede validar ese beneficio. La resolución 512² se
conserva; reducirla requiere una prueba visual distinta.
