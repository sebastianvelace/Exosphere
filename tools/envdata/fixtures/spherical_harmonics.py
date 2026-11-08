#!/usr/bin/env python3
"""Generate the spherical-harmonic gravity oracle (environment plan E3.2).

    python3 tools/envdata/fixtures/spherical_harmonics.py

A method-independent reference: the potential is summed directly from 4pi-normalized associated
Legendre functions (stable forward-column recursion) at 40 significant digits with mpmath, and
the acceleration is its gradient by central differences in that precision (truncation ~1e-20
relative). Coefficients are deterministic pseudo-random values with a Kaula-like 1e-5/n^2
decay, so every (n, m) term is exercised; two fields (degree 12 and 60) at 100 points each, from
just above the reference sphere to 20 radii, poles included.
"""
import json
import pathlib
import random

import mpmath as mp

mp.mp.dps = 40
ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "ExosphereSimulation.Tests" / "Fixtures" / "environment" / "spherical_harmonics.json"
GM, R = mp.mpf("4.9028001224453e12"), mp.mpf("1738000.0")


def coefficients(degree, seed):
    rng = random.Random(seed)
    c, s = {}, {}
    for n in range(degree + 1):
        for m in range(n + 1):
            scale = 1.0 if n == 0 else 1e-5 / max(n, 1) ** 2
            c[(n, m)] = 1.0 if n == 0 else (0.0 if n == 1 else rng.uniform(-1, 1) * scale)
            s[(n, m)] = 0.0 if (m == 0 or n == 1) else rng.uniform(-1, 1) * scale
    return c, s


def legendre(degree, x):
    """4pi-normalized P̄nm(x) for x = sin(latitude)."""
    p = {}
    u = mp.sqrt(1 - x * x)
    p[(0, 0)] = mp.mpf(1)
    for m in range(1, degree + 1):
        k = mp.sqrt(mp.mpf(2 * m + 1) / (2 * m)) if m > 1 else mp.sqrt(3)
        p[(m, m)] = k * u * p[(m - 1, m - 1)]
    for m in range(0, degree + 1):
        if m + 1 <= degree:
            p[(m + 1, m)] = mp.sqrt(2 * m + 3) * x * p[(m, m)]
        for n in range(m + 2, degree + 1):
            a = mp.sqrt(mp.mpf((2 * n - 1) * (2 * n + 1)) / ((n - m) * (n + m)))
            b = mp.sqrt(mp.mpf((2 * n + 1) * (n + m - 1) * (n - m - 1)) / ((n - m) * (n + m) * (2 * n - 3)))
            p[(n, m)] = a * x * p[(n - 1, m)] - b * p[(n - 2, m)]
    return p


def potential(c, s, degree, x, y, z):
    r = mp.sqrt(x * x + y * y + z * z)
    lam = mp.atan2(y, x)
    p = legendre(degree, z / r)
    total = mp.mpf(0)
    for n in range(degree + 1):
        ratio = (R / r) ** n
        for m in range(n + 1):
            total += ratio * p[(n, m)] * (c[(n, m)] * mp.cos(m * lam) + s[(n, m)] * mp.sin(m * lam))
    return GM / r * total


def main():
    rng = random.Random(1200)
    fields = []
    for degree, seed in ((12, 12), (60, 60)):
        c, s = coefficients(degree, seed)
        points = []
        for i in range(100):
            radius = 1.0005 if i < 30 else rng.uniform(1.001, 20.0)
            if i < 4:
                direction = [(0, 0, 1), (0, 0, -1), (1e-9, 0, 1), (1, 0, 0)][i]
            else:
                direction = [rng.gauss(0, 1) for _ in range(3)]
            norm = sum(d * d for d in direction) ** 0.5
            pos = [mp.mpf(d / norm) * R * mp.mpf(radius) for d in direction]
            h = mp.mpf("1e-6")
            u0 = potential(c, s, degree, *pos)
            grad = []
            for axis in range(3):
                plus = list(pos); minus = list(pos)
                plus[axis] += h; minus[axis] -= h
                grad.append((potential(c, s, degree, *plus) - potential(c, s, degree, *minus)) / (2 * h))
            points.append({"position_m": [float(v) for v in pos], "potential": float(u0),
                           "acceleration_mps2": [float(g) for g in grad]})
        n_terms = (degree + 1) * (degree + 2) // 2
        flat_c = [0.0] * n_terms; flat_s = [0.0] * n_terms
        for (n, m), v in c.items(): flat_c[n * (n + 1) // 2 + m] = v
        for (n, m), v in s.items(): flat_s[n * (n + 1) // 2 + m] = v
        fields.append({"degree": degree, "gm": float(GM), "radius": float(R), "c": flat_c, "s": flat_s,
                       "points": points})
        print(f"degree {degree}: {len(points)} points")
    OUT.write_text(json.dumps({"generator": "tools/envdata/fixtures/spherical_harmonics.py",
                               "oracle": f"mpmath {mp.__version__}, 40 digits, direct Legendre sum, central differences",
                               "fields": fields}) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
