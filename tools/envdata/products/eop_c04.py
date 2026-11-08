"""Daily Earth orientation parameters from the IERS EOP 20 C04 series (Paris Observatory),
consistent with ITRF2020 and the IAU 2000/2006 precession-nutation, sampled at 0h UTC.

Output row per day: MJD (UTC), polar motion x_p and y_p (arcsec), UT1 - UTC (s), length of day
excess (s), the celestial pole offsets dX and dY (arcsec, zero before VLBI in the 1980s), and
the published 1-sigma uncertainties of x_p, y_p and UT1 - UTC, so a dated mission can state the
orientation error of its epoch (0.02" and 1.5 ms in 1969, tens of microarcseconds today).
"""
from __future__ import annotations

COLUMNS = ["mjd_utc", "xp_arcsec", "yp_arcsec", "ut1_minus_utc_s", "lod_s", "dx_arcsec", "dy_arcsec",
           "xp_sigma_arcsec", "yp_sigma_arcsec", "ut1_sigma_s"]


def parse(text: str) -> list[list[float]]:
    rows = []
    for line in text.splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        f = line.split()
        if len(f) != 21:
            raise ValueError(f"unexpected C04 line: {line!r}")
        mjd = float(f[4])
        if rows and mjd != rows[-1][0] + 1.0:
            raise ValueError(f"C04 is not a continuous daily series at MJD {mjd}")
        # columns: x y UT1-UTC dX dY xrt yrt LOD | x_err y_err UT1_err ...
        rows.append([mjd, float(f[5]), float(f[6]), float(f[7]), float(f[12]), float(f[8]), float(f[9]),
                     float(f[13]), float(f[14]), float(f[15])])
    return rows


def build(sources: dict) -> tuple[dict, list[list[float]]]:
    rows = parse(sources["iers_eop_c04"].decode("ascii"))
    if rows[0][0] != 37665.0:
        raise ValueError("C04 must start at 1962-01-01 (MJD 37665)")
    header = {
        "product": "eop_c04",
        "description": "IERS EOP 20 C04 daily at 0h UTC; interpolate UT1-TAI, not UT1-UTC, across leap steps",
        "time_scale": "UTC",
        "frame": "ITRF2020 / IAU 2006-2000A",
        "valid_from_mjd_utc": rows[0][0],
        "valid_to_mjd_utc": rows[-1][0],
        "columns": COLUMNS,
        "units": ["d", "arcsec", "arcsec", "s", "s", "arcsec", "arcsec", "arcsec", "arcsec", "s"],
    }
    return header, rows
