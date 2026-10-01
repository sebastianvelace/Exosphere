#!/usr/bin/env python3
"""Validate a frozen below/inside/above cloud capture and measured hull occlusion."""
import argparse
import math
from pathlib import Path
import re
from PIL import Image


def hull_feature_contrast(clear_image, obscured_image, x, y0, y1):
    """Measure the same visible hull features in both images, using only the clear shot.

    At 640px the hull is ~1 pixel wide: a fixed seven-pixel strip measures mostly
    background and horizon aliasing. Select clear centre-line features once and keep
    that mask for the obscured shot, so opacity cannot select its own passing pixels.
    """
    width, height = clear_image.size
    assert obscured_image.size == (width, height)
    assert 12 <= x < width - 12 and 0 <= y0 < y1 < height
    features = []
    for y in range(y0, y1 + 1):
        left, right = clear_image.getpixel((x-10, y)), clear_image.getpixel((x+10, y))
        background = tuple((left[c] + right[c]) / 2 for c in range(3))
        pixel = clear_image.getpixel((x, y))
        contrast = sum(abs(pixel[c]-background[c]) for c in range(3)) / (3*255)
        if contrast > 0.04:
            features.append((y, background, contrast))
    assert len(features) >= max(5, (y1-y0)*0.4), 'insufficient resolved clear hull features'
    clear = sum(feature[2] for feature in features) / len(features)
    obscured = 0.0
    for y, background, _ in features:
        left, right = obscured_image.getpixel((x-10,y)), obscured_image.getpixel((x+10,y))
        assert max(abs((left[c]+right[c])/2-background[c]) for c in range(3)) <= 2, 'A/B background changed'
        pixel = obscured_image.getpixel((x,y))
        obscured += sum(abs(pixel[c]-background[c]) for c in range(3)) / (3*255)
    return clear, obscured / len(features)


def validate(folder, log):
    evidence = log.read_text()
    for slug in ('cloud_below', 'cloud_inside_off', 'cloud_inside_on', 'cloud_above', 'cloud_domes', 'cloud_nadir'):
        with Image.open(folder / f'exo_play_{slug}.png') as image:
            image.verify()
        assert re.search(rf'^CAPTURE {slug} .*phase=ORBIT', evidence, re.M), slug
    matches = {}
    for slug in ('cloud_inside_off', 'cloud_inside_on'):
        row = re.search(rf'^CLOUD_FOREGROUND slug={slug} (.*)$', evidence, re.M)
        assert row, f'missing foreground binding: {slug}'
        matches[slug] = dict(re.findall(r'(\w+)=([^ ]+)', row[1]))
        assert float(matches[slug]['timeScale']) == 0, 'fixture is not paused'
    off, on = matches.values()
    assert off['attached'] == 'False' and on['attached'] == 'True', 'A/B did not toggle actual overlay'
    assert all(abs(float(off[k]) - float(on[k])) < 0.1 for k in ('x', 'y0', 'y1')), 'camera changed between A/B'
    x, y0, y1 = round(float(off['x'])), math.floor(float(off['y0'])), math.ceil(float(off['y1']))
    images = [Image.open(folder / f'exo_play_cloud_inside_{mode}.png').convert('RGB') for mode in ('off', 'on')]
    assert images[0].size == images[1].size
    clear, obscured = hull_feature_contrast(images[0], images[1], x, y0, y1)
    assert clear > 0.01, 'clear hull has insufficient contrast to establish occlusion'
    ratio = obscured / clear
    assert ratio < 0.65, f'hull remains visible through dense cloud: ratio={ratio:.3f}'
    print(f'Cloud occlusion PASS: off={clear:.5f} on={obscured:.5f} contrast reduction={1-ratio:.1%}. Visual review still required.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('log', type=Path)
    args = parser.parse_args()
    validate(args.folder, args.log)
