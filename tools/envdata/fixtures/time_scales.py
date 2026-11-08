#!/usr/bin/env python3
"""Generate the time-scale oracle fixture (environment plan E1.1).

    pip install -r tools/envdata/requirements.txt
    python3 tools/envdata/fixtures/time_scales.py

Writes ExosphereSimulation.Tests/Fixtures/environment/time_scales.json: for UTC instants from
1961 to 2035 (mission epochs, the 1961-1971 rate era, both sides of leap steps and random
instants), TAI, TT and TDB as seconds since J2000.0 in each scale, computed by ERFA (the IAU
SOFA library: eraUtctai, eraTaitt, and the full Fairhead-Bretagnon eraDtdb at the geocentre),
plus SPICE's ephemeris time (str2et with naif0012.tls) for information: SPICE uses a one-term
TDB model, so it is not an oracle at the microsecond level.
"""
import datetime
import json
import pathlib
import random

import erfa
import spiceypy

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "time_scales.json"
CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"


def seconds(d1: float, d2: float) -> float:
    """Seconds since J2000 from a two-part Julian date, without losing the fraction."""
    return ((d1 - 2451545.0) + d2) * 86400.0


def instants() -> list[tuple[int, int, int, int, int, float, str]]:
    fixed = [
        (1961, 1, 1, 0, 0, 0.0, "first UTC instant"),
        (1961, 5, 5, 14, 34, 13.0, "Freedom 7 launch (Mercury-Redstone 3)"),
        (1962, 2, 20, 14, 47, 39.0, "Friendship 7 launch (MA-6)"),
        (1966, 3, 16, 16, 41, 2.0, "Gemini 8 launch"),
        (1968, 1, 31, 23, 59, 59.5, "before the 1968-02-01 offset step"),
        (1968, 2, 1, 0, 0, 0.5, "after the 1968-02-01 offset step"),
        (1968, 12, 21, 12, 51, 0.0, "Apollo 8 launch"),
        (1969, 7, 16, 13, 32, 0.0, "Apollo 11 launch (SP-238)"),
        (1971, 12, 31, 23, 59, 59.0, "last rate-era second"),
        (1972, 1, 1, 0, 0, 0.0, "first integer-offset instant"),
        (2016, 12, 31, 23, 59, 59.0, "before the latest leap second"),
        (2017, 1, 1, 0, 0, 0.0, "after the latest leap second"),
        (2000, 1, 1, 11, 58, 55.816, "J2000.0 in UTC"),
    ]
    rng = random.Random(20261108)
    start = datetime.datetime(1961, 1, 2)
    span = (datetime.datetime(2035, 12, 31) - start).total_seconds()
    for _ in range(100):
        t = start + datetime.timedelta(seconds=rng.uniform(0.0, span))
        fixed.append((t.year, t.month, t.day, t.hour, t.minute,
                      round(t.second + t.microsecond * 1e-6, 6), "random"))
    return fixed


def main() -> None:
    spiceypy.furnsh(str(CACHE / "naif_lsk" / "naif0012.tls"))
    rows = []
    for year, month, day, hour, minute, second, note in instants():
        u1, u2 = erfa.dtf2d("UTC", year, month, day, hour, minute, second)
        a1, a2 = erfa.utctai(u1, u2)
        t1, t2 = erfa.taitt(a1, a2)
        fraction = (u1 - 0.5) % 1.0 + u2  # UT fraction of day; irrelevant at the geocentre
        dtdb = erfa.dtdb(t1, t2, fraction, 0.0, 0.0, 0.0)
        row = {
            "utc": [year, month, day, hour, minute, second],
            "note": note,
            "tai_s": seconds(a1, a2),
            "tt_s": seconds(t1, t2),
            "tdb_s": seconds(t1, t2) + dtdb,
        }
        if year >= 1972:
            stamp = f"{year:04d}-{month:02d}-{day:02d}T{hour:02d}:{minute:02d}:{second:09.6f}"
            row["spice_et_s"] = spiceypy.str2et(stamp)
        rows.append(row)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({
        "generator": "tools/envdata/fixtures/time_scales.py",
        "oracle": f"pyerfa {erfa.__version__} (ERFA/SOFA eraDtf2d, eraUtctai, eraTaitt, eraDtdb geocentric)",
        "information": f"spiceypy {spiceypy.__version__} str2et with naif0012.tls (one-term TDB model)",
        "generated": datetime.date.today().isoformat(),
        "instants": rows,
    }, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} instants")


if __name__ == "__main__":
    main()
