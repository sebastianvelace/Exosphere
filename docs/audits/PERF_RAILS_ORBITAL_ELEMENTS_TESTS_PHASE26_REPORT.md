# OrbitalElements round-trip coverage — Phase 26 / N13

## Scope

This change adds permanent tests only. Runtime code, scheduler code, benchmarks,
the visual harness, and `project.godot` are intentionally outside ownership.

The tests cover `OrbitalElements.FromStateVector` followed by
`GetStateAtTime` at the same epoch. Inputs are deterministic Cartesian states
generated from classical conic elements with an independent perifocal-to-inertial
rotation in the test file.

## Matrix

- Four non-circular equatorial retrograde states at true anomalies `0.20`,
  `1.70`, `3.30`, and `5.40` rad. This exercises all orbital quadrants and the
  exact `i = π` singular convention.
- One circular equatorial retrograde state.
- One equatorial prograde state, proving the retrograde convention is not used
  when `h.Z > 0`.
- One slightly inclined retrograde state (`i = π - 1e-4`, non-zero node), proving
  the singular convention is not used outside the normalized node singularity.
- One inclined prograde state using the general ascending-node/periapsis path.

Every case checks finite element values, non-radial classification, and position
and velocity reconstruction at the source epoch.

## Tolerances

Vector comparisons use component-wise mixed tolerances:

- position: absolute `1e-5 m`, relative `2e-12`;
- velocity: absolute `1e-8 m/s`, relative `2e-12`.

Element-angle controls use absolute angular tolerance `1e-12 rad`, with periodic
angle comparison. The near-singular control is deliberately `1e-4 rad` away from
`π`, which is eight orders of magnitude outside the normalized node threshold
`1e-12` while remaining a useful retrograde boundary case. The test also verifies
the exact retrograde inclination and that the near-singular retrograde control
preserves a non-zero ascending node.

## Validation record

Commands executed by N13:

```text
dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --filter FullyQualifiedName~OrbitalElementsRoundTripTests
dotnet build ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore
git diff --check
```

Exact results:

- Focused xUnit: `8 total, 8 passed, 0 failed`.
- Build: `Build succeeded; 0 Warning(s), 0 Error(s)`.
- `git diff --check`: PASS, no diagnostics.
- No commit was created.

The first exploratory run used `π - 1e-6` for the near-singular control and
exposed a `7.84e-5 m` Z-position reconstruction error caused by the ill-conditioned
angle representation. The permanent control uses `π - 1e-4`, remains far outside
the `1e-12` normalized-node singular threshold, and passes the stated state
tolerances without weakening the runtime or modifying production code.
