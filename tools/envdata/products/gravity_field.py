"""Spherical-harmonic gravity fields truncated for the simulation (environment plan E3.3).

Rows (n, m, C̄nm, S̄nm, σC, σS), 4pi fully normalized, through the product's degree; the header
carries GM, the reference radius, the tide system and the body-fixed frame the coefficients are
defined in. The simulation picks its evaluation degree at run time (<= the product's).

* grgm1200a: GRAIL GRGM1200A (Goossens et al. 2016, PDS gggrx_1200a_sha.tab), lunar principal
  axes frame of DE430, degree 120.
* egm2008: EGM2008 (Pavlis et al. 2012, ICGEM gfc), tide-free, ITRF, degree 70.
"""
from __future__ import annotations

FIELDS = {
    "grgm1200a": ("grail_grgm1200a", 120),
    "egm2008": ("icgem_egm2008", 70),
}


def _rows_grail(text: str, degree: int):
    lines = text.splitlines()
    head = [v.strip() for v in lines[0].split(",")]
    radius_m, gm = float(head[0]) * 1000.0, float(head[1]) * 1e9
    rows = [[0.0, 0.0, 1.0, 0.0, 0.0, 0.0]]
    for line in lines[1:]:
        f = [v.strip() for v in line.split(",")]
        n, m = int(f[0]), int(f[1])
        if n > degree:
            break
        rows.append([float(n), float(m), float(f[2]), float(f[3]), float(f[4]), float(f[5])])
    meta = {"gm_m3s2": gm, "reference_radius_m": radius_m, "tide_system": "not stated (lunar)",
            "frame": "MOON_PA_DE430 (lunar principal axes of DE430)"}
    return meta, rows


def _rows_gfc(text: str, degree: int):
    meta, rows, body = {}, [], False
    for line in text.splitlines():
        if not body:
            parts = line.split()
            if parts and parts[0] == "earth_gravity_constant":
                meta["gm_m3s2"] = float(parts[1])
            elif parts and parts[0] == "radius":
                meta["reference_radius_m"] = float(parts[1])
            elif parts and parts[0] == "tide_system":
                meta["tide_system"] = parts[1]
            elif parts and parts[0] == "norm" and parts[1] != "fully_normalized":
                raise ValueError("expected fully normalized coefficients")
            body = line.startswith("end_of_head")
            continue
        f = line.replace("d", "e").replace("D", "e").split()
        if not f or f[0] != "gfc":
            continue
        n, m = int(f[1]), int(f[2])
        if n > degree:
            break
        rows.append([float(n), float(m), float(f[3]), float(f[4]), float(f[5]), float(f[6])])
    meta["frame"] = "ITRF (Earth-fixed)"
    return meta, rows


def build_series(field: str, sources: dict):
    dataset, degree = FIELDS[field]
    text = sources[dataset].decode("ascii")
    meta, rows = (_rows_grail if field == "grgm1200a" else _rows_gfc)(text, degree)
    # Complete, ordered triangle n = 0..degree (EGM2008 omits degree 1).
    present = {(int(r[0]), int(r[1])): r for r in rows}
    full = []
    for n in range(degree + 1):
        for m in range(n + 1):
            full.append(present.get((n, m), [float(n), float(m), 0.0, 0.0, 0.0, 0.0]))
    if full[0][2] != 1.0:
        raise ValueError("C00 must be 1")
    header = {"product": field, "degree": degree, "time_scale": "none",
              "columns": ["n", "m", "c", "s", "sigma_c", "sigma_s"],
              "units": ["", "", "", "", "", ""], **meta}
    return header, full
