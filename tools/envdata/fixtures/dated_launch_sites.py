#!/usr/bin/env python3
"""Generate the dated launch-site oracle (environment plan E1.2b historical gate).

    python3 tools/envdata/fixtures/dated_launch_sites.py

Writes ExosphereSimulation.Tests/Fixtures/environment/dated_launch_sites.json: the inertial
position and velocity of launch pads at their launch epochs, from SPICE (georec on the WGS84
ellipsoid in ITRF93, sxform ITRF93 -> J2000 with the NAIF high-precision kernel
earth_620120_260806.bpc, which NAIF refers to the ICRF), rotated into the simulation frame by
the IAU 2006 obliquity about +X. SPICE is handed ERFA's TDB because its own UTC is wrong before
1972. Site coordinates are read from data/launch_sites.
"""
import datetime
import json
import math
import pathlib

import erfa
import numpy as np
import spiceypy

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "dated_launch_sites.json"
CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"
A, B = 6378137.0, 6356752.314245
EPS0 = 84381.406 * math.pi / (180 * 3600)
CASES = [
    ("kennedy", (1969, 7, 16, 13, 32, 0.0), "Apollo 11 launch (SP-238)"),
    ("cape_canaveral_lc14", (1962, 2, 20, 14, 47, 39.0), "Friendship 7 launch"),
    ("starbase", (2024, 6, 6, 12, 50, 0.0), "Starship Flight 4 launch"),
]


def main():
    spiceypy.furnsh(str(CACHE / "naif_lsk" / "naif0012.tls"))
    spiceypy.furnsh(str(CACHE / "naif_earth_bpc" / "earth_620120_260806.bpc"))
    c, s = math.cos(EPS0), math.sin(EPS0)
    to_sim = np.array([[1, 0, 0], [0, c, s], [0, -s, c]])
    rows = []
    for site_id, utc, note in CASES:
        site = json.loads((ROOT / "data" / "launch_sites" / f"{site_id}.json").read_text())
        u1, u2 = erfa.dtf2d("UTC", *utc)
        a1, a2 = erfa.utctai(u1, u2)
        t1, t2 = erfa.taitt(a1, a2)
        tdb = ((t1 - 2451545.0) + t2) * 86400.0 + erfa.dtdb(t1, t2, (u1 - 0.5) % 1.0 + u2, 0.0, 0.0, 0.0)
        body = np.array(spiceypy.georec(math.radians(site["longitude"]), math.radians(site["latitude"]),
                                        site["altitude"] / 1000.0, A / 1000.0, (A - B) / A)) * 1000.0
        state = np.array(spiceypy.sxform("ITRF93", "J2000", tdb)) @ np.concatenate([body, np.zeros(3)])
        rows.append({"site": site_id, "utc": list(utc), "note": note, "tdb_s": tdb,
                     "position_m": (to_sim @ state[:3]).tolist(), "velocity_mps": (to_sim @ state[3:]).tolist()})
    OUT.write_text(json.dumps({
        "generator": "tools/envdata/fixtures/dated_launch_sites.py",
        "oracle": f"spiceypy {spiceypy.__version__} georec + sxform ITRF93->J2000 (earth_620120_260806.bpc), "
                  f"TDB from pyerfa {erfa.__version__}",
        "generated": datetime.date.today().isoformat(),
        "cases": rows}, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} cases")


if __name__ == "__main__":
    main()
