#!/usr/bin/env python3
"""Generate the NRLMSIS oracle (environment plan E4.3).

    python3 tools/envdata/fixtures/msis.py

ExosphereSimulation.Tests/Fixtures/environment/msis_as506.json: NRLMSIS 2.1 (pymsis) density,
temperature and pressure at 300 random instants and places inside the msis_as506 product's covered
intervals (1969-07-16 0 UT to 07-17 21 UT and 07-24 0-21 UT, any longitude and latitude, 120-950 km), with the same GFZ
space weather the product uses, to measure the product's interpolation error.
"""
import datetime
import json
import pathlib
import random
import sys

import numpy as np
import pymsis

ROOT = pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "envdata"))
from products import msis as product  # noqa: E402

CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "msis_as506.json"
K = 1.380649e-23


def main():
    daily = product._indices((CACHE / "gfz_kp_ap_f107" / "Kp_ap_Ap_SN_F107_since_1932.txt").read_text())
    rng = random.Random(506)
    windows = [(datetime.datetime(1969, 7, 16, 0), 45 * 3600), (datetime.datetime(1969, 7, 24, 0), 21 * 3600)]
    rows = []
    for k in range(300):
        start, span = windows[k % 2]
        t = start + datetime.timedelta(seconds=rng.uniform(0, span))
        day = t.date()
        ap = daily[day][0]
        f107 = daily[day - datetime.timedelta(days=1)][1]
        f107a = sum(daily[day + datetime.timedelta(days=k)][1] for k in range(-40, 41)) / 81
        lat, lon, alt = rng.uniform(-80, 80), rng.uniform(-180, 180), rng.uniform(120, 950)
        out = pymsis.calculate(np.array([np.datetime64(t.isoformat())]), np.array([lon]), np.array([lat]), np.array([alt]),
                               f107s=np.array([f107]), f107as=np.array([f107a]), aps=np.array([[ap] * 7]), version=2.1).reshape(-1, 11)[0]
        n = sum(float(out[getattr(pymsis.Variable, s)]) for s in product.SPECIES if np.isfinite(out[getattr(pymsis.Variable, s)]))
        rows.append({"utc": [t.year, t.month, t.day, t.hour, t.minute, t.second + t.microsecond * 1e-6],
                     "latitude_deg": lat, "longitude_deg": lon, "altitude_m": alt * 1000.0,
                     "density": float(out[pymsis.Variable.MASS_DENSITY]), "temperature": float(out[pymsis.Variable.TEMPERATURE]),
                     "pressure": n * K * float(out[pymsis.Variable.TEMPERATURE])})
    OUT.write_text(json.dumps({"generator": "tools/envdata/fixtures/msis.py", "oracle": f"pymsis {pymsis.__version__} NRLMSIS 2.1",
                               "generated": datetime.date.today().isoformat(), "points": rows}, indent=1) + "\n")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} points")


if __name__ == "__main__":
    main()
