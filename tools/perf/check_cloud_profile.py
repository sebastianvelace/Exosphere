#!/usr/bin/env python3
"""Validate real-framebuffer A/B evidence, not a source-code approximation.

Usage: python3 tools/perf/check_cloud_profile.py exports/path/profile-prefix
Requires Pillow. Run cloud_traversal_probe.gd first on the target GPU.
"""
import json
import math
import statistics
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageStat


def check(prefix: Path) -> dict:
    rows = json.loads(prefix.with_suffix(".json").read_text())
    log = prefix.with_suffix(".log").read_text()
    if "CLOUD_PROFILE_OK" not in log or "ERROR:" in log:
        raise ValueError("Incomplete or erroneous GPU run")
    cases = {}
    states = []
    for line in log.splitlines():
        if line.startswith("CLOUD_PROFILE_CASE "):
            states.append(json.loads(line.split(" state=", 1)[1]))
    if len(states) != 4 or any(state != states[0] for state in states[1:]):
        raise ValueError("Cloud modes were not measured at the same physical state")
    if not states[0]["paused"]:
        raise ValueError("Physics was not paused for the A/B")
    for mode in ["cached", "procedural", "cached-repeat", "hull-off"]:
        samples = [r for r in rows if r[0] == mode]
        if len(samples) != 40 or any(
            not all(math.isfinite(float(v)) for v in r[1:9])
            or r[2] <= 0 or r[6] <= 0 for r in samples
        ):
            raise ValueError("Missing finite, positive GPU timings for " + mode)
        cases[mode] = {
            "frame_median_ms": statistics.median(r[2] for r in samples),
            "gpu_median_ms": statistics.median(r[6] for r in samples),
            "frame_p95_ms": sorted(r[2] for r in samples)[37],
        }
    cached = Image.open(str(prefix) + "-cached.png").convert("RGB")
    procedural = Image.open(str(prefix) + "-procedural.png").convert("RGB")
    if cached.size != procedural.size or min(cached.size) < 360:
        raise ValueError("Missing comparable real framebuffer captures")
    # Exclude telemetry and most sky: compare the central cloud/vehicle region.
    w, h = cached.size
    region = (int(w * .26), int(h * .40), int(w * .76), int(h * .79))
    diff = ImageStat.Stat(ImageChops.difference(cached.crop(region), procedural.crop(region)))
    # Byte-domain regression limits allow quantization and exposure settling;
    # they do not establish measured meteorological or mission fidelity.
    if max(diff.mean) > 4 or max(diff.rms) > 8:
        raise ValueError("Cached cloud transport exceeds image-difference bounds")
    return {
        "physical_state_unchanged": True,
        "cloud_roi_mean_rgb_byte_difference": diff.mean,
        "cloud_roi_rms_rgb_byte_difference": diff.rms,
        "timings": cases,
        "gpu_speedup": cases["procedural"]["gpu_median_ms"] / cases["cached"]["gpu_median_ms"],
    }


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check_cloud_profile.py PROFILE_PREFIX")
    print(json.dumps(check(Path(sys.argv[1])), indent=2))
