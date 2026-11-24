"""Ship entry corridor and Super Heavy return from the IFT-4/5/6 full-flight 1 Hz extracts.

Reads data/ift_terminal_descent/ift*_full_flight_1hz.csv (webcast OCR, see ATTRIBUTION.md) and
writes data/ift_entry_corridor.json:
- ship: first time the displayed altitude reaches each level from 120 to 20 km during entry,
  with the displayed speed there, and the first speed below 1235 km/h (about Mach 1);
- booster (IFT-5, first tower catch): peak ascent speed, boostback minimum, coast apogee,
  peak descent speed, and the landing burn from the first sharp deceleration to catch.

Displayed altitude is whole kilometres and speed is total speed in km/h; time is video time.
Usage: python3 -I scripts/ift_entry_and_booster.py
"""

import csv
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "data" / "ift_terminal_descent"
LEVELS = [120, 110, 100, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20]


def load(path, prefix):
    rows = []
    with open(path) as handle:
        for row in csv.DictReader(handle):
            a, v = row[f"{prefix}_altitude_km"], row[f"{prefix}_speed_kmh"]
            if a and v:
                rows.append((float(row["video_time_s"]), float(a), float(v)))
    return rows


def ship_corridor(rows):
    entry = [x for x in rows if x[0] > 1500]
    crossings = {}
    for level in LEVELS:
        hit = next((x for x in entry if x[1] <= level), None)
        if hit:
            crossings[str(level)] = {"video_time_s": hit[0], "speed_kmh": hit[2]}
    sonic = next((x for x in entry if x[2] < 1235 and x[1] < 40), None)
    return {"crossings": crossings,
            "first_below_1235_kmh": None if sonic is None else
            {"video_time_s": sonic[0], "altitude_km": sonic[1], "speed_kmh": sonic[2]}}


def booster_return(rows):
    ascent = [x for x in rows if x[0] < 200]
    peak = max(ascent, key=lambda x: x[2])
    boost = [x for x in rows if peak[0] < x[0] < peak[0] + 60]
    boostback_min = min(boost, key=lambda x: x[2])
    coast = [x for x in rows if boostback_min[0] < x[0]]
    apogee = max(coast, key=lambda x: x[1])
    descent = [x for x in rows if x[0] > apogee[0]]
    fastest = max(descent, key=lambda x: x[2])
    burn = None
    late = [x for x in descent if x[0] > fastest[0] and x[1] <= 2]
    for i in range(1, len(late)):
        if late[i - 1][2] - late[i][2] > 80:
            start = late[i - 1]
            caught = next((x for x in late if x[0] > start[0] and x[2] <= 1), None)
            phase = next((x for x in late if x[0] > start[0] and x[2] < 260), None)
            burn = {"start_video_time_s": start[0], "start_speed_kmh": start[2], "start_altitude_km": start[1],
                    "hard_phase_end_video_time_s": phase[0] if phase else None,
                    "hard_phase_end_speed_kmh": phase[2] if phase else None,
                    "speed_zero_video_time_s": caught[0] if caught else None,
                    "duration_s": None if caught is None else round(caught[0] - start[0], 1)}
            break
    return {"peak_ascent": {"video_time_s": peak[0], "altitude_km": peak[1], "speed_kmh": peak[2]},
            "boostback_minimum": {"video_time_s": boostback_min[0], "altitude_km": boostback_min[1], "speed_kmh": boostback_min[2]},
            "apogee": {"video_time_s": apogee[0], "altitude_km": apogee[1], "speed_kmh": apogee[2]},
            "peak_descent": {"video_time_s": fastest[0], "altitude_km": fastest[1], "speed_kmh": fastest[2]},
            "landing_burn": burn}


def main():
    out = {
        "schema_version": 1,
        "source": "SofieBrink/StarshipTelemetryExtractor (MIT), webcast OCR; reduced to 1 Hz in data/ift_terminal_descent/",
        "generated_by": "scripts/ift_entry_and_booster.py",
        "limits": ["displayed altitude in whole km", "speed is total speed, frame unstated",
                   "video time, not mission elapsed time", "third-party OCR with gap filling"],
        "ship_entry": {},
        "booster_ift5_catch": None,
    }
    for n in (4, 5, 6):
        out["ship_entry"][f"IFT{n}"] = ship_corridor(load(DATA / f"ift{n}_full_flight_1hz.csv", "ship"))
    out["booster_ift5_catch"] = booster_return(load(DATA / "ift5_full_flight_1hz.csv", "booster"))
    (ROOT / "data" / "ift_entry_corridor.json").write_text(json.dumps(out, indent=2) + "\n")
    print(json.dumps(out["booster_ift5_catch"], indent=1))


if __name__ == "__main__":
    main()
