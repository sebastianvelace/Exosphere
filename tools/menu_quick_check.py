#!/usr/bin/env python3
"""Exercise the operations menu with real Godot pixels and isolated user data."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--flight-hud", action="store_true", help="Check broadcast bounds and camera/density transitions at 1280x720")
    parser.add_argument("--case", help="Run one named case from the matrix")
    parser.add_argument("--output", type=Path, default=Path("exports/menu-operations-review"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    godot = os.environ.get("GODOT_BIN", "/home/sebasvelace/Downloads/"
                           "Godot_v4.6.3-stable_mono_linux_x86_64/"
                           "Godot_v4.6.3-stable_mono_linux.x86_64")
    if not args.skip_build:
        subprocess.run(["dotnet", "build", "Exosphere.csproj", "--nologo", "-v", "quiet"],
                       cwd=root, check=True)
    cases = [
        ("home-1920", "", "1920x1080", 1.0, 0, ""),
        ("home-es-1920", "", "1920x1080", 1.0, 1, ""),
        ("home-1280", "", "1280x720", 1.0, 0, ""),
        ("vehicles-1280", "vehicles", "1280x720", 1.0, 0, ""),
        ("flight14-1280", "flight14", "1280x720", 1.0, 0, ""),
        ("campaign-1280", "campaign", "1280x720", 1.0, 0, ""),
        ("apollo11-partial", "partial", "1280x720", 1.0, 0, ""),
        ("mission-1280", "mission", "1280x720", 1.0, 0, ""),
        ("continue-choice", "continue", "1280x720", 1.0, 0, ""),
        ("settings-1280", "settings", "1280x720", 1.0, 0, ""),
        ("home-es-scale150", "", "1280x720", 1.5, 1, ""),
        ("vehicles-es-scale150", "vehicles", "1280x720", 1.5, 1, ""),
        ("vehicle-es-scale150", "vehicle", "1280x720", 1.5, 1, ""),
        ("vab-1280", "vab", "1280x720", 1.0, 0, ""),
        ("freedom7-launch", "missionlaunch", "960x540", 1.0, 0, ""),
        ("starship-launch", "launch", "960x540", 1.0, 0,
         "starship-flight-12-v3-2026-05-22"),
        ("falcon-launch", "launch", "960x540", 1.0, 0,
         "falcon9-block5-standard-2025-05"),
    ]
    if args.flight_hud:
        cases = [("starship-launch", "launch", "1280x720", 1.0, 0, "starship-flight-12-v3-2026-05-22")]
    if args.case:
        cases = [case for case in cases if case[0] == args.case]
        if not cases:
            parser.error("Unknown case: " + args.case)
    for name, mode, size, scale, language, vehicle in cases:
        with tempfile.TemporaryDirectory(prefix="exo-menu-") as profile:
            settings = Path(profile) / "godot/app_userdata/Exosphere/interface.cfg"
            settings.parent.mkdir(parents=True)
            settings.write_text(f"[interface]\nlanguage={language}\nui_scale={scale}\n"
                                "reduced_motion=true\n", encoding="utf-8")
            if mode == "continue":
                saves = settings.parent / "saves"
                saves.mkdir()
                for slot in ("alpha", "zulu"):
                    (saves / f"{slot}.json").write_text("{}", encoding="utf-8")
            env = dict(os.environ, XDG_DATA_HOME=profile, CAPTURE_MENU_MODAL=mode,
                       CAPTURE_MENU_VEHICLE=vehicle,
                       CAPTURE_MENU_OUTPUT=str(output / f"{name}.png"),
                       CAPTURE_FLIGHT_HUD="1" if args.flight_hud else "0")
            log_path = output / f"{name}.log"
            with log_path.open("w") as log:
                result = subprocess.run([
                    "xvfb-run", "-a", "-s", f"-screen 0 {size}x24", godot,
                    "--path", str(root), "--rendering-driver", "opengl3",
                    "--resolution", size, "--script", "tools/capture_menu.gd",
                ], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=360 if args.flight_hud else 180)
            evidence = log_path.read_text()
            route_marker = ("MENU_LAUNCH_OK" if mode in {"launch", "missionlaunch"}
                            else "MENU_VAB_OK" if mode == "vab" else "MENU_LAYOUT_OK")
            markers = ["MENU_CAPTURE", route_marker]
            if args.flight_hud:
                markers.extend(f"FLIGHT_HUD_OK case={case} " for case in
                               ("minimal", "full", "clean", "restored", "cockpit", "exterior", "map", "return"))
            if not mode:
                markers.append("MENU_HOME_FOCUS_OK")
            if mode == "continue":
                markers.append("MENU_CONTINUE_CHOICE_OK")
            if mode and mode not in {"launch", "missionlaunch", "vab"}:
                markers.append("MENU_BACK_OK")
            if result.returncode or any(marker not in evidence for marker in markers):
                raise SystemExit(f"FAIL {name}: inspect {log_path}\n{evidence[-4000:]}")
            if mode == "launch":
                expected = ("Starship Flight 12 — V3 / Raptor 3 at starbase_pad2" if name.startswith("starship")
                            else "Falcon 9 Block 5 — Standard Fairing at kennedy")
                if expected not in evidence or "profile=manual, mission=sandbox" not in evidence:
                    raise SystemExit(f"FAIL {name}: wrong vehicle, pad or profile; inspect {log_path}")
            if mode == "missionlaunch" and (
                "at cape_canaveral_lc5; profile=mercury-redstone3-suborbital, mission=mission-freedom7-1961" not in evidence
            ):
                raise SystemExit(f"FAIL {name}: campaign mission intent was not preserved")
            print(f"PASS {name}", flush=True)
    print(f"Menu checks passed. Captures: {output}")


if __name__ == "__main__":
    main()
