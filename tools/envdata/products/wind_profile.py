"""Measured wind profiles for dated launches (environment plan E4.1).

From an IGRA v2 station file (NOAA NCEI Integrated Global Radiosonde Archive), the sounding
named by the product: one row per level with a wind, (geometric altitude m, geopotential height
m, pressure Pa, wind-from direction deg clockwise from north, speed m/s, temperature K or NaN).
Geopotential heights convert to geometric altitude above mean sea level with the US Standard
Atmosphere 1976 radius 6356.766 km. Only mandatory and significant levels present in the
archive enter; fine structure between them is not recorded (for AS-506 the FER's peak of
9.6 m/s at 11.4 km lies between the 250 and 200 hPa levels, which read 7.0 m/s).
"""
from __future__ import annotations

R0 = 6356766.0
SOUNDINGS = {
    "wind_as506": ("noaa_igra_usm00074794", "#USM00074794 1969 07 16 14",
                   "Cape Kennedy (USM00074794) rawinsonde 1969-07-16 14Z, 28 min after the AS-506 launch"),
}


def build_series(product_id: str, sources: dict):
    dataset, header_line, description = SOUNDINGS[product_id]
    lines = sources[dataset].decode("ascii").splitlines()
    start = next(i for i, line in enumerate(lines) if line.startswith(header_line))
    count = int(lines[start][32:36])
    rows = []
    for line in lines[start + 1:start + 1 + count]:
        pressure = int(line[9:15])
        gph = int(line[16:21])
        temp = int(line[22:27])
        wdir = int(line[40:45])
        wspd = int(line[46:51])
        if gph == -9999 or wdir == -9999 or wspd == -9999:
            continue
        geometric = R0 * gph / (R0 - gph)
        rows.append([geometric, float(gph), float(pressure), float(wdir), wspd / 10.0,
                     (temp / 10.0 + 273.15) if temp > -8888 else float("nan")])
    rows.sort(key=lambda r: r[0])
    header = {"product": product_id, "description": description, "time_scale": "UTC", "frame": "local ENU",
              "station_latitude_deg": int(lines[start][55:62]) / 1e4, "station_longitude_deg": int(lines[start][63:71]) / 1e4,
              "columns": ["altitude_m", "geopotential_m", "pressure_pa", "wind_from_deg", "wind_speed_mps", "temperature_k"],
              "units": ["m", "m", "Pa", "deg", "m/s", "K"]}
    return header, rows
