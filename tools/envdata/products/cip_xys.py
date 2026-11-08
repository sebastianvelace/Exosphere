"""Celestial Intermediate Pole coordinates X, Y and the CIO locator s from the IAU 2006
precession and IAU 2000A nutation, evaluated by ERFA (the IAU SOFA library, eraXys06a) on a
daily grid at 0h TT. The full series has ~1300 terms per coordinate; tabulating it lets the
simulation interpolate (8-point Lagrange) instead of summing it every physics step. The shortest
nutation terms with microarcsecond amplitude have periods of ~5 days, so a daily grid keeps the
interpolation error far below the model's own accuracy (tested in the C# suite against ERFA).

Pure model, no observations: the grid spans 1960-2051 regardless of the EOP cut-off. Observed
corrections dX, dY come from the EOP product.
"""
from __future__ import annotations

FIRST_MJD_TT = 36934.0   # 1960-01-01
LAST_MJD_TT = 69807.0    # 2051-12-31


def build(sources: dict) -> tuple[dict, list[list[float]]]:
    import erfa  # pinned in tools/envdata/requirements.txt

    rows = []
    mjd = FIRST_MJD_TT
    while mjd <= LAST_MJD_TT:
        x, y, s = erfa.xys06a(2400000.5, mjd)
        rows.append([mjd, x, y, s])
        mjd += 1.0
    header = {
        "product": "cip_xys",
        "description": "CIP X, Y and CIO locator s (rad), IAU 2006/2000A via ERFA eraXys06a, daily at 0h TT",
        "time_scale": "TT",
        "frame": "GCRS",
        "model": f"IAU 2006 precession + IAU 2000A nutation, pyerfa {erfa.__version__}",
        "valid_from_mjd_tt": FIRST_MJD_TT,
        "valid_to_mjd_tt": LAST_MJD_TT,
        "columns": ["mjd_tt", "x_rad", "y_rad", "s_rad"],
        "units": ["d", "rad", "rad", "rad"],
    }
    return header, rows
