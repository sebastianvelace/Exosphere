#!/usr/bin/env python3
"""Prepare Starbase regional coverage from NASA GIBS and existing NAIP imagery.

Input WMS: EPSG:4326, latitude-first bbox
24.4972,-98.822,27.4972,-95.4912, 4096 square Landsat / 1024 square BMNG.
Landsat annual true-color layer: 2000-12-01; BMNG: August 2004 composite.
No runtime downloads. These are historical geographic context, not flight weather.
"""
from __future__ import annotations
import argparse
import json
import math
import hashlib
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt, binary_propagation, map_coordinates


def metric_size(bounds: dict, latitude: float) -> list[float]:
    # WGS84 degree lengths at the raster centre. Visual georeferencing only.
    phi = math.radians(latitude)
    a, e2 = 6378137.0, 6.69437999014e-3
    d = 1.0 - e2 * math.sin(phi) ** 2
    east = math.pi / 180 * a / math.sqrt(d) * math.cos(phi)
    north = math.pi / 180 * a * (1 - e2) / d ** 1.5
    return [round((bounds['east'] - bounds['west']) * east, 3),
            round((bounds['north'] - bounds['south']) * north, 3)]


def rebuild_mask(image_path: Path, output: Path, width: int, size_m: list[float] | None = None,
                 water_context: np.ndarray | None = None) -> dict:
    rgb = np.array(Image.open(image_path).convert('RGB'))
    fill = rgb[-1, -1].astype(float)
    # Legacy JPEG void is a flat median fill, not alpha. Reconstruct its hard
    # validity once offline, rather than estimating it per fragment/LOD.
    candidate = np.max(np.abs(rgb.astype(float) - fill), axis=2) <= 10
    seed = np.zeros(candidate.shape, dtype=bool)
    seed[0, :] = candidate[0, :]
    seed[-1, :] = candidate[-1, :]
    seed[:, 0] = candidate[:, 0]
    seed[:, -1] = candidate[:, -1]
    # Reject connected provider voids, not similarly coloured fields inside
    # the observed mosaic. Colour alone erased legitimate agricultural detail.
    valid = ~binary_propagation(seed, mask=candidate)
    mask = np.minimum(distance_transform_edt(valid) / width, 1.0)
    if size_m is not None:
        r, g, b = rgb.astype(float).transpose(2, 0, 1)
        water = (g > r * 1.30) & (b > r * 1.10) & (g + b > 46)
        if water_context is not None:
            water = water_context
        # NAIP offshore swaths have visible acquisition seams and end abruptly.
        # Keep measured beaches/lagoon colour, then hand water to continuous
        # NASA ocean context by distance from the observed shore, not tile shape.
        distance = distance_transform_edt(water | ~valid,
            sampling=(size_m[1] / rgb.shape[0], size_m[0] / rgb.shape[1]))
        shore_weight = np.clip((1250.0 - distance) / 1000.0, 0.0, 1.0)
        mask *= np.where(water, shore_weight, 1.0)
    Image.fromarray(np.round(mask * 255).astype('uint8')).save(output)
    return dict(method='distance inside observed footprint; legacy neutral JPEG fill rejected',
                feather_pixels=width, ocean_handoff_m=[250, 1250] if size_m is not None else None, valid_fraction=round(float(valid.mean()), 6))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--landsat', type=Path, required=True)
    parser.add_argument('--blue-marble', type=Path, required=True)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    texture_dir = args.root / 'assets/textures'
    landsat = np.array(Image.open(args.landsat).convert('RGB'))
    bm = np.array(Image.open(args.blue_marble).convert('RGB').resize(
        (landsat.shape[1], landsat.shape[0]), Image.Resampling.BILINEAR))
    # WELD contains black no-data beyond its land-focused swaths and cloud
    # artifacts over the open Gulf. BMNG supplies continuous ocean context.
    br, bg, bb = bm.astype(float).transpose(2, 0, 1)
    water = (np.max(bm, axis=2) < 28) | ((bg > br * 1.20) & (bb > br * 1.15))
    land_distance = distance_transform_edt(~water)
    weight = np.minimum(land_distance / 8.0, 1.0)
    weight *= (np.max(landsat, axis=2) > 10)
    combined = np.round(landsat * weight[..., None] + bm * (1 - weight[..., None])).astype('uint8')
    Image.fromarray(combined).save(texture_dir / 'starbase_landsat_context.jpg', quality=92, optimize=True)
    manifest_path = args.root / 'data/launch_sites/starbase_terrain.json'
    manifest = json.loads(manifest_path.read_text())
    latitude = manifest['center']['latitude']
    for prefix, key, bbox_key, width in [('10km', 'ortho', 'bbox_wgs84', 12),
                                          ('50km', 'macro_ortho', 'macro_bbox_wgs84', 24)]:
        entry = manifest[key]
        entry['runtime_size_m'] = metric_size(manifest[bbox_key], latitude)
        bounds = manifest[bbox_key]
        # The continuous NASA ocean separates open Gulf from land without
        # treating dark/noisy NAIP water pixels as tiny islands. Geographic
        # shore distance, not acquisition-strip geometry, owns this handoff.
        context_bounds = dict(west=-98.822, south=24.4972, east=-95.4912, north=27.4972)
        size = Image.open(texture_dir / f'starbase_naip_{prefix}.jpg').size
        lon = np.linspace(bounds['west'], bounds['east'], size[0])
        lat = np.linspace(bounds['north'], bounds['south'], size[1])
        cx = (lon - context_bounds['west']) / (context_bounds['east'] - context_bounds['west']) * (bm.shape[1] - 1)
        cy = (context_bounds['north'] - lat) / (context_bounds['north'] - context_bounds['south']) * (bm.shape[0] - 1)
        y, x = np.meshgrid(cy, cx, indexing='ij')
        water_context = map_coordinates(water.astype(float), [y, x], order=1) > 0.5
        entry['mask_preparation'] = rebuild_mask(texture_dir / f'starbase_naip_{prefix}.jpg',
            texture_dir / f'starbase_naip_{prefix}_mask.png', width, entry['runtime_size_m'], water_context)
    bbox = dict(west=-98.822, south=24.4972, east=-95.4912, north=27.4972)
    manifest['context_ortho'] = dict(file='assets/textures/starbase_landsat_context.jpg',
        width=landsat.shape[1], height=landsat.shape[0], bbox_wgs84=bbox,
        runtime_size_m=metric_size(bbox, latitude),
        source='NASA GIBS Landsat WELD annual true color with Blue Marble Next Generation ocean',
        source_url='https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi',
        landsat_layer='Landsat_WELD_CorrectedReflectance_TrueColor_Global_Annual',
        landsat_time='2000-12-01', ocean_layer='BlueMarble_NextGeneration',
        ocean_time='August 2004 static composite',
        preparation='Land-focused WELD over continuous BMNG ocean; 8 pixel interior blend',
        attribution='NASA GIBS / NASA GSFC / USGS Landsat / WELD',
        request=dict(service='WMS', version='1.3.0', crs='EPSG:4326',
                     bbox_latitude_first=[24.4972, -98.822, 27.4972, -95.4912],
                     landsat_size_px=[4096, 4096], ocean_size_px=[1024, 1024], format='image/png'),
        source_sha256=dict(landsat=hashlib.sha256(args.landsat.read_bytes()).hexdigest(),
                           ocean=hashlib.sha256(args.blue_marble.read_bytes()).hexdigest()),
        limitations='Historical geographic context; not current buildings or flight-specific weather')
    manifest['notes'] = [n for n in manifest['notes'] if not n.startswith(('runtime_size_m', 'No circular'))] + ['runtime_size_m is the raster georeference; legacy runtime_extent_m is retained for compatibility only.',
                          'No circular imagery cutout: narrow footprint masks reveal continuous Landsat/BMNG context.']
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')


if __name__ == '__main__':
    main()
