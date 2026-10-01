#!/usr/bin/env python3
"""Extract selected video windows on a nominal one-second grid, with provenance.

This tool reads a local video; acquisition and manual broadcast-clock annotation
are separate. Video time must never be silently treated as mission elapsed time.
"""
import argparse
import hashlib
import html
import json
import math
import subprocess
from pathlib import Path


def probe(video):
    result = subprocess.run([
        "ffprobe", "-v", "error", "-show_entries", "format=duration",
        "-of", "json", str(video)], check=True, capture_output=True, text=True)
    return float(json.loads(result.stdout)["format"]["duration"])


def validate_windows(windows, duration):
    for window in windows:
        start, end = float(window["start_s"]), float(window["end_s"])
        step = float(window.get("step_s", 1))
        if not all(math.isfinite(x) for x in (start, end, step)):
            raise ValueError("Window times must be finite")
        if not 0 <= start < end <= duration or step <= 0:
            raise ValueError(f"Invalid video interval: {start}..{end}, step {step}")
        if "mission_time_at_start_s" in window:
            if not window.get("clock_evidence"):
                raise ValueError("Mission alignment requires visible-clock evidence")
            if not math.isfinite(float(window["mission_time_at_start_s"])):
                raise ValueError("Mission time must be finite")
    return windows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("video", type=Path)
    parser.add_argument("plan", type=Path, help="JSON with source_url and windows")
    parser.add_argument("output", type=Path, help="New output directory")
    args = parser.parse_args()
    plan = json.loads(args.plan.read_text())
    duration = probe(args.video)
    windows = validate_windows(plan["windows"], duration)
    with args.video.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    if plan.get("video_sha256") and plan["video_sha256"] != digest:
        raise ValueError("Video does not match the source track recorded in the plan")
    args.output.mkdir(parents=True, exist_ok=False)
    records = []
    for index, window in enumerate(windows):
        start, end = float(window["start_s"]), float(window["end_s"])
        step = float(window.get("step_s", 1))
        for sample in range(math.ceil((end - start) / step)):
            video_time = start + sample * step
            if video_time >= end:
                break
            name = f"w{index:02d}_{sample:05d}.jpg"
            subprocess.run([
                "ffmpeg", "-hide_banner", "-loglevel", "error", "-ss", str(video_time),
                "-i", str(args.video), "-frames:v", "1", "-q:v", "2",
                str(args.output / name)], check=True)
            if not (args.output / name).is_file():
                raise RuntimeError(f"No decoded frame at video time {video_time}")
            record = {"file": name, "video_time_s": video_time,
                      "window": window.get("label", str(index))}
            if "mission_time_at_start_s" in window:
                record["mission_time_s"] = float(window["mission_time_at_start_s"]) + video_time - start
                record["clock_evidence"] = window["clock_evidence"]
            records.append(record)
    manifest = {"source_url": plan.get("source_url"), "video_sha256": digest,
                "duration_s": duration, "frames": records,
                "timestamp_semantics": "Nominal seek grid; decoder frame quantization applies. Mission alignment is manual and valid only within a verified uncut window."}
    (args.output / "index.json").write_text(json.dumps(manifest, indent=2) + "\n")
    cards = []
    for record in records:
        label = f'{record["window"]} · video {record["video_time_s"]:.2f}s'
        if "mission_time_s" in record:
            label += f' · T{record["mission_time_s"]:+.2f}s (manual alignment)'
        cards.append(f'<figure><a href="{record["file"]}"><img loading="lazy" src="{record["file"]}"></a><figcaption>{html.escape(label)}</figcaption></figure>')
    (args.output / "index.html").write_text(
        '<!doctype html><meta charset="utf-8"><title>Video reference frames</title>'
        '<style>body{background:#111923;color:#eee;font:16px sans-serif;margin:24px}'
        'main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:12px}'
        'figure{margin:0}img{width:100%}figcaption{padding:8px}</style>'
        '<h1>Video reference frames</h1><p>Video time and mission time are distinct. '
        'Camera, exposure and broadcast cuts require manual review.</p><main>'
        + ''.join(cards) + '</main>')
    print(f"Extracted {len(records)} frames to {args.output}")


if __name__ == "__main__":
    main()
