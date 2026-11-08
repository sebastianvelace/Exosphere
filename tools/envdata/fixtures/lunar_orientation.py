#!/usr/bin/env python3
"""Generate the lunar orientation oracle (environment plan E3.1).

    python3 tools/envdata/fixtures/lunar_orientation.py

ExosphereSimulation.Tests/Fixtures/environment/lunar_orientation.json: at 80 instants 1960-2050
(span boundaries included) SPICE pxform J2000 -> MOON_PA_DE440 and the angular velocity of the
principal-axes frame in J2000 from sxform (w = skew part of dR^T/dt R), straight from the NAIF
binary PCK moon_pa_de440_200625.bpc.
"""
import datetime
import json
import pathlib

import numpy as np
import spiceypy as sp

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "lunar_orientation.json"
CACHE = pathlib.Path.home() / ".cache" / "exosphere" / "envdata"
FIRST = (2436934.5 - 2451545.0) * 86400.0
LAST = (2469807.5 - 2451545.0) * 86400.0


def main():
    sp.furnsh(str(CACHE / "naif_moon_pa_de440" / "moon_pa_de440_200625.bpc"))
    sp.furnsh(str(CACHE / "naif_moon_de440_tf" / "moon_de440_250416.tf"))
    rng = np.random.default_rng(301)
    times = [float(t) for t in rng.uniform(FIRST + 86400, LAST - 86400, 70)]
    times += [FIRST + 400 * 8 * 86400.0 + d for d in (-1e-3, 1e-3)] + [-962_536_000.0]
    rows = []
    for t in times:
        xform = np.array(sp.sxform("J2000", "MOON_PA_DE440", t))
        r, rdot = xform[:3, :3], xform[3:, :3]
        w = r.T @ rdot  # skew-symmetric: omega in J2000 axes (sign per SPICE xf2rav)
        rot, av = sp.xf2rav(xform)
        rows.append({"tdb_s": t, "icrf_to_pa": r.tolist(), "omega_icrf_rad_s": list(av)})
    OUT.write_text(json.dumps({"generator": "tools/envdata/fixtures/lunar_orientation.py",
                               "oracle": f"spiceypy {sp.__version__} sxform/xf2rav J2000 -> MOON_PA_DE440 (moon_pa_de440_200625.bpc)",
                               "generated": datetime.date.today().isoformat(), "instants": rows}, indent=1) + "\n")
    print(f"wrote {OUT.relative_to(ROOT)}: {len(rows)} instants")


if __name__ == "__main__":
    main()
