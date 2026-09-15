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
MACRO_BBOX = {
    "west": -97.6566,
    "south": 25.4972,
    "east": -96.6566,
    "north": 26.4972,
}
HEIGHT_MIN_M = -2.0
HEIGHT_MAX_M = 12.0
REFERENCE_M = 0.96


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--naip", type=Path, required=True)
    parser.add_argument("--dem", type=Path, required=True)
    parser.add_argument("--naip-macro", type=Path)
    parser.add_argument("--dem-macro", type=Path)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    return parser.parse_args()


def prepare_ortho(source: Path, output: Path, mask_output: Path, feather_radius: int) -> Image.Image:
    ortho = Image.open(source).convert("RGB")
    rgb = np.asarray(ortho, dtype=np.uint8).copy()
    # A JPEG export of a provider void is not always byte-for-byte black at
    # the boundary; use a luminance-safe max-channel threshold so dark
    # compression fringes do not become repeated scanlines in the fill.
    source_valid = np.max(rgb, axis=2) > 32
    # JPEG encoding can leave isolated non-black scanlines inside a provider
    # void. Keep the longest contiguous observed run in each row; a real NAIP
    # strip is contiguous, while those isolated remnants are not usable data.
    cleaned_valid = np.zeros_like(source_valid)
    for row in range(source_valid.shape[0]):
        columns = np.flatnonzero(source_valid[row])
        if columns.size == 0:
            continue
        breaks = np.flatnonzero(np.diff(columns) > 1)
        starts = np.r_[0, breaks + 1]
        ends = np.r_[breaks, columns.size - 1]
        run = int(np.argmax(ends - starts + 1))
        cleaned_valid[row, columns[starts[run]]:columns[ends[run]] + 1] = True
    source_valid = cleaned_valid
    # Keep the measured pixels intact. Fill provider voids with one neutral
    # colour so texture filtering cannot pull a black scanline into the soft
    # boundary; the validity mask below prevents this fill from being shown as
    # measured geography.
    if not source_valid.any():
        raise RuntimeError(f"orthophoto has no usable pixels: {source}")
    fill_color = np.median(rgb[source_valid], axis=0).astype(np.uint8)
    rgb[~source_valid] = fill_color
    ortho = Image.fromarray(rgb, mode="RGB")
    ortho.save(output, quality=92, optimize=True)
    # Feather the observed footprint itself. This is distinct from the
    # geospatial edge fade in the shader: it hides provider tile boundaries and
    # lets the measured layer return smoothly to the procedural/Blue Marble
    # context wherever the source mosaic is absent.
    mask = Image.fromarray((source_valid.astype(np.uint8) * 255), mode="L")
    mask = mask.filter(ImageFilter.GaussianBlur(radius=feather_radius))
    mask.save(mask_output, optimize=True)
    return ortho


def prepare_dem(source: Path, output: Path) -> tuple[Image.Image, float]:
    dem_image = Image.open(source)
    if dem_image.mode != "F":
        raise RuntimeError(f"expected a float32 DEM, got {dem_image.mode}")
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", RuntimeWarning)
        dem_bytes = dem_image.tobytes()
    dem = np.frombuffer(dem_bytes, dtype="<f4").reshape(dem_image.height, dem_image.width)
    with np.errstate(invalid="ignore"):
        finite = np.isfinite(dem)
    valid_dem = finite & (dem > -5.0) & (dem < 30.0)
    if valid_dem.mean() < 0.60:
        raise RuntimeError(f"3DEP valid coverage unexpectedly low: {valid_dem.mean():.3f}")
    clipped = np.clip(np.where(valid_dem, dem, REFERENCE_M), HEIGHT_MIN_M, HEIGHT_MAX_M)
    encoded = np.round(
        (clipped - HEIGHT_MIN_M) / (HEIGHT_MAX_M - HEIGHT_MIN_M) * 65535.0
    ).astype(np.uint16)
    # The dynamic mosaic can contain isolated sub-pixel tile seams and void
    # speckles. The runtime uses this only for broad landform displacement.
    encoded_image = Image.fromarray(encoded.astype(np.int32), mode="I").filter(ImageFilter.MedianFilter(7))
    encoded = np.asarray(encoded_image, dtype=np.uint16)
    Image.fromarray(encoded, mode="I;16").save(output, optimize=True)
    return Image.open(output), float(valid_dem.mean())


