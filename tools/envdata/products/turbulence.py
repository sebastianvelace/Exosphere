"""Atmospheric turbulence statistics for gust simulation (environment plan E4.4).

From NASA/TM-2008-215633, "Terrestrial Environment (Climatic) Criteria Guidelines for Use in
Aerospace Vehicle Development, 2008 Revision" (D.L. Johnson, ed., MSFC), section 2.3.13.5: the
von Karman multimission turbulence model. Both tables are parsed from the pinned PDF with
pdftotext (poppler), nothing is transcribed by hand:

* Table 2-71 (from Justus et al., NASA TM-4168, 1990): standard deviations of horizontal and
  vertical turbulence for light, moderate and severe turbulence and the horizontal and vertical
  length scales, 1-200 km MSL. The handbook (sec. 2.3.6.x) notes the table caption's
  "magnitudes" are standard deviations.
* Table 2-70: boundary layer 10-1000 m, severe standard deviations and the integral scales
  (all intensities) of the longitudinal, lateral and vertical components. Its 1000 m row equals
  Table 2-71's 1 km severe row, so the two join without a seam. Double entries (step changes
  at 304.8 and 762 m) become two rows 1 mm apart.

Derived, not published (recorded in the provenance uncertainty): below 1 km the horizontal
deviation is the energy mean sqrt((s1^2+s2^2)/2) of the longitudinal and lateral columns and the
horizontal scale their mean; light and moderate deviations below 1 km scale the severe profile
by their 1 km ratio to severe.
"""
from __future__ import annotations

import os
import re
import subprocess
import tempfile

PAGES = (180, 181)  # PDF pages holding Tables 2-70 and 2-71 in the pinned file
NUMBER = r"[-+]?\d[\d,]*\.?\d*"


def _text(pdf: bytes) -> str:
    with tempfile.TemporaryDirectory(prefix="envdata_turb_") as folder:
        path = os.path.join(folder, "tm.pdf")
        with open(path, "wb") as handle:
            handle.write(pdf)
        return subprocess.run(["pdftotext", "-layout", "-f", str(PAGES[0]), "-l", str(PAGES[1]), path, "-"],
                              capture_output=True, text=True, check=True).stdout


def _value(token: str) -> float:
    return float(token.replace(",", ""))


def _table_270(text: str) -> list[tuple]:
    """(height m, s1, s2, s3, L1, L2, L3), double entries split into lower/upper rows."""
    start = text.index("Table 2-70.")
    end = text.index("*Double entries", start)
    rows = []
    for line in text[start:end].splitlines():
        tokens = line.split()
        if len(tokens) != 8 or not re.fullmatch(NUMBER, tokens[0]):
            continue
        height = _value(tokens[0])
        cells = [t.rstrip("*").split("/") for t in tokens[2:]]
        lower = [_value(c[0]) for c in cells]
        rows.append((height, *lower))
        if any(len(c) == 2 for c in cells):
            rows.append((height + 1e-3, *[_value(c[-1]) for c in cells]))
    heights = [r[0] for r in rows]
    if heights[0] != 10.0 or heights[-1] != 1000.0 or len(rows) != 22:
        raise ValueError(f"Table 2-70 parse: {len(rows)} rows from {heights[:1]} to {heights[-1:]}")
    return rows


def _table_271(text: str) -> list[tuple]:
    """(altitude km, light sh, sw, moderate sh, sw, severe sh, sw, Lh km, Lw km)."""
    start = text.index("Table 2-71.")
    rows = []
    for line in text[start:].splitlines():
        tokens = line.split()
        if len(tokens) != 13 or not all(re.fullmatch(NUMBER, t) for t in tokens):
            continue
        v = [_value(t) for t in tokens]
        # km, kft, light sh sw p, moderate sh sw p, severe sh sw p, Lh, Lw
        rows.append((v[0], v[2], v[3], v[5], v[6], v[8], v[9], v[11], v[12]))
    altitudes = [r[0] for r in rows]
    if altitudes[0] != 1.0 or altitudes[-1] != 200.0 or len(rows) != 31:
        raise ValueError(f"Table 2-71 parse: {len(rows)} rows from {altitudes[:1]} to {altitudes[-1:]}")
    return rows


def build_series(product_id: str, sources: dict):
    if product_id != "turbulence_msfc2008":
        raise KeyError(product_id)
    text = _text(sources["nasa_tm2008_215633"])
    boundary = _table_270(text)
    free = _table_271(text)
    top = boundary[-1]
    first = free[0]
    # The handbook's two tables meet at 1 km: severe sigma and both scales agree exactly.
    if (top[1], top[3], top[4], top[6]) != (first[5], first[6], first[7] * 1000.0, first[8] * 1000.0):
        raise ValueError("Tables 2-70 (1000 m) and 2-71 (1 km severe) do not join")
    ratio = [(first[1] / first[5], first[2] / first[6]), (first[3] / first[5], first[4] / first[6])]
    rows = []
    for height, s1, s2, s3, l1, l2, l3 in boundary[:-1]:  # the 1000 m row is Table 2-71's first
        sh = ((s1 * s1 + s2 * s2) / 2.0) ** 0.5
        rows.append([height, sh * ratio[0][0], s3 * ratio[0][1], sh * ratio[1][0], s3 * ratio[1][1],
                     sh, s3, (l1 + l2) / 2.0, l3])
    for km, lh, lw, mh, mw, sh, sw, length_h, length_w in free:
        rows.append([km * 1000.0, lh, lw, mh, mw, sh, sw, length_h * 1000.0, length_w * 1000.0])
    header = {"product": product_id,
              "description": "von Karman turbulence statistics, NASA/TM-2008-215633 Tables 2-70 (10-1000 m) "
                             "and 2-71 (1-200 km); light, moderate and severe standard deviations",
              "frame": "local horizontal/vertical", "time_scale": "none",
              "columns": ["altitude_m", "sigma_h_light", "sigma_w_light", "sigma_h_moderate", "sigma_w_moderate",
                          "sigma_h_severe", "sigma_w_severe", "length_h_m", "length_w_m"],
              "units": ["m", "m/s", "m/s", "m/s", "m/s", "m/s", "m/s", "m", "m"]}
    return header, rows
