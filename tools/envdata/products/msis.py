"""Dated thermosphere from NRLMSIS 2.1 (environment plan E4.3).

For each listed day of a mission the official NRLMSIS 2.1 Fortran model (through pymsis) is run
on a grid of universal time (every 3 h), local solar time (every 2 h; longitude follows from
both), geodetic latitude (-90..90 every 15 deg) and altitude (80-300 km every 10 km, 300-1000 km
every 50 km). Universal time is a grid axis because NRLMSIS is not a function of local time
alone: at fixed local time its density varies up to 17 % with UT (longitude and geomagnetic
terms, high latitude, 800 km). Only the days a mission spends in the air are listed, each from
0 to 24 UT with that day's indices: NRLMSIS's daily Ap and F10.7 make it step at midnight, and
interpolating across the step would invent an atmosphere the model does not have. Each cell stores ln(mass density), temperature and ln of the total number density of the
thermal neutral species (anomalous oxygen, which is in the mass density but not in thermal
equilibrium, is excluded), so pressure follows as p = n k T. Space weather comes from the GFZ Potsdam daily file: F10.7 of the
previous day, its 81-day centred mean, and the daily Ap of the day.
"""
from __future__ import annotations

import datetime
import math

import numpy as np

MISSIONS = {
    "msis_as506": (["1969-07-16", "1969-07-17", "1969-07-24"],
                   "Apollo 11 (AS-506): launch and parking orbit (07-16/17) and entry (07-24)"),
}
UT_HOURS = list(range(0, 25, 3))  # 0..24: each date carries its own daily indices to its end
LST_HOURS = list(range(0, 24, 2))
LATITUDES = list(range(-90, 91, 15))
ALTITUDES_KM = list(range(80, 300, 10)) + list(range(300, 1001, 50))
SPECIES = ("N2", "O2", "O", "HE", "H", "AR", "N", "NO")
AMU = 1.66053906660e-27
MASSES = {"N2": 28.0134, "O2": 31.9988, "O": 15.9994, "HE": 4.002602, "H": 1.00794, "AR": 39.948,
          "N": 14.0067, "NO": 30.0061}


def _indices(text: str):
    daily = {}
    for line in text.splitlines():
        if line.startswith("#") or not line.strip():
            continue
        f = line.split()
        date = datetime.date(int(f[0]), int(f[1]), int(f[2]))
        # Columns: date(3) days days_m Bsr dB Kp1..Kp8 ap1..ap8 Ap SN F10.7obs F10.7adj D
        daily[date] = (float(f[23]), float(f[25]))  # Ap, observed F10.7
    return daily


def build_series(product_id: str, sources: dict):
    import pymsis

    dates, description = MISSIONS[product_id]
    daily = _indices(sources["gfz_kp_ap_f107"].decode("ascii"))
    # Guard the column mapping against the published row of 1969-07-16 (Ap 10, F10.7obs 122.1).
    if daily[datetime.date(1969, 7, 16)] != (10.0, 122.1):
        raise ValueError("GFZ column mapping does not reproduce the 1969-07-16 row")
    rows, weather = [], []
    for date in dates:
        day = datetime.date.fromisoformat(date)
        ap = daily[day][0]
        f107 = daily[day - datetime.timedelta(days=1)][1]
        window = [daily[day + datetime.timedelta(days=k)][1] for k in range(-40, 41)]
        if min(ap, f107, *window) < 0:
            raise ValueError(f"missing space weather around {day}")
        f107a = sum(window) / len(window)
        weather.append({"date": day.isoformat(), "ap": ap, "f107": f107, "f107a": round(f107a, 4)})
        for ut in UT_HOURS:
            for lst in LST_HOURS:
                lon = ((lst - ut) * 15.0 + 180.0) % 360.0 - 180.0
                when = np.datetime64(f"{day.isoformat()}T00:00") + np.timedelta64(ut, "h")
                if ut == 24:
                    when = when - np.timedelta64(1, "s")  # the date's own last instant, same daily indices
                out = pymsis.calculate(np.array([when]), np.array([lon]),
                                       np.array([float(v) for v in LATITUDES]),
                                       np.array([float(a) for a in ALTITUDES_KM]),
                                       f107s=np.array([f107]), f107as=np.array([f107a]), aps=np.array([[ap] * 7]),
                                       version=2.1)
                block = out.reshape(len(LATITUDES), len(ALTITUDES_KM), -1)
                for i, _lat in enumerate(LATITUDES):
                    for j, _alt in enumerate(ALTITUDES_KM):
                        values = block[i, j]
                        rho = float(values[pymsis.Variable.MASS_DENSITY])
                        temperature = float(values[pymsis.Variable.TEMPERATURE])
                        n_total = mass = 0.0
                        for species in SPECIES:
                            n = float(values[getattr(pymsis.Variable, species)])
                            if math.isfinite(n) and n > 0:
                                n_total += n
                                mass += n * MASSES[species] * AMU
                        if not (rho > 0 and temperature > 0 and n_total > 0):
                            raise ValueError(f"invalid MSIS cell {day} {ut} {lst} {_lat} {_alt}")
                        rows.append([math.log(rho), temperature, math.log(n_total)])
    header = {"product": product_id, "description": description, "model": f"NRLMSIS 2.1 via pymsis {pymsis.__version__}",
              "time_scale": "UTC", "frame": "geodetic", "dates": dates, "ut_hours": UT_HOURS,
              "lst_hours": LST_HOURS, "latitudes_deg": LATITUDES, "altitudes_km": ALTITUDES_KM,
              "order": "date, ut, lst, latitude, altitude", "space_weather": weather,
              "columns": ["ln_density", "temperature_k", "ln_number_density"], "units": ["ln(kg/m3)", "K", "ln(1/m3)"]}
    return header, rows
