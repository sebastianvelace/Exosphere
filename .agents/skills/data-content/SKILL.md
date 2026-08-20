---
name: data-content
description: Esquemas de los datos JSON de Exosphere (data/bodies, data/parts, data/launch_sites). Úsala al añadir o editar cuerpos celestes, piezas de cohete o sitios de lanzamiento. El proyecto es data-driven — las constantes físicas viven aquí, no en código C#.
---

# Contenido data-driven (`data/*.json`)

Cuerpos, piezas y sitios se cargan desde JSON en runtime (`Universe.LoadFromDataDirectory`,
`SimulationBridge.DataDirectory = res://data`). **No hardcodees masas, radios, ISP, etc. en C#** —
van en los datos. Al crear contenido, copia el esquema de un archivo existente del mismo tipo.

## `data/bodies/*.json` — cuerpos celestes

Unidades SI. `gm` en m³/s², ángulos de `orbital_elements` en **grados**, tiempo en s desde J2000.

```json
{
  "id": "earth",                 // identificador único en minúsculas
  "name": "Earth",
  "mass": 5.972e24,              // kg
  "radius": 6371000,             // m (radio medio)
  "soi": 924000000,              // m (radio de la esfera de influencia)
  "rotational_period": 86164,    // s (sidéreo; negativo = retrógrado)
  "axial_tilt": 23.44,           // grados
  "has_atmosphere": true,
  "atmosphere": {
    "scale_height": 8500, "sea_level_density": 1.225,
    "sea_level_pressure": 101325, "sea_level_temperature": 288.15,
    "max_altitude": 140000,
    "layers": [                  // atmósfera estándar por capas
      {"alt_min": 0, "alt_max": 11000, "temp_base": 288.15, "lapse_rate": -0.0065}
    ]
  },
  "surface_gravity": 9.807,      // m/s²
  "gm": 3.986004418e14,          // μ = GM, m³/s²
  "orbital_elements": {          // null para el cuerpo raíz (Sun)
    "semi_major_axis": 1.496e11, "eccentricity": 0.0167, "inclination": 0.0,
    "longitude_of_node": -11.26, "argument_of_periapsis": 114.21,
    "mean_anomaly_at_epoch": 358.617, "epoch": 0.0, "reference_body": "sun"
  }
}
```

## `data/parts/*.json` — piezas

```json
{
  "id": "command_pod_mk1",
  "name": "Mk1 Command Pod",
  "description": "...",
  "category": "command",         // command | engine | tank | decoupler | ...
  "mass_dry": 840,               // kg
  "cost": 600,
  "drag_coefficient": 0.2,
  "heat_tolerance": 2400,        // K
  "max_crew": 1,
  "has_heat_shield": true,
  "monopropellant_capacity": 10,
  "attachment_nodes": [
    {"id": "bottom", "position": [0, -0.9, 0], "size": 1, "type": "stack"}
  ]
}
```

Motores/tanques añaden campos de empuje/ISP/propelente — copia un `engine_*`/`fuel_tank_*` existente.

## `data/launch_sites/*.json`

Lat/lon del sitio (Kennedy, Baikonur). Ojo: el render fija Cabo Cañaveral en 27.5°N/80.7°O.

## GOTCHAS

- **Recuerda: cada etapa es UNA parte-motor en el sim.** El empuje total de la etapa va en esa única
  parte (Super Heavy ≈ 74 MN), aunque visualmente haya 33 bells.
- IDs en minúsculas, únicos. `reference_body` debe referenciar un `id` existente.
- Tras añadir datos, build 0/0 + arrancar para confirmar que cargan sin excepción.
