#!/usr/bin/env python3
"""Replay an unpowered atmospheric entry with the production EDL and force integrator.

This is a diagnostic initial-condition fixture, not a recorded user flight or a
historical mission. It ends above the landing burn. Headless mode produces no PNG.
"""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", default=os.environ.get("GODOT_BIN") or shutil.which("godot"))
    parser.add_argument("--altitude", type=float, default=109_000)
    parser.add_argument("--speed", type=float, default=6_100, help="body-centred inertial m/s")
    parser.add_argument("--angle", type=float, default=-9.7, help="inertial flight-path degrees")
    parser.add_argument("--reserve", type=float, default=0)
    parser.add_argument("--require-descent", action="store_true", help="fail if this fixture develops an unpowered climb")
    parser.add_argument("--output", type=Path, default=Path("/tmp/exo-entry-rebound.jsonl"))
    args = parser.parse_args()
    if not args.godot:
        parser.error("Set GODOT_BIN or pass --godot pointing to the mono executable")
    if not 0 <= args.reserve <= 1 or not -90 < args.angle < 0 or args.altitude <= 0 or args.speed <= 0:
        parser.error("Entry requires a positive altitude/speed, descending angle and reserve in [0,1]")
    if Path("/tmp/exosphere-visual-playtest.lock").exists():
        parser.error("Wait for the active visual playtest before building the audit fixture")
    script = ROOT / "scripts/_EntryReboundAuditShot.cs"
    if script.exists():
        parser.error(f"Existing fixture must be cleaned by its owner: {script}")
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="exo-entry-audit-") as temporary:
        folder = Path(temporary)
        driver = folder / "run.gd"
        driver.write_text('extends SceneTree\nfunc _initialize():\n'
                          '    root.add_child(load("res://scripts/_EntryReboundAuditShot.cs").new())\n'
                          '    change_scene_to_file("res://scenes/flight/Flight.tscn")\n')
        env = os.environ.copy()
        env.pop("EXOSPHERE_PLAYTEST_TOKEN", None)
        env.update(XDG_DATA_HOME=str(folder / "profile"), ENTRY_ALT=str(args.altitude),
                   ENTRY_SPEED=str(args.speed), ENTRY_ANGLE=str(args.angle),
                   ENTRY_RESERVE=str(args.reserve), ENTRY_AUDIT_LOG=str(output))
        try:
            script.write_text((ROOT / "tools/fixtures/EntryReboundAudit.cs.in").read_text())
            subprocess.run(["dotnet", "build", "Exosphere.csproj", "--nologo", "-v", "quiet"], cwd=ROOT, check=True)
            with output.with_suffix(".console.log").open("w") as console:
                subprocess.run([args.godot, "--headless", "--path", str(ROOT),
                                "--log-file", str(folder / "godot.log"), "--script", str(driver)],
                               cwd=ROOT, env=env, stdout=console, stderr=subprocess.STDOUT,
                               check=True, timeout=360)
        finally:
            script.unlink(missing_ok=True)
            script.with_suffix(".cs.uid").unlink(missing_ok=True)
    rows = [json.loads(line) for line in output.read_text().splitlines()]
    if not rows:
        raise RuntimeError("No production physics samples were recorded")
    summary = dict(samples=len(rows), finalAltitude=rows[-1]["alt"],
                   maximumVerticalSpeed=max(row["vUp"] for row in rows),
                   peakAeroG=max(row["g"] for row in rows),
                   energyChange=rows[-1]["energy"]-rows[0]["energy"],
                   minimumGravity=min(row["gravity"] for row in rows),
                   destroyed=any(row["destroyed"] for row in rows),
                   demo=any(row["demo"] for row in rows))
    print(json.dumps(summary, indent=2))
    output.with_suffix(".summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    if args.require_descent and summary["maximumVerticalSpeed"] > 0:
        raise RuntimeError("The controlled entry developed an unpowered climb; inspect the trace")
    if summary["destroyed"] or summary["demo"] or summary["finalAltitude"] > 11_000:
        raise RuntimeError("Entry did not complete the unpowered diagnostic corridor")


if __name__ == "__main__":
    main()
