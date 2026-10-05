#!/usr/bin/env python3
"""Compare saved booster telemetry with rounded broadcast readings, never drive physics."""

import argparse
import hashlib
import json
import math
from pathlib import Path


def load_json(path):
    return json.loads(Path(path).read_text(), parse_constant=lambda value: reject(value))


def reject(value):
    raise ValueError(f"Non-finite JSON value: {value}")


def metrics(values):
    return {
        "count": len(values),
        "mean_absolute_residual": sum(abs(v) for v in values) / len(values) if values else None,
        "root_mean_square_residual": math.sqrt(sum(v * v for v in values) / len(values)) if values else None,
    }


def compare(samples, references, tolerance=0.05):
    if not math.isfinite(tolerance) or tolerance < 0:
        raise ValueError("Sample clock tolerance must be finite and nonnegative.")
    if not samples:
        raise ValueError("Empty model telemetry.")
    previous = -math.inf
    for sample in samples:
        clock = sample["missionElapsedSeconds"]
        if not math.isfinite(clock) or clock <= previous:
            raise ValueError("Model sample clocks must be finite and strictly increasing.")
        previous = clock
        for field in ("geodeticAltitudeM", "atmosphereRelativeSpeedMps", "bodyCentredInertialSpeedMps"):
            if field in sample and not math.isfinite(sample[field]):
                raise ValueError(f"Non-finite model {field}.")
    rows = []
    for reference in references:
        if reference.get("physics_authority") is not False:
            raise ValueError("Reference must explicitly declare physics_authority=false.")
        for anchor in reference["anchors"]:
            if "booster_altitude_display_km" not in anchor:
                continue
            clock = anchor["broadcast_met_seconds"]
            altitude = anchor["booster_altitude_display_km"]
            speed = anchor.get("booster_speed_display_kmh")
            if not all(math.isfinite(v) for v in [clock, altitude] + ([] if speed is None else [speed])):
                raise ValueError("Non-finite reference reading.")
            sample = min(samples, key=lambda value: abs(value["missionElapsedSeconds"] - clock))
            clock_error = sample["missionElapsedSeconds"] - clock
            row = {
                "broadcast_met_seconds": clock,
                "reference_altitude_display_km": altitude,
                "reference_speed_display_kmh": speed,
                "source_url": reference["source_url"],
                "capture_sha256": anchor["capture_sha256"],
                "segment": "coast" if altitude >= 10 else "terminal",
                "model_sample_clock_residual_seconds": clock_error,
                "status": "sampled" if abs(clock_error) <= tolerance else "model-sample-missing",
            }
            if row["status"] == "sampled":
                row.update({
                    "model_phase": sample["phase"],
                    "model_geodetic_altitude_m": sample["geodeticAltitudeM"],
                    "altitude_display_residual_m": sample["geodeticAltitudeM"] - altitude * 1000,
                    "model_atmosphere_relative_speed_kmh": sample["atmosphereRelativeSpeedMps"] * 3.6,
                })
                if "bodyCentredInertialSpeedMps" in sample:
                    row["model_body_centred_inertial_speed_kmh"] = sample["bodyCentredInertialSpeedMps"] * 3.6
                if speed is not None:
                    row["atmosphere_relative_speed_display_residual_kmh"] = row["model_atmosphere_relative_speed_kmh"] - speed
                    if "model_body_centred_inertial_speed_kmh" in row:
                        row["body_centred_inertial_speed_display_residual_kmh"] = row["model_body_centred_inertial_speed_kmh"] - speed
            rows.append(row)
    summaries = {}
    for segment in ("coast", "terminal"):
        subset = [r for r in rows if r["segment"] == segment]
        summaries[segment] = {"missing_samples": sum(r["status"] != "sampled" for r in subset)}
        for field in ("altitude_display_residual_m", "atmosphere_relative_speed_display_residual_kmh", "body_centred_inertial_speed_display_residual_kmh"):
            summaries[segment][field] = metrics([r[field] for r in subset if field in r])
    return {
        "status": "reference-comparison-only",
        "physics_authority": False,
        "limitations": [
            "Residuals compare rounded displays, not measured state or reconstruction tolerances.",
            "Broadcast altitude datum, speed frame, feed latency and measurement uncertainty are unknown.",
            "Both speed frames are reported when available; neither is assumed to be the broadcast frame.",
            "Sample clock tolerance only matches recorded model clocks; it is not reference timing uncertainty.",
            "Missing samples remain missing. A retired booster state is not extrapolated to a later epoch.",
        ],
        "sample_clock_tolerance_seconds": tolerance,
        "summaries": summaries,
        "anchors": sorted(rows, key=lambda row: row["broadcast_met_seconds"]),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--telemetry", type=Path, required=True)
    parser.add_argument("--anchors", type=Path, nargs="+", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    samples = [json.loads(line, parse_constant=reject) for line in args.telemetry.read_text().splitlines() if line.strip()]
    report = compare(samples, [load_json(path) for path in args.anchors])
    report["inputs"] = [{"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in [args.telemetry, *args.anchors]]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
    print(json.dumps(report["summaries"], indent=2))


if __name__ == "__main__":
    main()
