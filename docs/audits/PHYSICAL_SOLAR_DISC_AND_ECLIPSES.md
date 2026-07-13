# Disco solar físico y eclipses continuos

**Fecha:** 2026-07-12 · **Estado:** integrado y verificable

## Resultado

El Sol ya no es únicamente una dirección de iluminación ni una esfera de backdrop. El sky
renderiza una fotosfera HDR cuyo radio angular se deriva cada frame de `asin(Rsol/distancia)`.
A 1 AU el diámetro resultante es aproximadamente 0,53°, consistente con la experiencia visual
terrestre. Al cambiar la distancia heliocéntrica cambia el tamaño aparente, no la radiancia de
la superficie solar.

El brillo radial usa una ley lineal de oscurecimiento de limbo y se normaliza por su promedio,
de modo que añadir detalle a la fotosfera no cambia artificialmente el flujo integrado. La
extinción RGB ya calculada por Rayleigh, Mie, ozono y nubes actúa sobre el disco: el Sol se
atenúa y enrojece cerca del horizonte sin interpolar colores manuales.

## Eclipse

Todos los cuerpos esféricos situados delante del Sol se evalúan mediante radios aparentes y
área exacta de solapamiento de discos. El ocultante más relevante recorta la fotosfera por
píxel, por lo que aparecen geometrías parcial, total y anular; la radiancia restante no se
convierte en un disco gris. La separación angular CPU usa:

```text
θ = atan2(|ŝ × ô|, ŝ · ô)
```

Esto conserva separaciones diminutas que `acos(dot)` puede redondear a cero. El área de lente
se calcula normalizada por el radio solar para mantener precisión con milirradianes.

La misma fracción solar controla ahora cuatro sistemas coherentes:

- energía de la luz direccional;
- fuentes directas del scattering y de las nubes;
- potencia de paneles solares;
- calentamiento térmico solar.

Así la penumbra deja de ser una transición visual suave con sistemas internos todavía binarios.
La antigua esfera scaled-space del Sol se omite para evitar doble fotosfera y siluetas rotas.

## Escala HDR

El disco se suma después de la compresión local del scattering y antes del Filmic tonemap de
Godot. Su radiancia relativa (`32`) conserva un núcleo saturado y permite bloom/exposición sin
convertir todo el cielo en blanco. Es una integración HDR estable, aunque el pipeline todavía
no es radiometría absoluta extremo a extremo.

## Límites pendientes

- La atmósfera aún trata el Sol como fuente puntual dentro de cada muestra; falta convolucionar
  la fase Mie con el disco finito para amaneceres y contactos extremadamente precisos.
- Se dibuja un ocultante visual principal; la unión simultánea de varios tránsitos es rara pero
  aún no se rasteriza completa.
- No hay refracción/aplanamiento del disco en el horizonte, turbulencia, manchas ni granulación.
- La corona y prominencias durante totalidad necesitan otro rango de exposición y no se inventan
  como un halo artístico. La aureola visible procede del scattering Mie existente.
- Cámara muy alejada y astronauta siguen compartiendo el observador físico de la nave.

## Evidencia

Las pruebas cubren tamaño solar a 1 AU, total/parcial/anular, solución analítica de dos discos,
invariancia de escala, monotonía de penumbra y potencia proporcional. La carga headless de
Godot valida el shader y CI verifica la compilación e integración completa del proyecto.
