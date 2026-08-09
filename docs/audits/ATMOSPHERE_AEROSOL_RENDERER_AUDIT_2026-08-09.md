# Auditoría de integración visual de aerosoles — 2026-08-09

## Alcance

Se conectó el estado `AerosolClimateState` con el consumidor visual principal de atmósfera,
`SkyController` + `assets/shaders/space_sky.gdshader`. La integración es opt-in: una atmósfera
sin `aerosol_climate` conserva exactamente la ruta Mie anterior.

## Cadena física

1. `AtmosphereModelJson` lee el bloque `aerosol_climate` y lo normaliza en el límite de datos.
2. `AtmosphereAerosolOptics.Resolve` evalúa AOD550, Ångström, latitud, tiempo y altura. La
   escala Mie se normaliza contra la columna vertical Mie visible ya configurada; no se suma
   una segunda columna de aerosol encima del modelo óptico existente.
3. `SkyController` publica la escala, el exponente y la escala de altura al shader. Actualiza
   la envolvente cada 600 s de tiempo simulado, evitando invalidar el cielo en cada tick.
4. El shader aplica la ley de Ångström a bandas aproximadas R/G/B (650/550/450 nm), sustituye
   la envolvente vertical Mie por la escala climática y usa los mismos coeficientes en
   extinción, dispersión, orden bajo y twilight. Rayleigh, ozono y refracción no se escalan.

La ruta sin perfil mantiene `aerosol_climate_enabled=false`, `aerosol_mie_scale=1` y un
factor vertical unitario.

## Perfiles de datos

- Earth: AOD550 0.04 como columna costera despejada de referencia para Starbase; el usuario
  puede elevarlo para representar bruma o polvo.
- Mars: AOD550 0.30, predominio de polvo y modulación estacional/diurna más fuerte.
- Venus: AOD550 1.50, exponente bajo y escala de altura comparable a su cubierta densa.

Los valores son perfiles de gameplay acotados, no una afirmación de un parte meteorológico en
tiempo real. Raptor, atmósfera superior y aerodinámica no leen este estado.

## Evidencia automatizada

| Evidencia | Resultado |
|---|---|
| `AtmosphereAerosolJsonTests` + `AtmosphereAerosolOpticsTests` | 13/13 |
| `dotnet build Exosphere.csproj --no-restore` | 0 warnings, 0 errors |
| `aerosol-v1`, AOD Earth 0.08 | 16/16 hitos físicos; falla visual por `ground_day skyWhiteClipFrac=0.16751` |
| `aerosol-v2`, AOD Earth 0.04 | `ATMOSPHERE_OK`, 16/16 hitos, 1157 frames, PNGs verificadas |

La corrida v1 se conserva como regresión deliberada: el sistema físico avanzó, pero el gate de
exposición la rechazó. La corrida v2 bajó `ground_day skyWhiteClipFrac` a `0.08642` y quedó bajo
el umbral `0.10`; el resto de la matriz no reportó `GAP`, `FALLBACK` ni error de shader. Las
capturas y el log reproducible están en `/tmp/exo_aerosol_v2/` y `/tmp/exo_aerosol_v2.log`.

## Limitaciones y siguiente paso

- El clima se evalúa en el punto del observador y se transporta mediante una envolvente
  vertical; todavía no hay un campo 3D de aerosoles por cada muestra del rayo.
- La LUT de transmittance/multiple scattering permanece basada en el perfil óptico estático;
  el aerosol dinámico modifica el integrador realtime, no regenera LUTs durante vuelo.
- Conviene añadir una captura comparativa Earth/Mars/Venus y un gate que compruebe que cambiar
  AOD afecta Mie pero no Rayleigh/ozono antes de considerar cerrado el tramo visual.
