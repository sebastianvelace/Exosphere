#!/usr/bin/env python3
"""Generate the Earth orientation oracle fixture (environment plan E1.2).

    python3 tools/envdata/fixtures/earth_orientation.py

Writes ExosphereSimulation.Tests/Fixtures/environment/earth_orientation.json with, at mission
epochs and random instants from 1962 to 2026:

* "erfa": the GCRS-to-ITRS matrix built by ERFA/SOFA from the same IERS model chain the
  simulation implements (eraXys06a + C04 dX/dY, eraC2ixys, eraEra00 of UT1, eraPom00 with
  eraSp00), with the C04 values interpolated here by 4-point Lagrange (UT1 through UT1 - TAI);
* "erfa_c2t06a": ERFA's one-call eraC2t06a (no dX/dY), an independent assembly of the matrix;
* "spice": SPICE pxform("J2000", "ITRF93") from the high-precision NAIF kernel
  earth_620120_260806.bpc, a separate EOP reduction (IAU 1976/1980 with corrections). NAIF's
  high-precision Earth kernels refer ITRF93 to the ICRF although the frame is named J2000, so no
  frame bias applies (measured: 0.19 m median without it, 0.76 m with eraBp06). The bias matrix
  is still recorded for reference.
"""
import datetime
import json
import pathlib
import random

import erfa
import numpy as np
import spiceypy

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "earth_orientation.json"
CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"
ARCSEC = np.pi / (180.0 * 3600.0)


def load_c04() -> dict[float, list[float]]:
    rows = {}
    for line in (CACHE / "iers_eop_c04" / "eopc04.1962-now").read_text().splitlines():
        if line.startswith("#") or not line.strip():
            continue
        f = line.split()
        rows[float(f[4])] = [float(f[5]), float(f[6]), float(f[7]), float(f[8]), float(f[9])]
    return rows


def lagrange(xs, ys, x):
    total = 0.0
    for i, (xi, yi) in enumerate(zip(xs, ys)):
        term = yi
        for j, xj in enumerate(xs):
            if j != i:
                term *= (x - xj) / (xi - xj)
        total += term
    return total


def eop_at(c04, mjd_utc):
    base = np.floor(mjd_utc)
    nodes = [base - 1, base, base + 1, base + 2]
    values = []
    for k in range(5):
        ys = []
        for n in nodes:
            v = c04[n][k]
            if k == 2:  # UT1-UTC -> UT1-TAI at the node's 0h UTC
                u1, u2 = 2400000.5, n
                a1, a2 = erfa.utctai(u1, u2)
                v -= ((a1 - u1) + (a2 - u2)) * 86400.0
            ys.append(v)
        values.append(lagrange(nodes, ys, mjd_utc))
    return values  # xp", yp", UT1-TAI s, dX", dY"


def instants():
    fixed = [
        (1962, 2, 20, 14, 47, 39.0, "Friendship 7 launch"),
        (1966, 3, 16, 16, 41, 2.0, "Gemini 8 launch"),
        (1968, 12, 21, 12, 51, 0.0, "Apollo 8 launch"),
        (1969, 7, 16, 13, 32, 0.0, "Apollo 11 launch"),
        (1969, 7, 24, 16, 50, 35.0, "Apollo 11 splashdown"),
        (1990, 1, 1, 0, 0, 0.0, "EOP node"),
        (2000, 1, 1, 12, 0, 0.0, "near J2000"),
        (2024, 6, 6, 12, 50, 0.0, "Starship Flight 4 launch"),
    ]
    rng = random.Random(1969716)
    start = datetime.datetime(1962, 1, 10)
    span = (datetime.datetime(2026, 6, 25) - start).total_seconds()
    for _ in range(120):
        t = start + datetime.timedelta(seconds=rng.uniform(0.0, span))
        fixed.append((t.year, t.month, t.day, t.hour, t.minute,
                      round(t.second + t.microsecond * 1e-6, 6), "random"))
    return fixed


def main():
    spiceypy.furnsh(str(CACHE / "naif_lsk" / "naif0012.tls"))
    spiceypy.furnsh(str(CACHE / "naif_earth_bpc" / "earth_620120_260806.bpc"))
    c04 = load_c04()
    rb, _, _ = erfa.bp06(2451545.0, 0.0)
    rows = []
    for year, month, day, hour, minute, second, note in instants():
        u1, u2 = erfa.dtf2d("UTC", year, month, day, hour, minute, second)
        a1, a2 = erfa.utctai(u1, u2)
        t1, t2 = erfa.taitt(a1, a2)
        mjd_utc = (u1 - 2400000.5) + u2
        xp, yp, ut1_tai, dx, dy = eop_at(c04, mjd_utc)
        b1, b2 = a1, a2 + ut1_tai / 86400.0
        x, y, s = erfa.xys06a(t1, t2)
        rc2i = erfa.c2ixys(x + dx * ARCSEC, y + dy * ARCSEC, s)
        era = erfa.era00(b1, b2)
        rpom = erfa.pom00(xp * ARCSEC, yp * ARCSEC, erfa.sp00(t1, t2))
        rc2t = rpom @ erfa.rz(era, np.eye(3)) @ rc2i
        c2t06a = erfa.c2t06a(t1, t2, b1, b2, xp * ARCSEC, yp * ARCSEC)
        row = {"utc": [year, month, day, hour, minute, second], "note": note,
               "erfa": rc2t.tolist(), "erfa_c2t06a": c2t06a.tolist()}
        if datetime.datetime(year, month, day) > datetime.datetime(1962, 1, 21):
            # SPICE's own UTC treats every instant before 1972 as TAI - UTC = 9 s (its kernel
            # starts there), 7 s wrong in 1962; hand it ERFA's TDB instead.
            et = ((t1 - 2451545.0) + t2) * 86400.0 + erfa.dtdb(t1, t2, (u1 - 0.5) % 1.0 + u2, 0.0, 0.0, 0.0)
            row["spice"] = np.array(spiceypy.pxform("J2000", "ITRF93", et)).tolist()
        rows.append(row)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({
        "generator": "tools/envdata/fixtures/earth_orientation.py",
        "oracle": f"pyerfa {erfa.__version__}: eraXys06a, eraC2ixys, eraEra00, eraPom00, eraSp00, eraC2t06a",
        "independent": f"spiceypy {spiceypy.__version__} pxform J2000->ITRF93 with earth_620120_260806.bpc",
        "frame_bias_gcrs_to_j2000": rb.tolist(),
        "generated": datetime.date.today().isoformat(),
        "instants": rows,
    }, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} instants")


if __name__ == "__main__":
    main()
