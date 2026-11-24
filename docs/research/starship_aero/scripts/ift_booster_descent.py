"""Super Heavy unpowered descent from the IFT-4/5/6 full-flight 1 Hz extracts.

Writes data/ift_booster_descent.json: apogee, peak descent speed, and at each displayed
altitude level from 40 to 1 km the speed, the deceleration over +/-2 s, and the implied
ballistic ratio m/(C_D S) = rho v^2 / (2 (g - dv/dt)).

Assumptions: near-vertical fall (the boostback leaves a horizontal component, so the
gravity term is over-counted by up to about 1 - sin(gamma); about +/-20 % at the lowest levels);
total displayed speed; whole-km altitude taken at k + 0.5 km; U.S. Standard Atmosphere 1976.
Usage: python3 -I scripts/ift_booster_descent.py
"""

import csv
import json
import math
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
LEVELS = [40, 30, 25, 20, 15, 12, 10, 8, 6, 5, 4, 3, 2, 1]


def density(h):
    if h < 11000.0:
        t = 288.15 - 0.0065 * h
        p = 101325.0 * (t / 288.15) ** 5.25588
    else:
        t = 216.65
        p = 22632.1 * math.exp(-9.80665 * (h - 11000.0) / (287.053 * t))
    return p / (287.053 * t)


def analyse(path):
    rows = []
    with open(path) as handle:
        for r in csv.DictReader(handle):
            if r["booster_altitude_km"] and r["booster_speed_kmh"]:
                rows.append((float(r["video_time_s"]), float(r["booster_altitude_km"]), float(r["booster_speed_kmh"])))
    apogee = max((x for x in rows if x[0] < 300), key=lambda x: x[1])
    descent = [x for x in rows if x[0] > apogee[0]]
    fastest = max(descent, key=lambda x: x[2])
    levels = []
    for level in LEVELS:
        hit = next((x for x in descent if x[1] <= level), None)
        if hit is None:
            continue
        window = [x for x in descent if abs(x[0] - hit[0]) <= 2.0]
        dvdt = (window[-1][2] - window[0][2]) / 3.6 / (window[-1][0] - window[0][0])
        speed = hit[2] / 3.6
        drag_per_mass = 9.80665 - dvdt
        ratio = 0.5 * density((level + 0.5) * 1000.0) * speed * speed / drag_per_mass if drag_per_mass > 0.1 else None
        levels.append({"altitude_km": level, "video_time_s": hit[0], "speed_kmh": hit[2],
                       "dv_dt_mps2": round(dvdt, 1),
                       "ballistic_ratio_kg_m2": None if ratio is None else round(ratio)})
    band = [x["ballistic_ratio_kg_m2"] for x in levels if 2 <= x["altitude_km"] <= 20 and x["ballistic_ratio_kg_m2"]]
    return {"apogee": {"video_time_s": apogee[0], "altitude_km": apogee[1]},
            "peak_descent": {"video_time_s": fastest[0], "altitude_km": fastest[1], "speed_kmh": fastest[2]},
            "peak_deceleration_mps2": -min(x["dv_dt_mps2"] for x in levels),
            "ballistic_ratio_2_to_20_km": {"min": min(band), "max": max(band), "mean": round(sum(band) / len(band))},
            "levels": levels}


def main():
    out = {"schema_version": 1,
           "subject": "Super Heavy unpowered descent, IFT-4 (water), IFT-5 (tower catch), IFT-6 (water)",
           "source": "SofieBrink/StarshipTelemetryExtractor (MIT) webcast OCR, reduced in data/ift_terminal_descent/",
           "generated_by": "scripts/ift_booster_descent.py",
           "confidence": "secondary",
           "flights": {}}
    for n in (4, 5, 6):
        out["flights"][f"IFT{n}"] = analyse(ROOT / "data" / "ift_terminal_descent" / f"ift{n}_full_flight_1hz.csv")
    out["interpretation"] = ("With the about 250 t catch mass Musk quoted for Flight 5 (spoken, an estimate), a ratio of "
                             "2,200-2,800 kg/m2 implies a drag area of about 90-115 m2, C_D about 1.4-1.8 on the 63.6 m2 "
                             "base area, for the engine-first booster with its grid fins.")
    (ROOT / "data" / "ift_booster_descent.json").write_text(json.dumps(out, indent=2) + "\n")
    for k, v in out["flights"].items():
        print(k, v["ballistic_ratio_2_to_20_km"], "peak decel", v["peak_deceleration_mps2"])


if __name__ == "__main__":
    main()
