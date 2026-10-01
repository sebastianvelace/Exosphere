#!/usr/bin/env python3
"""Reject atmosphere images whose bound sky Sun differs from the requested fixture."""
import math
import re
import sys
from pathlib import Path


def validate(log):
    requests, rendered = {}, {}
    for line in log.splitlines():
        fields = dict(re.findall(r"(\w+)=([^ ]+)", line))
        if line.startswith("ATMOS_APPLY "):
            requests[fields["slug"]] = float(fields["targetSunElevation"])
        elif line.startswith("ATMOS_RENDER "):
            rendered[fields["slug"]] = fields
    if not requests:
        raise ValueError("No atmosphere requests")
    for slug, expected in requests.items():
        state = rendered.get(slug)
        if not state or state.get("source") != "bound_sky_parameters":
            raise ValueError(f"Missing bound sky state: {slug}")
        actual = float(state["sunElevation"])
        if not math.isfinite(actual) or not math.isfinite(expected) or abs(actual - expected) > 0.1:
            raise ValueError(f"Wrong rendered Sun: {slug}, expected {expected}, bound {actual}")
        if state.get("sunOverride") != "none":
            raise ValueError(f"Physical solar fixture inherited a presentation override: {slug}")
    return len(requests)


if __name__ == "__main__":
    try:
        count = validate(Path(sys.argv[1]).read_text())
    except (ValueError, KeyError) as error:
        sys.exit(f"atmosphere_render_state: FAIL: {error}")
    print(f"atmosphere_render_state: PASS ({count} bound solar states)")
