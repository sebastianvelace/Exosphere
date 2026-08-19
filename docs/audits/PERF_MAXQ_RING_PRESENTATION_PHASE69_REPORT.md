# Fase 69 — muestra cacheada del anillo Max-Q

Fecha: 2026-08-18  
Área: `scripts/MaxQRingController.cs`, `tools/tests/maxq_ring_performance_contract_test.sh`

## Hallazgo

El efecto visual de condensación hacía su trabajo completo en cada `_Process`:

- buscaba el cuerpo Earth y recalculaba altitud, densidad, velocidad relativa y `q`;
- enumeraba las piezas con LINQ para decidir si el vehículo era Super Heavy;
- escribía `Visible` incluso cuando ya estaba oculto;
- reescribía la posición del anillo aunque el vehículo y su configuración no cambiaran.

El anillo sólo comunica una condición visual de Max-Q. No es parte de la aerodinámica ni del
solver físico.

## Cambio implementado

`MaxQRingController` toma una muestra de presentación cada `1.0 / 20.0` s. La visibilidad se
actualiza mediante `SetRingVisible`, que compara el estado anterior; la posición sólo se cambia
cuando cambia la nave activa o la presencia de Super Heavy. `HasSuperHeavy` recorre el buffer
concreto de piezas mediante índices, sin `Any` ni enumerador de compatibilidad.

La ecuación se conserva exactamente:

```text
rho = 1.225 * exp(-altitude / 8500)
q   = 0.5 * rho * relativeSpeed²
```

Se mantienen `Q_THRESH = 12,000 Pa`, `Q_PEAK = 35,000 Pa`, flicker, alpha y squash. El flicker
activo ahora se actualiza a 20 Hz, coherente con la cadencia de muestreo; necesita framebuffer
para validar que no introduce una regresión temporal visible.

## Reducción estructural

En un frame rate de 60 Hz:

| Trabajo | Antes | Ahora |
|---|---:|---:|
| muestras de entradas físicas del efecto | 60/s | 20/s |
| scans de piezas para Super Heavy | hasta 60/s | hasta 20/s |
| setters de visibilidad en estado estable | 60/s | 0 |
| setters de posición en configuración estable | 60/s | 0 |

No se presenta esto como una medición de FPS: el backend llvmpipe del entorno no permite una
captura de framebuffer reproducible.

## Verificación

- `maxq_ring_performance_contract_test.sh`: PASS;
- `dotnet build Exosphere.csproj --no-restore`: 0 warnings, 0 errors;
- `tools/ci_check.sh`: `CI_EXIT=0`;
- xUnit: **702/702 PASS**, 0 omitidos;
- contratos de optimización: **46/46 PASS**;
- Flight startup y Construction headless: PASS;
- validación de imagen/FPS: pendiente por X11/Xvfb, sin afirmar aceptación visual.

## Decisión

Promover como optimización CPU de presentación con gate visual pendiente. No habilitar ninguna
pausa de física ni usar el anillo como fuente de estado de vuelo. La siguiente repetición visual
debe cubrir ascenso atravesando `12–35 kPa`, entrada por debajo del umbral y cambio de nave tras
separación, comprobando alpha, posición y ausencia de parpadeo espurio.
