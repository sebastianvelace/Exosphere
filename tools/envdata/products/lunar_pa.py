"""Lunar principal-axes orientation from DE440 (environment plan E3.1).

The NAIF binary PCK moon_pa_de440_200625.bpc holds DE440's integrated lunar librations. The
product fits the 3-1-3 Euler angles (phi, theta, psi) of the ICRF -> MOON_PA_DE440 rotation,
R = R3(psi) R1(theta) R3(phi), with psi unwrapped, as piecewise Chebyshev series (8 d / 12) in
TDB. The build checks the rebuilt matrix against SPICE pxform at random instants; the
interpolation error is stated as a displacement on the lunar surface.
"""
from __future__ import annotations

import math
import os
import tempfile

import numpy as np

FIRST_TDB_S = (2436934.5 - 2451545.0) * 86400.0
LAST_TDB_S = (2469807.5 - 2451545.0) * 86400.0
SPAN_DAYS, DEGREE = 8, 12
MOON_RADIUS_M = 1737400.0


def build(sources: dict):
    import spiceypy as sp

    folder = tempfile.mkdtemp(prefix="envdata_moon_")
    for name in ("naif_moon_pa_de440", "naif_moon_de440_tf"):
        dataset = sources["__datasets__"][name]
        path = os.path.join(folder, dataset["file"])
        with open(path, "wb") as handle:
            handle.write(sources[name])
        sp.furnsh(path)

    def angles(t):
        m = np.array(sp.pxform("J2000", "MOON_PA_DE440", t))
        psi, theta, phi = sp.m2eul(m, 3, 1, 3)
        return np.array([phi, theta, psi])

    span = SPAN_DAYS * 86400.0
    count = int(math.ceil((LAST_TDB_S - FIRST_TDB_S) / span))
    k = np.arange(DEGREE + 1)
    nodes = np.cos(np.pi * k / DEGREE)
    rows, segments = [], []
    for i in range(count):
        a, b = FIRST_TDB_S + i * span, FIRST_TDB_S + (i + 1) * span
        times = a + (nodes + 1.0) * (b - a) / 2.0
        values = np.array([angles(t) for t in times])
        values = np.unwrap(values, axis=0)
        c = np.polynomial.chebyshev.chebfit(nodes, values, DEGREE)
        segments.append((a, b, c))
        rows.append([a, b, *c[:, 0], *c[:, 1], *c[:, 2]])

    rng = np.random.default_rng(301)
    worst = 0.0
    for _ in range(2000):
        i = int(rng.integers(0, count))
        a, b, c = segments[i]
        x = rng.uniform(-1.0, 1.0)
        t = a + (x + 1.0) * (b - a) / 2.0
        phi, theta, psi = np.polynomial.chebyshev.chebval(x, c)
        m = np.array(sp.eul2m(psi, theta, phi, 3, 1, 3))
        truth = np.array(sp.pxform("J2000", "MOON_PA_DE440", t))
        d = m @ truth.T
        # Rotation angle from the antisymmetric part: acos near 1 has a 2e-8 rad floor (3.7 cm here).
        sine = 0.5 * math.sqrt((d[2, 1] - d[1, 2]) ** 2 + (d[0, 2] - d[2, 0]) ** 2 + (d[1, 0] - d[0, 1]) ** 2)
        angle = math.atan2(sine, (np.trace(d) - 1.0) / 2.0)
        worst = max(worst, angle * MOON_RADIUS_M)
    if worst > 1e-3:
        raise ValueError(f"lunar orientation fit error {worst:.3e} m on the surface")
    sp.kclear()
    columns = ["t_start_tdb_s", "t_end_tdb_s"] + [f"{a}{j}" for a in ("phi", "theta", "psi") for j in range(DEGREE + 1)]
    header = {"product": "lunar_pa", "description": "ICRF -> MOON_PA_DE440 3-1-3 Euler angles, R = R3(psi) R1(theta) R3(phi)",
              "time_scale": "TDB", "frame": "ICRF -> MOON_PA_DE440", "degree": DEGREE, "span_days": SPAN_DAYS,
              "columns": columns, "units": ["s", "s"] + ["rad"] * (3 * (DEGREE + 1)),
              "verified_max_surface_error_m": worst}
    return header, rows
