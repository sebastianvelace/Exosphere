#!/usr/bin/env python3
"""Generate the dated-ephemeris oracle fixture (environment plan E2).

    python3 tools/envdata/fixtures/ephemeris.py

Writes ExosphereSimulation.Tests/Fixtures/environment/ephemeris.json with body states (m, m/s)
in the simulation frame (ICRF rotated about +X by the IAU 2006 obliquity) relative to the
solar-system barycentre, plus the geocentric Moon:

* "spice": SPICE spkgeo on de440s.bsp at mission epochs, random instants 1960-2050 and both
  sides of Chebyshev span boundaries (the product's own source, read independently);
* "horizons": JPL Horizons (an independent JPL system, DE441) geocentric Moon at the Apollo 11
  launch epoch, queried by the API with the exact request recorded.
"""
import datetime
import json
import math
import pathlib
import urllib.parse
import urllib.request

import numpy as np
import spiceypy

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "ephemeris.json"
CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"
EPS0 = 84381.406 * math.pi / (180 * 3600)
BODIES = {"sun": "SUN", "mercury": "MERCURY BARYCENTER", "venus": "VENUS BARYCENTER", "earth": "EARTH",
          "moon": "MOON", "mars": "MARS BARYCENTER", "jupiter": "JUPITER BARYCENTER", "saturn": "SATURN BARYCENTER"}
FIRST_TDB_S = (2436934.5 - 2451545.0) * 86400.0
APOLLO11_TDB_S = -930_000_000.0  # replaced below by the exact launch TDB


def to_sim(v):
    c, s = math.cos(EPS0), math.sin(EPS0)
    m = np.array([[1, 0, 0], [0, c, s], [0, -s, c]])
    return (m @ np.asarray(v)).tolist()


def states(t):
    out = {}
    for key, name in BODIES.items():
        s, _ = spiceypy.spkgeo(spiceypy.bodn2c(name), t, "J2000", 0)
        s = np.array(s) * 1000.0
        out[key] = {"position_m": to_sim(s[:3]), "velocity_mps": to_sim(s[3:])}
    s, _ = spiceypy.spkgeo(301, t, "J2000", 399)
    s = np.array(s) * 1000.0
    out["moon_geocentric"] = {"position_m": to_sim(s[:3]), "velocity_mps": to_sim(s[3:])}
    return out


def horizons_moon(tdb_jd):
    params = {"format": "json", "COMMAND": "'301'", "CENTER": "'500@399'", "EPHEM_TYPE": "'VECTORS'",
              "REF_PLANE": "'FRAME'", "REF_SYSTEM": "'ICRF'", "VEC_TABLE": "'2'", "OUT_UNITS": "'KM-S'",
              "TLIST": f"'{tdb_jd:.10f}'", "TLIST_TYPE": "'JD'", "TIME_TYPE": "'TDB'", "CSV_FORMAT": "'YES'"}
    url = "https://ssd.jpl.nasa.gov/api/horizons.api?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=120) as response:
        reply = json.loads(response.read().decode())
    text = reply["result"]
    block = text[text.index("$$SOE") + 5:text.index("$$EOE")].strip()
    fields = [f.strip() for f in block.split(",")]
    x, y, z, vx, vy, vz = (float(v) for v in fields[2:8])
    source = next((line.strip() for line in text.splitlines() if "Target body name" in line or "Ephemeris" in line), "")
    return {"query": url, "source_line": source,
            "position_m": to_sim(np.array([x, y, z]) * 1000.0), "velocity_mps": to_sim(np.array([vx, vy, vz]) * 1000.0)}


def main():
    spiceypy.furnsh(str(CACHE / "naif_de440s" / "de440s.bsp"))
    import erfa
    u1, u2 = erfa.dtf2d("UTC", 1969, 7, 16, 13, 32, 0.0)
    a1, a2 = erfa.utctai(u1, u2)
    t1, t2 = erfa.taitt(a1, a2)
    apollo11 = ((t1 - 2451545.0) + t2) * 86400.0 + erfa.dtdb(t1, t2, (u1 - 0.5) % 1.0 + u2, 0, 0, 0)
    instants = [(apollo11, "Apollo 11 launch (TDB)"), (0.0, "J2000")]
    rng = np.random.default_rng(1969)
    for _ in range(60):
        instants.append((float(rng.uniform(FIRST_TDB_S + 86400.0, (2469807.5 - 2451545.0) * 86400.0 - 86400.0)), "random"))
    for days in (8, 16, 32, 64):  # both sides of a span boundary of every series length
        boundary = FIRST_TDB_S + 300 * days * 86400.0
        instants += [(boundary - 1e-3, f"{days}-day boundary -1 ms"), (boundary + 1e-3, f"{days}-day boundary +1 ms")]
    rows = [{"tdb_s": t, "note": note, "states": states(t)} for t, note in instants]
    horizons = horizons_moon(2451545.0 + apollo11 / 86400.0)
    horizons["tdb_s"] = apollo11
    OUT.write_text(json.dumps({
        "generator": "tools/envdata/fixtures/ephemeris.py",
        "frame": "ICRF rotated about +X by 84381.406 arcsec (simulation frame); barycentric unless noted",
        "oracle": f"spiceypy {spiceypy.__version__} spkgeo on de440s.bsp",
        "independent": "JPL Horizons API (DE441) geocentric Moon",
        "generated": datetime.date.today().isoformat(),
        "instants": rows, "horizons_moon_apollo11": horizons}, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} instants")


if __name__ == "__main__":
    main()
