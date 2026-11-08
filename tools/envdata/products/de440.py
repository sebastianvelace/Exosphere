"""Dated ephemerides from JPL DE440 (environment plan E2.1).

Each product holds one series as piecewise Chebyshev polynomials in TDB: rows of
(t_start_s, t_end_s, x_0..x_n, y_0..y_n, z_0..z_n) with positions in metres in the ICRF
(SPICE "J2000"), times in TDB seconds since J2000. The coefficients interpolate DE440 at
Chebyshev-Lobatto nodes (span ends included, so position is continuous across spans); velocity
is the analytic derivative. The build verifies every series
against SPICE at random instants (position and velocity) and fails beyond its tolerance.

Series (target relative to centre), with span and degree from the measured prototypes:
the Moon about the Earth 8 d / 18, the Earth-Moon barycentre about the solar-system barycentre
16 d / 13, the Sun 32 d / 13, Mercury 8 d / 12, Venus 16 d / 12, Mars 32 d / 12, Jupiter and
Saturn system barycentres 64 d / 11. Earth is EMB − Moon·μ_M/(μ_E + μ_M) at run time, with the
DE440 GMs recorded in the Moon product's header.
"""
from __future__ import annotations

import math

import numpy as np

# TDB seconds since J2000 (JD 2451545.0) of 1960-01-01 0h (JD 2436934.5) and 2051-01-01 0h
# (JD 2469807.5): exact in seconds, no leap-second kernel needed.
FIRST_TDB_S = (2436934.5 - 2451545.0) * 86400.0
LAST_TDB_S = (2469807.5 - 2451545.0) * 86400.0

SERIES = {
    # product id: (target, centre, span days, degree, position tol m, velocity tol m/s)
    "de440_moon": ("MOON", "EARTH", 8, 18, 0.001, 1e-6),
    "de440_emb": ("EARTH MOON BARYCENTER", "SOLAR SYSTEM BARYCENTER", 16, 13, 0.02, 1e-6),
    "de440_sun": ("SUN", "SOLAR SYSTEM BARYCENTER", 32, 13, 0.1, 1e-6),
    "de440_mercury": ("MERCURY BARYCENTER", "SOLAR SYSTEM BARYCENTER", 8, 12, 1.0, 1e-4),
    "de440_venus": ("VENUS BARYCENTER", "SOLAR SYSTEM BARYCENTER", 16, 12, 1.0, 1e-4),
    "de440_mars": ("MARS BARYCENTER", "SOLAR SYSTEM BARYCENTER", 32, 12, 1.0, 1e-4),
    "de440_jupiter": ("JUPITER BARYCENTER", "SOLAR SYSTEM BARYCENTER", 64, 11, 1.0, 1e-4),
    "de440_saturn": ("SATURN BARYCENTER", "SOLAR SYSTEM BARYCENTER", 64, 11, 1.0, 1e-4),
}


def _spice(sources: dict):
    import os
    import tempfile

    import spiceypy

    folder = tempfile.mkdtemp(prefix="envdata_spice_")
    for name in ("naif_de440s", "naif_gm_de440"):
        dataset = sources["__datasets__"][name]
        path = os.path.join(folder, dataset["file"])
        with open(path, "wb") as handle:
            handle.write(sources[name])
        spiceypy.furnsh(path)
    return spiceypy


def build_series(product_id: str, sources: dict) -> tuple[dict, list[list[float]]]:
    sp = _spice(sources)
    target, centre, span_days, degree, pos_tol, vel_tol = SERIES[product_id]
    t0, t1 = FIRST_TDB_S, LAST_TDB_S
    span = span_days * 86400.0
    count = int(math.ceil((t1 - t0) / span))
    k = np.arange(degree + 1)
    # Chebyshev-Lobatto nodes include both span ends, so neighbouring spans interpolate the
    # same DE440 value at their shared boundary: position is continuous by construction.
    nodes = np.cos(np.pi * k / degree)

    def state(t):
        s, _ = sp.spkgeo(sp.bodn2c(target), t, "J2000", sp.bodn2c(centre))
        return np.array(s) * 1000.0

    rows, coefficients = [], []
    for i in range(count):
        a, b = t0 + i * span, t0 + (i + 1) * span
        times = a + (nodes + 1.0) * (b - a) / 2.0
        positions = np.array([state(t)[:3] for t in times])
        c = np.polynomial.chebyshev.chebfit(nodes, positions, degree)  # (degree+1, 3)
        coefficients.append((a, b, c))
        rows.append([a, b, *c[:, 0], *c[:, 1], *c[:, 2]])

    # Verification against SPICE: random instants, position and analytic velocity.
    rng = np.random.default_rng(440)
    worst_p = worst_v = 0.0
    for _ in range(2000):
        i = int(rng.integers(0, count))
        a, b, c = coefficients[i]
        x = rng.uniform(-1.0, 1.0)
        t = a + (x + 1.0) * (b - a) / 2.0
        truth = state(t)
        p = np.polynomial.chebyshev.chebval(x, c)
        v = np.polynomial.chebyshev.chebval(x, np.polynomial.chebyshev.chebder(c)) * 2.0 / (b - a)
        worst_p = max(worst_p, float(np.abs(p - truth[:3]).max()))
        worst_v = max(worst_v, float(np.abs(v - truth[3:]).max()))
    if worst_p > pos_tol or worst_v > vel_tol:
        raise ValueError(f"{product_id}: fit error {worst_p:.3e} m / {worst_v:.3e} m/s exceeds "
                         f"{pos_tol} m / {vel_tol} m/s")

    columns = ["t_start_tdb_s", "t_end_tdb_s"] + [f"{axis}{j}" for axis in "xyz" for j in range(degree + 1)]
    header = {
        "product": product_id,
        "description": f"{target} relative to {centre}, DE440, Chebyshev {span_days} d / degree {degree}",
        "target": target,
        "centre": centre,
        "time_scale": "TDB",
        "frame": "ICRF",
        "degree": degree,
        "span_days": span_days,
        "valid_from_tdb_s": t0,
        "valid_to_tdb_s": coefficients[-1][1],
        "columns": columns,
        "units": ["s", "s"] + ["m"] * (3 * (degree + 1)),
        "verified_max_position_error_m": worst_p,
        "verified_max_velocity_error_mps": worst_v,
    }
    if product_id == "de440_moon":
        header["gm_earth_m3s2"] = float(sp.bodvrd("EARTH", "GM", 1)[1][0]) * 1e9
        header["gm_moon_m3s2"] = float(sp.bodvrd("MOON", "GM", 1)[1][0]) * 1e9
    sp.kclear()
    return header, rows


def make_builder(product_id: str):
    return lambda sources: build_series(product_id, sources)
