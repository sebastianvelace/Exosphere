# Nubes volumétricas planetarias

**Fecha:** 2026-08-09 · **Estado:** V8: capa volumétrica + billow 3D determinista verificada

## Cambio verificable

Las nubes cercanas dejaron de ser un color pegado a la superficie. El cielo intersecta una
cáscara esférica definida en metros y recorre únicamente el segmento visible entre su base y
su techo. Esto conserva espesor, horizonte y curvatura al despegar, atravesar la capa y verla
desde arriba. La Tierra usa 1,2–12 km; Venus usa su cubierta alta de 48–70 km. Marte permanece
sin nubes hasta disponer de un perfil meteorológico defendible.

La densidad combina una envolvente vertical suave, un mapa meteorológico equirectangular y un
sesgo macro 3D continuo. El mapa mantiene los continentes/frentes y su detalle; el ruido se evalúa
en coordenadas orientadas del mundo para que la nube tenga variación dentro del espesor, no una
lámina pegada a la latitud/longitud:

```text
field = weather(uv) + 0.25 · (noise3(x) − 0.5) + 0.20 · (detail(uv) − 0.5)
ρcloud(x) = ρvertical(h) · smoothstep(threshold − 0.12, threshold + 0.10, field)
```

`noise3` usa una interpolación diagonal de dos esquinas y un hash aritmético determinista. Es una
decisión de rendimiento: el shader corre el rayo de vista a 20 pasos y la autosombra a 7 pasos.
La autosombra usa deliberadamente `ρcloud_base` (weather + detalle, sin ruido macro) para no
multiplicar el coste por cada rayo solar; ambos recorridos comparten soporte vertical, cobertura,
advección y extinción Beer–Lambert.

La advección usa tiempo de simulación y una velocidad angular por planeta, por lo que pausa y
time-warp conservan una meteorología reproducible. El mapa terrestre de 8192×4096 se interpreta
como cobertura, no como color final; V8 añade el sesgo volumétrico sin sustituirlo.

## Transporte de luz

Por cada segmento de vista:

```text
σt(x) = ρvertical(x) · ρweather(x) · σcloud
αsegmento = 1 − exp(−σt Δs)
Tcloud ← Tcloud · (1 − αsegmento)
Lcloud += Tcloud · [Tatm,sun · Tcloud,sun · PdualHG(μ) + Lambient] · αsegmento
```

La luz solar dentro de la nube realiza un segundo recorrido de Beer–Lambert. Una mezcla de
Henyey–Greenstein hacia delante (`g=0,82`) y un lóbulo trasero débil (`g=−0,20`) reproduce el
silver lining sin usar un borde dibujado. La sombra sólida del planeta anula la fuente directa.
Las estrellas reciben la transmitancia acumulada de nube.

El shader usa 20 muestras de vista y 7 de autosombra. El cubemap se actualiza incrementalmente,
una cara por frame, porque el campo meteorológico cambia lentamente; así se evita ejecutar todo
el transporte atmosférico seis veces por frame.

## Datos y contratos

`AtmosphereOptics` contiene base, techo, extinción en m⁻¹, cobertura y viento. Un perfil solo se
habilita cuando todos sus valores son finitos, la base no es negativa, el techo está encima,
la extinción es positiva y la cobertura pertenece a `(0,1]`. Las funciones CPU reproducen la
envolvente vertical, el remapeo meteorológico y la extinción local para que esos invariantes no
dependan del GPU.

## Límites pendientes

Esto mejora sustancialmente la percepción, pero todavía no es microfísica completa:

- un solo campo macro 3D no reproduce torres convectivas ni evolución meteorológica 3D;
- hay una sola capa agregada por planeta, sin especies cirrus/cumulus ni precipitación;
- la composición cielo-nube aproxima el orden del haze situado detrás de la nube;
- las sombras todavía no se proyectan sobre terreno y vehículo;
- la esfera terrestre orbital conserva su representación superficial para resolver profundidad
  contra el disco; debe migrar a una shell geométrica sincronizada;
- faltan reproyección temporal, microfísica, sombras sobre terreno, un campo macro en autosombra y
  goldens automatizados por altitud/clima.

El próximo gate correcto es una weather map dinámica con humedad/especies y un modelo de sombra
proyectada sobre superficie, seguido por aerial perspective segmentada; aumentar colores o
contraste no resolvería esos límites.

## Verificación

```bash
bash tools/atmosphere_quick_check.sh
bash tools/ci_check.sh
```

La suite cubre soporte vertical finito, bordes suaves, perfiles inválidos, cobertura monótona,
extinción acotada, paridad JSON/preset y diferencias físicas Tierra–Marte–Venus. La carga
headless de Godot valida el contrato del shader. La matriz framebuffer V8 (`--atmosphere`) cubre
16 estados y terminó con `ATMOSPHERE_OK`; los goldens con comparación pixel a pixel siguen siendo
un gate pendiente y ningún harness temporal se versiona.
