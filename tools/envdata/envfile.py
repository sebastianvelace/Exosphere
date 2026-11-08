"""Deterministic binary container for environment products (data/environment/**/*.bin).

Layout (all little-endian):

    0   8 bytes   magic b"EXOENV01"
    8   u32       header length H in bytes (UTF-8 JSON, space-padded)
    12  H bytes   canonical JSON header (sorted keys, no insignificant whitespace)
    12+H          payload: rows x columns float64, row-major

The header is padded so the payload starts on an 8-byte boundary. It records the product id,
format version, frame, time scale, validity interval, column names and units, the SHA-256 of
every source file and of the payload, so a reader can refuse a product built from something
other than what the manifest declares. Writing the same inputs twice gives the same bytes.
"""
from __future__ import annotations

import hashlib
import json
import struct

MAGIC = b"EXOENV01"
FORMAT_VERSION = 1


def canonical_json(value) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True, allow_nan=False)


def encode(header: dict, rows: list[list[float]]) -> bytes:
    columns = header["columns"]
    for row in rows:
        if len(row) != len(columns):
            raise ValueError(f"row has {len(row)} values, header declares {len(columns)} columns")
    payload = b"".join(struct.pack("<" + "d" * len(row), *row) for row in rows)
    full = dict(header)
    full["format_version"] = FORMAT_VERSION
    full["rows"] = len(rows)
    full["payload_sha256"] = hashlib.sha256(payload).hexdigest()
    text = canonical_json(full).encode("ascii")
    padding = (-(len(MAGIC) + 4 + len(text))) % 8
    text += b" " * padding
    return MAGIC + struct.pack("<I", len(text)) + text + payload


def decode(blob: bytes) -> tuple[dict, list[tuple[float, ...]]]:
    if blob[:8] != MAGIC:
        raise ValueError("not an Exosphere environment product (bad magic)")
    (length,) = struct.unpack_from("<I", blob, 8)
    header = json.loads(blob[12:12 + length].decode("ascii"))
    if header.get("format_version") != FORMAT_VERSION:
        raise ValueError(f"unsupported format version {header.get('format_version')}")
    payload = blob[12 + length:]
    if hashlib.sha256(payload).hexdigest() != header["payload_sha256"]:
        raise ValueError("payload SHA-256 does not match its header")
    width = len(header["columns"])
    if len(payload) != 8 * width * header["rows"]:
        raise ValueError("payload size does not match rows x columns")
    rows = [struct.unpack_from("<" + "d" * width, payload, 8 * width * i) for i in range(header["rows"])]
    return header, rows
