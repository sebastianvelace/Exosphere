#!/usr/bin/env python3
"""Convert a global ETOPO GeoTIFF into the bounded Godot height texture."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

import numpy as np
from PIL import Image


OUTPUT_SIZE = (4096, 2048)
ELEVATION_MIN_M = -11000.0
ELEVATION_MAX_M = 9000.0


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def inspect(source: Path) -> Image.Image:
    Image.MAX_IMAGE_PIXELS = None
    image = Image.open(source)
    print(f"SOURCE_SIZE={image.width}x{image.height}")
    print(f"SOURCE_MODE={image.mode}")
    return image


def convert(source: Path, output: Path) -> None:
    image = inspect(source)
    if image.width < OUTPUT_SIZE[0] or image.height < OUTPUT_SIZE[1]:
        raise ValueError(
            f"source raster {image.width}x{image.height} is smaller than {OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]}"
        )

    reduced = image.resize(OUTPUT_SIZE, Image.Resampling.BILINEAR)
    values = np.asarray(reduced, dtype=np.float32)
    if not np.isfinite(values).all():
        raise ValueError("source raster contains non-finite elevation values")

    observed_min = float(values.min())
    observed_max = float(values.max())
    encoded = np.clip(
        (values - ELEVATION_MIN_M)
        * (65535.0 / (ELEVATION_MAX_M - ELEVATION_MIN_M)),
        0.0,
        65535.0,
    ).astype(np.uint16)

    output.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(encoded, mode="I;16").save(output, format="PNG", optimize=True)
    print(f"OBSERVED_RANGE_M={observed_min:.3f},{observed_max:.3f}")
    print(f"OUTPUT_SIZE={OUTPUT_SIZE[0]}x{OUTPUT_SIZE[1]}")
    print(f"OUTPUT_SHA256={sha256(output)}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--check-only", action="store_true")
    args = parser.parse_args()

    if not args.source.is_file():
        parser.error(f"source GeoTIFF not found: {args.source}")

    print(f"SOURCE_SHA256={sha256(args.source)}")
    if args.check_only:
        inspect(args.source)
        print("EARTH_RELIEF_CHECK=PASS")
        return 0

    convert(args.source, args.output)
    print(f"OUTPUT={args.output}")
    print("EARTH_RELIEF_CONVERSION=PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
