#!/usr/bin/env python3
"""Offline tests of the environment data pipeline (tools/envdata, plan E0.2)."""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "envdata"))

import envfile  # noqa: E402
from products import tai_utc  # noqa: E402


def fail(message: str) -> None:
    print(f"envdata_test: FAIL {message}", file=sys.stderr)
    sys.exit(1)


# Container round trip is exact and byte-deterministic.
header = {"product": "probe", "columns": ["a", "b"], "units": ["s", "m"], "sources": {}}
rows = [[1.0, -2.5], [3.0e300, 5e-324]]
blob = envfile.encode(header, rows)
if blob != envfile.encode(dict(header), [list(r) for r in rows]):
    fail("encoding is not deterministic")
decoded_header, decoded_rows = envfile.decode(blob)
if [list(r) for r in decoded_rows] != rows or decoded_header["rows"] != 2:
    fail("round trip changed the payload")
if (12 + int.from_bytes(blob[8:12], "little")) % 8:
    fail("payload is not 8-byte aligned")
corrupt = bytearray(blob)
corrupt[-1] ^= 1
try:
    envfile.decode(bytes(corrupt))
    fail("a corrupted payload was accepted")
except ValueError:
    pass

# Calendar conversion: J2000.0 is JD 2451545.0 at 12h, so 0h is 2451544.5.
if tai_utc.julian_day_number(2000, 1, 1) != 2451544.5:
    fail("Julian day of 2000-01-01")
if tai_utc.julian_day_number(1969, 7, 16) != 2440418.5:
    fail("Julian day of 1969-07-16")

# The pinned product: Apollo 11 launch (1969-07-16 13:32 UTC) falls in the 1968 FEB 1 rate row.
product = ROOT / "data" / "environment" / "time" / "tai_utc.bin"
header, table = envfile.decode(product.read_bytes())
jd = 2440418.5 + (13 * 3600 + 32 * 60) / 86400.0
row = max((r for r in table if r[0] <= jd), key=lambda r: r[0])
if row[0] != 2439887.5 or row[3] != 0.002592:
    fail(f"Apollo 11 epoch resolves to the wrong row {row}")
offset = row[1] + (jd - 2400000.5 - row[2]) * row[3]
if not 7.56 < offset < 7.57:
    fail(f"TAI-UTC at the Apollo 11 launch is {offset}")
if table[-1][1] != 37.0:
    fail("latest leap second should give TAI-UTC = 37 s")

# The offline contract itself.
result = subprocess.run([sys.executable, str(ROOT / "tools" / "envdata" / "envdata.py"), "verify"],
                        capture_output=True, text=True)
if result.returncode != 0:
    fail(result.stderr.strip())

print(f"envdata_test: PASS (container, calendar, TAI-UTC {offset:.4f} s at the Apollo 11 launch, verify)")
