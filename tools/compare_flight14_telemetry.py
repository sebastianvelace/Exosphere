#!/usr/bin/env python3
"""Compare sampled simulator telemetry to the manually inspected Flight 14 HUD.

No interpolation, extrapolation, speed-frame conversion or physical acceptance is
inferred. Both inertial and atmosphere-relative residuals are reported separately.
Legacy logs require an explicit liftoff epoch; first trace time is not liftoff.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_ANCHORS = ROOT / "docs/research/STARSHIP_FLIGHT14_OBSERVED_ANCHORS_2026-10-01.json"


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def read_samples(path, liftoff_epoch=None):
    if liftoff_epoch is not None and not finite(liftoff_epoch):
        raise ValueError("Explicit liftoff epoch must be finite")
    lines = path.read_text().splitlines()
    clocks = [float(match[1]) for line in lines if (match := re.match(r"FLIGHT_CLOCK liftoffEpoch=(\S+)", line))]
    if clocks:
        if any(not finite(clock) for clock in clocks) or len(set(clocks)) != 1:
            raise ValueError("Ambiguous/non-finite liftoff epochs; split rewound or multiple flights")
        if liftoff_epoch is not None and abs(liftoff_epoch-clocks[0]) > 1e-6:
            raise ValueError("Explicit liftoff epoch disagrees with recorded callback")
        liftoff_epoch = clocks[0]
    samples = []
    for line in lines:
        if line.startswith("TRACE_ASCENT "):
            fields = dict(re.findall(r"([A-Za-z][A-Za-z0-9]*)=([^ ]+)", line))
            explicit_elapsed = float(fields.get("missionElapsed", "nan"))
            if finite(explicit_elapsed):
                # TRACE_ASCENT t is rounded to 0.1 s; missionElapsed retains 1 ms.
                if finite(liftoff_epoch) and abs(float(fields["t"])-liftoff_epoch-explicit_elapsed) > 0.051:
                    raise ValueError("Trace mission clock disagrees with liftoff epoch")
                elapsed = explicit_elapsed
            elif finite(liftoff_epoch):
                elapsed = float(fields["t"])-liftoff_epoch
            else:
                elapsed = explicit_elapsed
            row = {"missionElapsedSeconds": elapsed, "phase": fields.get("mission"),
                   "geodeticAltitudeM": float(fields["alt"]),
                   "atmosphereRelativeSpeedMps": float(fields["spd"])}
            for original, normalized in (("inertialSpeed", "inertialSpeedMps"), ("radialAltitude", "radialAltitudeM")):
                if original in fields:
                    row[normalized] = float(fields[original])
        elif line.startswith("{"):
            row = json.loads(line)
            if "missionElapsedSeconds" not in row:
                raise ValueError("JSONL samples must declare missionElapsedSeconds, not an ambiguous t")
        else:
            continue
        elapsed = row.get("missionElapsedSeconds")
        if not finite(elapsed):
            if row.get("phase") in ("PRE_LAUNCH", "COUNTDOWN", "IGNITION"):
                continue
            raise ValueError("Flight sample has no finite, explicit launch-clock alignment")
        if elapsed < 0:
            continue
        for name in ("geodeticAltitudeM", "radialAltitudeM", "inertialSpeedMps", "atmosphereRelativeSpeedMps"):
            if name in row and row[name] is not None and not finite(row[name]):
                raise ValueError(f"Non-finite sample {name}")
        if samples and elapsed <= samples[-1]["missionElapsedSeconds"]:
            raise ValueError("Samples must be strictly ordered; split duplicate or rewound flights")
        samples.append(row)
    if not samples:
        raise ValueError("No clock-aligned flight samples")
    return samples


def compare(samples, reference, maximum_sample_offset):
    if not finite(maximum_sample_offset) or maximum_sample_offset < 0:
        raise ValueError("Maximum sample offset must be finite and nonnegative")
    if reference.get("speed_frame") != "not_specified_by_broadcast" or reference.get("physics_authority") is not False:
        raise ValueError("This comparator requires the reviewed, non-authoritative display reference")
    results = []
    for anchor in reference["anchors"]:
        elapsed = anchor["displayed_elapsed_seconds"]
        nearest = min(samples, key=lambda row: abs(row["missionElapsedSeconds"]-elapsed))
        offset = nearest["missionElapsedSeconds"]-elapsed
        item = {"referenceElapsedSeconds": elapsed, "view": anchor["view"],
                "referenceFrameSha256": anchor["frame_sha256"],
                "displayedAltitudeKm": anchor.get("displayed_altitude_km"),
                "displayedSpeedKmh": anchor.get("displayed_speed_kmh")}
        # Endpoint nearness does not authorize extrapolation beyond coverage.
        if (elapsed < samples[0]["missionElapsedSeconds"] or elapsed > samples[-1]["missionElapsedSeconds"]
                or abs(offset) > maximum_sample_offset):
            item.update(status="missing-sample", sampleOffsetSeconds=None)
        else:
            item.update(status="sampled", sampleElapsedSeconds=nearest["missionElapsedSeconds"],
                        sampleOffsetSeconds=offset, samplePhase=nearest.get("phase"), residuals={})
            for field in ("geodeticAltitudeM", "radialAltitudeM"):
                if finite(anchor.get("displayed_altitude_km")) and finite(nearest.get(field)):
                    item["residuals"][field] = nearest[field]-anchor["displayed_altitude_km"]*1000
            for field in ("inertialSpeedMps", "atmosphereRelativeSpeedMps"):
                if finite(anchor.get("displayed_speed_kmh")) and finite(nearest.get(field)):
                    item["residuals"][field] = nearest[field]-anchor["displayed_speed_kmh"]/3.6
        results.append(item)
    return results


def markdown(report):
    lines = ["# Flight 14 display comparison", "", "Diagnostic only: speed frame and altitude datum remain unspecified.",
             "Nearest real samples are used; no interpolation or physical acceptance is inferred.", "",
             "| Reference T+ (s) | Sample offset (s) | Geodetic altitude error (m) | Radial altitude error (m) | Atmosphere-relative speed error (m/s) | Inertial speed error (m/s) |",
             "| ---: | ---: | ---: | ---: | ---: | ---: |"]
    def number(value):
        return f"{value:.1f}" if finite(value) else "—"
    for item in report["comparisons"]:
        residuals = item.get("residuals", {})
        cells = [str(item["referenceElapsedSeconds"]), number(item.get("sampleOffsetSeconds"))]
        cells += [number(residuals.get(key)) for key in ("geodeticAltitudeM", "radialAltitudeM", "atmosphereRelativeSpeedMps", "inertialSpeedMps")]
        lines.append("| " + " | ".join(cells) + " |")
    lines += ["", f"Coverage: {report['sampledAnchors']}/{len(report['comparisons'])} reviewed reference anchors.",
              "Missing rows are unobserved coverage, never a passing match.", "",
              "Clock-alignment uncertainty and nearest-sample offsets are distinct from measurement/model uncertainty.",
              f"Manual simulator liftoff epoch (s): {report['manualSimulatorLiftoffEpochSeconds']}",
              f"Manual simulator clock uncertainty (s): {report['manualSimulatorClockUncertaintySeconds']}",
              f"Reference clock uncertainty (s): {report['referenceClockUncertaintySeconds']}", "",
              f"Telemetry SHA-256: `{report['telemetrySha256']}`", f"Reference SHA-256: `{report['referenceSha256']}`", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("telemetry", type=Path)
    parser.add_argument("--anchors", type=Path, default=DEFAULT_ANCHORS)
    parser.add_argument("--liftoff-epoch", type=float)
    parser.add_argument("--clock-uncertainty", type=float, default=0, help="manual simulator-clock alignment uncertainty, seconds")
    parser.add_argument("--maximum-sample-offset", type=float, default=1)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not finite(args.clock_uncertainty) or args.clock_uncertainty < 0:
        parser.error("Clock uncertainty must be finite and nonnegative")
    try:
        samples = read_samples(args.telemetry, args.liftoff_epoch)
        reference = json.loads(args.anchors.read_text())
        comparisons = compare(samples, reference, args.maximum_sample_offset)
    except (ValueError, KeyError) as error:
        parser.error(str(error))
    report = {"schemaVersion": 1, "status": "diagnostic-not-mission-acceptance", "physicsAuthority": False,
              "telemetryFile": str(args.telemetry.resolve()),
              "telemetrySha256": hashlib.sha256(args.telemetry.read_bytes()).hexdigest(),
              "referenceSha256": hashlib.sha256(args.anchors.read_bytes()).hexdigest(),
              "sourceVideoSha256": reference["source_video_sha256"],
              "referenceClockUncertaintySeconds": reference["alignment_uncertainty_seconds"],
              "manualSimulatorLiftoffEpochSeconds": args.liftoff_epoch,
              "manualSimulatorClockUncertaintySeconds": args.clock_uncertainty,
              "maximumSampleOffsetSeconds": args.maximum_sample_offset,
              "sampledAnchors": sum(item["status"] == "sampled" for item in comparisons),
              "comparisons": comparisons}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    args.output.with_suffix(".md").write_text(markdown(report))
    print(f"Display comparison: {report['sampledAnchors']}/{len(comparisons)} anchors sampled; remaining coverage missing.")


if __name__ == "__main__":
    main()
