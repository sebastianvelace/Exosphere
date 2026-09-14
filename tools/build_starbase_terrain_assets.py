#!/usr/bin/env python3
"""Build the checked-in Starbase regional terrain textures from official rasters.

The game consumes a compact orthoimage, a validity mask, and a 16-bit relative
height map. This tool keeps the lossy conversion explicit and reproducible while
leaving the original downloaded rasters outside the repository.
"""

from __future__ import annotations

import argparse
import json
import warnings
from datetime import date
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter


BBOX = {
    "west": -97.2066,
    "south": 25.9522,
    "east": -97.1066,
    "north": 26.0422,
}
HEIGHT_MIN_M = -2.0
HEIGHT_MAX_M = 12.0
REFERENCE_M = 0.96


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--naip", type=Path, required=True)
    parser.add_argument("--dem", type=Path, required=True)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    texture_dir = args.root / "assets" / "textures"
    data_dir = args.root / "data" / "launch_sites"
    texture_dir.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)

    ortho = Image.open(args.naip).convert("RGB")
    ortho.save(texture_dir / "starbase_naip_10km.jpg", quality=92, optimize=True)

    rgb = np.asarray(ortho, dtype=np.uint8)
    valid = np.any(rgb > 2, axis=2)
    mask = Image.fromarray(np.where(valid, 255, 0).astype(np.uint8), mode="L")
    # Feather only the provider's no-data boundary. This avoids a hard black
    # rectangle while the shader applies its physical regional edge fade.
    mask = mask.filter(ImageFilter.GaussianBlur(radius=6))
    mask.save(texture_dir / "starbase_naip_10km_mask.png", optimize=True)

    dem_image = Image.open(args.dem)
    # PIL's NumPy bridge can emit a cast warning for provider nodata values in
    # float TIFFs. Reading the native little-endian float buffer preserves them
    # exactly so the finite/range mask below can reject them deterministically.
    if dem_image.mode != "F":
        raise RuntimeError(f"expected a float32 DEM, got {dem_image.mode}")
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", RuntimeWarning)
        dem_bytes = dem_image.tobytes()
    dem = np.frombuffer(dem_bytes, dtype="<f4").reshape(dem_image.height, dem_image.width)
    with np.errstate(invalid="ignore"):
        finite = np.isfinite(dem)
    valid_dem = finite & (dem > -5.0) & (dem < 30.0)
    if valid_dem.mean() < 0.80:
        raise RuntimeError(f"3DEP valid coverage unexpectedly low: {valid_dem.mean():.3f}")
    clipped = np.clip(np.where(valid_dem, dem, REFERENCE_M), HEIGHT_MIN_M, HEIGHT_MAX_M)
    encoded = np.round(
        (clipped - HEIGHT_MIN_M) / (HEIGHT_MAX_M - HEIGHT_MIN_M) * 65535.0
    ).astype(np.uint16)
    # The dynamic mosaic can contain isolated sub-pixel tile seams and void
    # speckles. The runtime uses this only for broad landform displacement, so
    # a 7-pixel median (about 35 m at this export) removes those artifacts while
    # preserving dunes, shoreline and the low coastal relief.
    encoded_image = Image.fromarray(encoded.astype(np.int32), mode="I").filter(ImageFilter.MedianFilter(7))
    encoded = np.asarray(encoded_image, dtype=np.uint16)
    Image.fromarray(encoded, mode="I;16").save(
        texture_dir / "starbase_3dep_10km_height.png", optimize=True
    )

    metadata = {
        "schema": 1,
        "site": "Starbase / Boca Chica",
        "center": {"latitude": 25.9972, "longitude": -97.1566},
        "bbox_wgs84": BBOX,
        "projection": "EPSG:4326 source export; sampled in local east/north tangent metres",
        "ortho": {
            "file": "assets/textures/starbase_naip_10km.jpg",
            "mask_file": "assets/textures/starbase_naip_10km_mask.png",
            "width": ortho.width,
            "height": ortho.height,
            "source": "USDA NAIP CONUS ImageServer",
            "source_url": "https://apps.geo.fpac.usda.gov/geo-imagery/rest/services/naip/conus_naip/ImageServer",
            "license": "USDA public service; attribution retained for provenance",
        },
        "elevation": {
            "file": "assets/textures/starbase_3dep_10km_height.png",
            "width": int(encoded.shape[1]),
            "height": int(encoded.shape[0]),
            "source": "USGS 3DEP Elevation ImageServer",
            "source_url": "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer",
            "license": "USGS public domain",
            "encoding": "uint16 normalized absolute NAVD88 metres",
            "height_min_m": HEIGHT_MIN_M,
            "height_max_m": HEIGHT_MAX_M,
            "reference_m": REFERENCE_M,
            "valid_input_fraction": round(float(valid_dem.mean()), 6),
        },
        "prepared_on": str(date.today()),
        "runtime_extent_m": 10000.0,
        "notes": [
            "NAIP provides observed surface reflectance for the low-altitude regional ground.",
            "3DEP is bare-earth elevation and is used only for broad displacement.",
            "Invalid provider pixels are masked and filled at the launch-site reference height.",
        ],
    }
    (data_dir / "starbase_terrain.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps({
        "ortho": [ortho.width, ortho.height],
        "dem": [int(encoded.shape[1]), int(encoded.shape[0])],
        "valid_dem_fraction": round(float(valid_dem.mean()), 6),
        "height_range_m": [HEIGHT_MIN_M, HEIGHT_MAX_M],
        "reference_m": REFERENCE_M,
    }))


if __name__ == "__main__":
    main()
