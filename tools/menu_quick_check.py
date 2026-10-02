#!/usr/bin/env python3
"""Exercise the operations menu with real Godot pixels and isolated user data."""
import argparse
import os
import json
import re
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--flight-hud", action="store_true", help="Check all ten launchers, stage boards and camera/density transitions at 960x540")
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
    launchers = re.findall(r'new MenuLauncher\("([^\"]+)", "([^\"]+)"',
                           (root / "scripts/UI/FlightMenuCatalog.cs").read_text())
    catalog = {definition["id"]: (definition, site) for file, site in launchers
               for definition in [json.loads((root / "data/vehicles" / file).read_text())]}
    cases = [
        ("home-1920", "", "1920x1080", 1.0, 0, ""),
        ("home-es-1920", "", "1920x1080", 1.0, 1, ""),
        ("home-1280", "", "1280x720", 1.0, 0, ""),
        ("vehicles-1280", "vehicles", "1280x720", 1.0, 0, ""),
        ("flight14-1280", "flight14", "1280x720", 1.0, 0, ""),
        ("flight14-launch-1280", "flight14launch", "1280x720", 1.0, 0, ""),
        ("campaign-1280", "campaign", "1280x720", 1.0, 0, ""),
        ("apollo11-partial", "partial", "1280x720", 1.0, 0, ""),
        ("mission-1280", "mission", "1280x720", 1.0, 0, ""),
        ("continue-choice", "continue", "1280x720", 1.0, 0, ""),
        ("settings-1280", "settings", "1280x720", 1.0, 0, ""),
        ("graphics-1280", "graphics", "1280x720", 1.0, 0, ""),
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
        cases = [(Path(file).stem, "launch", "960x540", 1.0, 0,
                  json.loads((root / "data/vehicles" / file).read_text())["id"])
                 for file, _site in launchers]
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
            if mode == "graphics":
                # Exercise corrupt preference recovery before choosing/saving profiles.
                (settings.parent / "graphics.cfg").write_text('[graphics]\npreset="invalid"\n')
            case_output = output / name if args.flight_hud else output
            case_output.mkdir(parents=True, exist_ok=True)
            env = dict(os.environ, XDG_DATA_HOME=profile, CAPTURE_MENU_MODAL=mode,
                       CAPTURE_MENU_VEHICLE=vehicle,
                       CAPTURE_MENU_OUTPUT=str(case_output / f"{name}.png"),
                       CAPTURE_FLIGHT_HUD="1" if args.flight_hud else "0")
            if mode == "graphics":
                env.pop("EXOSPHERE_GRAPHICS_PRESET", None)
            log_path = output / f"{name}.log"
            with log_path.open("w") as log:
                result = subprocess.run([
                    "xvfb-run", "-a", "-s", f"-screen 0 {size}x24", godot,
                    "--path", str(root), "--rendering-driver", "opengl3",
                    "--resolution", size, "--script", "tools/capture_menu.gd",
                ], cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=int(env.get("CAPTURE_FLIGHT14_TIMEOUT", "180")) if mode == "flight14launch" else (360 if args.flight_hud else 180))
            evidence = log_path.read_text()
            route_marker = ("MENU_LAUNCH_OK" if mode in {"launch", "missionlaunch", "flight14launch"}
                            else "MENU_VAB_OK" if mode == "vab" else "MENU_LAYOUT_OK")
            markers = (["FLIGHT14_PREVIEW_OK", route_marker] if mode == "flight14launch" else ["MENU_CAPTURE", route_marker])
            if mode == "flight14launch" and float(env.get("CAPTURE_FLIGHT14_MET", "8")) <= 20:
                markers.append("FLIGHT14_PREVIEW_CONTROLS_OK")
            if mode == "graphics":
                markers.append("GRAPHICS_SETTINGS_OK")
            if args.flight_hud:
                markers.extend(f"FLIGHT_HUD_OK case={case} " for case in
                               ("minimal", "full", "clean", "restored", "cockpit", "exterior", "map", "return"))
            if not mode:
                markers.append("MENU_HOME_FOCUS_OK")
            if mode == "continue":
                markers.append("MENU_CONTINUE_CHOICE_OK")
            if mode and mode not in {"launch", "missionlaunch", "flight14launch", "vab"}:
                markers.append("MENU_BACK_OK")
            if (result.returncode or any(marker not in evidence for marker in markers)
                    or (mode == "graphics" and "This control can't grab focus" in evidence)):
                raise SystemExit(f"FAIL {name}: inspect {log_path}\n{evidence[-4000:]}")
            if mode == "graphics":
                # New process: loading the persisted explicit choice must beat detection.
                reload_env = dict(env, CAPTURE_MENU_MODAL="settings")
                reload_log = output / f"{name}-reload.log"
                with reload_log.open("w") as log:
                    reloaded = subprocess.run([
                        "xvfb-run", "-a", "-s", f"-screen 0 {size}x24", godot,
                        "--path", str(root), "--rendering-driver", "opengl3",
                        "--resolution", size, "--script", "tools/capture_menu.gd",
                    ], cwd=root, env=reload_env, stdout=log, stderr=subprocess.STDOUT, timeout=180)
                saved_evidence = reload_log.read_text()
                if reloaded.returncode or any(marker not in saved_evidence for marker in
                        ("GRAPHICS_PROFILE selected=Quality resolved=Quality scale3d=1.00",
                         "MENU_LAYOUT_OK", "MENU_BACK_OK")):
                    raise SystemExit(f"FAIL {name}: persisted preference not restored; inspect {reload_log}")
            if mode == "launch":
                definition, site = catalog[vehicle]
                expected = definition["name"] + " at " + site + ";"
                if expected not in evidence or "profile=manual, mission=sandbox" not in evidence:
                    raise SystemExit(f"FAIL {name}: wrong vehicle, pad or profile; inspect {log_path}")
            if mode == "missionlaunch" and (
                "at cape_canaveral_lc5; profile=mercury-redstone3-suborbital, mission=mission-freedom7-1961" not in evidence
            ):
                raise SystemExit(f"FAIL {name}: campaign mission intent was not preserved")
            if args.flight_hud:
                state = json.loads(next(line.removeprefix("FLIGHT_INSTRUMENTS ") for line in evidence.splitlines()
                                        if line.startswith("FLIGHT_INSTRUMENTS ")))
                expected_boards = {
                    "starship_flight12_v3_2026": (33, 6, 2), "starship_flight7_block2_2025": (33, 6, 2),
                    "falcon9_block5_standard_2025": (9, 1, 2), "falcon9_block5_extended_2025": (9, 1, 2),
                    "newglenn_7x2_public_2026": (7, 2, 2), "mercury_redstone3_freedom7_1961": (1, 1, 2),
                    "mercury_atlas6_friendship7_1962": (3, 1, 2), "gemini8_titan2_1966": (2, 1, 2),
                    "apollo8_saturn5_as503_1968": (5, 5, 4), "apollo11_saturn5_as506_1969": (5, 5, 4),
                }
                actual = (len(state["left_ids"]), len(state["right_ids"]), state["stage_count"])
                ids = state["left_ids"] + state["right_ids"]
                if actual != expected_boards[name] or len(ids) != len(set(ids)) or not state["vessel_id"]:
                    raise SystemExit(f"FAIL {name}: wrong live engine identities: {state}")
                staged = [json.loads(line.removeprefix("FLIGHT_STAGE_INSTRUMENTS ")) for line in evidence.splitlines()
                          if line.startswith("FLIGHT_STAGE_INSTRUMENTS ")]
                current_counts = {
                    "starship_flight12_v3_2026": [33], "starship_flight7_block2_2025": [33],
                    "newglenn_7x2_public_2026": [2], "apollo8_saturn5_as503_1968": [5, 1, 1],
                    "apollo11_saturn5_as506_1969": [5, 1, 1],
                }.get(name, [1])
                if len(staged) != len(current_counts) or [len(s["left_ids"]) for s in staged] != current_counts:
                    raise SystemExit(f"FAIL {name}: staging did not update the live boards: {staged}")
            print(f"PASS {name}", flush=True)
    print(f"Menu checks passed. Captures: {output}")


if __name__ == "__main__":
    main()
