#!/usr/bin/env python3
"""Validate a frozen below/inside/above cloud capture and measured hull occlusion."""
import argparse
import math
from pathlib import Path
import re
from PIL import Image


def validate(folder, log):
    evidence = log.read_text()
    for slug in ('cloud_below', 'cloud_inside_off', 'cloud_inside_on', 'cloud_above', 'cloud_domes'):
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
    def contrast(image):
        w, h = image.size
        assert 12 <= x < w - 12 and 0 <= y0 < y1 < h
        total, count = 0, 0
        for y in range(y0, y1 + 1):
            left, right = image.getpixel((x - 10, y)), image.getpixel((x + 10, y))
            for px in range(x - 3, x + 4):
                value = image.getpixel((px, y))
                total += sum(abs(value[c] - (left[c] + right[c]) / 2) for c in range(3)) / (3 * 255)
                count += 1
        return total / count
    clear, obscured = [contrast(image) for image in images]
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