def main() -> None:
    args = parse_args()
    texture_dir = args.root / "assets" / "textures"
    data_dir = args.root / "data" / "launch_sites"
    texture_dir.mkdir(parents=True, exist_ok=True)
    data_dir.mkdir(parents=True, exist_ok=True)

    ortho = prepare_ortho(
        args.naip,
        texture_dir / "starbase_naip_10km.jpg",
        texture_dir / "starbase_naip_10km_mask.png",
        feather_radius=192,
    )
    dem_10km_image, valid_dem_fraction = prepare_dem(
        args.dem, texture_dir / "starbase_3dep_10km_height.png"
    )

    macro_ortho = None
    macro_dem_fraction = None
    if (args.naip_macro is None) != (args.dem_macro is None):
        raise RuntimeError("--naip-macro and --dem-macro must be provided together")
    if args.naip_macro is not None and args.dem_macro is not None:
        macro_ortho = prepare_ortho(
            args.naip_macro,
            texture_dir / "starbase_naip_50km.jpg",
            texture_dir / "starbase_naip_50km_mask.png",
            # The macro source has a provider footprint smaller than its requested
            # bbox. A broad raster feather keeps that footprint from becoming a
            # visible diagonal at the 2–40 km camera distances.
            feather_radius=768,
        )
        _, macro_dem_fraction = prepare_dem(
            args.dem_macro, texture_dir / "starbase_3dep_50km_height.png"
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
            "width": dem_10km_image.width,
            "height": dem_10km_image.height,
            "source": "USGS 3DEP Elevation ImageServer",
            "source_url": "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer",
            "license": "USGS public domain",
            "encoding": "uint16 normalized absolute NAVD88 metres",
            "height_min_m": HEIGHT_MIN_M,
            "height_max_m": HEIGHT_MAX_M,
            "reference_m": REFERENCE_M,
            "valid_input_fraction": round(valid_dem_fraction, 6),
        },
        "prepared_on": str(date.today()),
        "runtime_extent_m": 10000.0,
        "notes": [
            "NAIP provides observed surface reflectance for the low-altitude regional ground.",
            "3DEP is bare-earth elevation and is used only for broad displacement.",
            "Invalid provider pixels are masked and filled at the launch-site reference height.",
        ],
    }
    if macro_ortho is not None:
        metadata["macro_bbox_wgs84"] = MACRO_BBOX
        metadata["macro_ortho"] = {
            "file": "assets/textures/starbase_naip_50km.jpg",
            "mask_file": "assets/textures/starbase_naip_50km_mask.png",
            "width": macro_ortho.width,
            "height": macro_ortho.height,
            "source": "USDA NAIP CONUS ImageServer",
            "source_url": "https://apps.geo.fpac.usda.gov/geo-imagery/rest/services/naip/conus_naip/ImageServer",
            "license": "USDA public service; attribution retained for provenance",
        }
        metadata["macro_elevation"] = {
            "file": "assets/textures/starbase_3dep_50km_height.png",
            "width": 2048,
            "height": 2048,
            "source": "USGS 3DEP Elevation ImageServer",
            "source_url": "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer",
            "license": "USGS public domain",
            "encoding": "uint16 normalized absolute NAVD88 metres",
            "height_min_m": HEIGHT_MIN_M,
            "height_max_m": HEIGHT_MAX_M,
            "reference_m": REFERENCE_M,
            "valid_input_fraction": round(macro_dem_fraction, 6),
        }
        metadata["macro_runtime_extent_m"] = 50000.0
    (data_dir / "starbase_terrain.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps({
        "ortho": [ortho.width, ortho.height],
        "dem": [dem_10km_image.width, dem_10km_image.height],
        "valid_dem_fraction": round(valid_dem_fraction, 6),
        "height_range_m": [HEIGHT_MIN_M, HEIGHT_MAX_M],
        "reference_m": REFERENCE_M,
    }))


if __name__ == "__main__":
    main()
