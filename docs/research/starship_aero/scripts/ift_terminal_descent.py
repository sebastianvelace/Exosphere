"""Terminal belly-flop analysis of the Starship IFT-4/5/6 webcast telemetry extracts.

Reads data/ift_terminal_descent/ift*_ship_last600s_2hz.csv and prints, for each flight:
- the speed at each change of the displayed altitude (k km -> k-1 km) below 12 km;
- the ballistic ratio m/(C_D S) = rho v^2 / (2 g) implied by steady terminal fall;
- the landing-burn duration from the first sharp deceleration to below 30 km/h.

Assumptions (see the dataset JSON for their effect):
- the displayed altitude is whole kilometres; the transition is taken at the half kilometre;
- the displayed speed is the total speed, taken as vertical (the glide is small);
- steady terminal fall (drag = weight) between 12 and 2 km;
- density from the U.S. Standard Atmosphere 1976 troposphere.

Usage: python3 -I scripts/ift_terminal_descent.py [--json]
"""

import csv
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
G = 9.80665


def density(h_m):
    temperature = 288.15 - 0.0065 * h_m
    pressure = 101325.0 * (temperature / 288.15) ** 5.25588
    return pressure / (287.053 * temperature)


def analyse(path):
    rows = []
    with open(path) as handle:
        reader = csv.reader(handle)
        next(reader)
        for t, a, v in reader:
            rows.append((float(t), float(a), float(v)))
    transitions = []
    for (t0, a0, v0), (t1, a1, v1) in zip(rows, rows[1:]):
        if a1 == a0 - 1 and a1 <= 11:
            h = (a1 + 0.5) * 1000.0
            speed = v1 / 3.6
            transitions.append({
                "video_time_s": t1,
                "altitude_from_km": a0,
                "altitude_to_km": a1,
                "speed_kmh": v1,
                "ballistic_ratio_kg_m2": round(density(h) * speed * speed / (2 * G)),
            })
    burn = None
    low = [(t, v) for t, a, v in rows if a <= 1]
    for i, (t0, v0) in enumerate(low):
        window = [v for t, v in low if t0 < t <= t0 + 2.0]
        if window and v0 > 200 and v0 - min(window) > 30:
            stop = next((t for t, v in low if t > t0 and v < 30), None)
            burn = {"start_video_time_s": t0, "start_speed_kmh": v0,
                    "below_30kmh_video_time_s": stop,
                    "duration_s": None if stop is None else round(stop - t0, 1)}
            break
    steady = [x["ballistic_ratio_kg_m2"] for x in transitions if x["altitude_to_km"] >= 2]
    return {"transitions": transitions, "landing_burn": burn,
            "steady_2_to_12_km": {"min": min(steady), "max": max(steady),
                                  "mean": round(sum(steady) / len(steady))} if steady else None}


def main():
    results = {}
    for path in sorted((ROOT / "data" / "ift_terminal_descent").glob("ift*_ship_last600s_2hz.csv")):
        results[path.name.split("_")[0].upper()] = analyse(path)
    if "--json" in sys.argv:
        print(json.dumps(results, indent=2))
        return
    for flight, r in results.items():
        print(f"== {flight}: steady m/(C_D S) {r['steady_2_to_12_km']}, burn {r['landing_burn']}")
        for x in r["transitions"]:
            print(f"   {x['altitude_from_km']:.0f}->{x['altitude_to_km']:.0f} km  "
                  f"{x['speed_kmh']:.0f} km/h  m/(CdS)={x['ballistic_ratio_kg_m2']} kg/m2")


if __name__ == "__main__":
    main()
