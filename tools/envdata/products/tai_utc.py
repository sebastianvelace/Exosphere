"""TAI - UTC table, including the 1961-1971 rate terms that dated Mercury/Gemini/Apollo
missions need (the NAIF leap-second kernel starts only in 1972).

Source row (USNO tai-utc.dat):

    1968 FEB  1 =JD 2439887.5  TAI-UTC=   4.2131700 S + (MJD - 39126.) X 0.002592 S

meaning: from UTC Julian date 2439887.5 on, TAI - UTC = 4.2131700 + (MJD_UTC - 39126) * 0.002592 s.
Each output row is (jd_utc_start, offset_s, mjd_reference, rate_s_per_day).

Cross-check: for every leap second since 1972 the table must equal DELTET/DELTA_AT in the NAIF
kernel naif0012.tls, an independent distribution of the same IERS Bulletin C decisions.
"""
from __future__ import annotations

import re

ROW = re.compile(
    r"^\s*(\d{4})\s+([A-Z]{3})\s+(\d+)\s*=JD\s*([\d.]+)\s+TAI-UTC=\s*([\d.]+)\s*S\s*\+\s*\(MJD\s*-\s*([\d.]+)\s*\)\s*X\s*([\d.]+)\s*S\s*$")
MONTHS = {m: i + 1 for i, m in enumerate("JAN FEB MAR APR MAY JUN JUL AUG SEP OCT NOV DEC".split())}


def julian_day_number(year: int, month: int, day: int) -> float:
    """Julian date at 0h of a Gregorian calendar date (Fliegel & Van Flandern, 1968)."""
    a = (14 - month) // 12
    y = year + 4800 - a
    m = month + 12 * a - 3
    jdn = day + (153 * m + 2) // 5 + 365 * y + y // 4 - y // 100 + y // 400 - 32045
    return jdn - 0.5


def parse_usno(text: str) -> list[list[float]]:
    rows = []
    for line in text.splitlines():
        if not line.strip():
            continue
        match = ROW.match(line)
        if not match:
            raise ValueError(f"unrecognised tai-utc.dat line: {line!r}")
        year, month, day, jd, offset, mjd_ref, rate = match.groups()
        jd = float(jd)
        if jd != julian_day_number(int(year), MONTHS[month], int(day)):
            raise ValueError(f"calendar date and JD disagree in {line!r}")
        rows.append([jd, float(offset), float(mjd_ref), float(rate)])
    if [r[0] for r in rows] != sorted(r[0] for r in rows):
        raise ValueError("tai-utc.dat rows are not in time order")
    return rows


def parse_naif_deltas(text: str) -> list[tuple[float, float]]:
    """(jd_utc, TAI-UTC) pairs from DELTET/DELTA_AT of a NAIF leap-second kernel."""
    block = re.search(r"DELTET/DELTA_AT\s*=\s*\((.*?)\)", text, re.S)
    if not block:
        raise ValueError("DELTET/DELTA_AT not found in the NAIF kernel")
    pairs = re.findall(r"(\d+)\s*,\s*@(\d{4})-([A-Z]{3})-(\d+)", block.group(1))
    return [(julian_day_number(int(y), MONTHS[m], int(d)), float(dat)) for dat, y, m, d in pairs]


def build(sources: dict[str, bytes]) -> tuple[dict, list[list[float]]]:
    rows = parse_usno(sources["usno_tai_utc"].decode("ascii"))
    naif = parse_naif_deltas(sources["naif_lsk"].decode("ascii"))
    stepped = [(r[0], r[1]) for r in rows if r[3] == 0.0]
    if stepped != naif:
        raise ValueError(f"USNO and NAIF leap seconds disagree:\nUSNO {stepped}\nNAIF {naif}")
    if rows[0][0] != 2437300.5:
        raise ValueError("table must start at 1961 JAN 1, the first UTC definition")
    header = {
        "product": "tai_utc",
        "description": "TAI - UTC = offset_s + (MJD_UTC - mjd_reference) * rate_s_per_day from jd_utc_start",
        "time_scale": "UTC",
        "frame": "none",
        "valid_from_jd_utc": rows[0][0],
        "columns": ["jd_utc_start", "offset_s", "mjd_reference", "rate_s_per_day"],
        "units": ["d", "s", "d", "s/d"],
        "cross_check": f"{len(naif)} leap seconds since 1972 equal NAIF DELTET/DELTA_AT",
    }
    return header, rows
