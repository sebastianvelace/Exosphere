#!/usr/bin/env python3
"""Behavioural checks for observed footprint recovery and raster georeferencing."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('coverage_builder', ROOT / 'tools/build_starbase_context_assets.py')
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class CoverageAssetsTests(unittest.TestCase):
    def test_border_connected_void_does_not_erase_similarly_coloured_fields(self):
        rgb = np.full((32, 32, 3), (60, 80, 40), dtype=np.uint8)
        rgb[:, 20:] = (139, 139, 105)
        rgb[12:16, 6:10] = (139, 139, 105)  # real isolated field, same colour
        with tempfile.TemporaryDirectory() as directory:
            image, mask = Path(directory) / 'source.png', Path(directory) / 'mask.png'
            Image.fromarray(rgb).save(image)
            builder.rebuild_mask(image, mask, 2)
            values = np.asarray(Image.open(mask))
            self.assertEqual(255, values[13, 7])
            self.assertEqual(0, values[13, 25])
            self.assertGreater(values[13, 19], 0)  # narrow interior handoff
            self.assertEqual(255, values[13, 17])

    def test_ocean_handoff_follows_shore_distance_not_noisy_swath_colour(self):
        rgb = np.full((32, 32, 3), (60, 80, 40), dtype=np.uint8)
        rgb[:, 28:] = (139, 139, 105)
        # Even grey/cloudy sea texels must not be mistaken for islands.
        ocean = np.zeros((32, 32), dtype=bool)
        ocean[:, 8:] = True
        with tempfile.TemporaryDirectory() as directory:
            image, mask = Path(directory) / 'source.png', Path(directory) / 'mask.png'
            Image.fromarray(rgb).save(image)
            builder.rebuild_mask(image, mask, 1, [6400, 6400], ocean)
            values = np.asarray(Image.open(mask))
            self.assertEqual(255, values[16, 5])  # observed land
            self.assertEqual(255, values[16, 8])  # measured coastal water
            self.assertEqual(0, values[16, 18])   # continuous open Gulf

    def test_one_degree_macro_extent_is_not_compressed_to_fifty_kilometres(self):
        size = builder.metric_size(dict(west=-97.6566, east=-96.6566,
                                        south=25.4972, north=26.4972), 25.9972)
        self.assertTrue(100000 < size[0] < 101000)
        self.assertTrue(110000 < size[1] < 112000)
        self.assertGreater(size[1], size[0])

    def test_shipped_georeference_and_context_have_no_provider_void(self):
        manifest = json.loads((ROOT / 'data/launch_sites/starbase_terrain.json').read_text())
        context = manifest['context_ortho']
        expected = builder.metric_size(context['bbox_wgs84'], manifest['center']['latitude'])
        self.assertEqual(expected, context['runtime_size_m'])
        self.assertGreater(min(expected), 330000)
        image = Image.open(ROOT / context['file'])
        self.assertEqual((4096, 4096), image.size)
        # Land in Mexico must have genuine spatial contrast beyond NAIP's swath.
        sample = np.asarray(image)[2600:3200, 900:1600]
        self.assertGreater(float(sample.std()), 15)
        self.assertLess(float(np.mean(np.max(sample, axis=2) < 10)), 0.001)


if __name__ == '__main__':
    unittest.main()
