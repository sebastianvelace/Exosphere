# Orbital return corridor audit — 2026-09-16

## Scope

This audit covers the normal orbital return path from the deterministic 1,200 km setup to
the Starbase atmospheric entry corridor. It does not change the physical catch solver or
its contact gates.

## Finding

The earlier seed was a valid inertial orbit but entered the atmosphere several degrees
west of the rotating Starbase track. A fixed catch-radius change would have hidden that
error. The current path instead keeps the physical cradle at the real launch site and
adds two explicit setup parameters:

- a small latitude bias for the measured entry footprint;
- a positive longitudinal lead for the inertial transfer plane.

During entry, `EntryCorridorGuidance` projects the target offset using vehicle and cradle
surface-relative velocities. This gives the lift plane a future corridor direction while
leaving drag, lift, attitude, propulsion, and contact resolution in their existing models.

## Evidence

The compatibility framebuffer run used:

```text
OUT_DIR=/tmp/exo_orbital_reentry_final_current
run_id=final_current
orbitalReturnExpectedSeconds=3300
orbitalReturnLatitudeBiasDegrees=-1.00
orbitalReturnLongitudeLeadDegrees=30.00
```

It produced real orbit and entry captures and reached the entry state with finite thermal
and flight telemetry. The vehicle crossed the Starbase longitude corridor, but the
remaining lateral momentum opened the miss again below the entry interface. The run was
therefore classified as partial and is not accepted as `ORBITAL_REENTRY_OK` or a physical
`CAUGHT` result.

## Validation

- `dotnet build ExosphereSimulation/ExosphereSimulation.csproj --no-restore --nologo -v quiet`
- `dotnet build Exosphere.csproj --no-restore --nologo -v quiet`
- `dotnet test ExosphereSimulation.Tests/ExosphereSimulation.Tests.csproj --no-restore --nologo` — 786 passed
- gameplay, visual-playtest, renderer, and reentry-plasma contract tests — passed
- `bash -n tools/visual_playtest.sh` and `git diff --check` — passed

## Next gate

The next implementation step is a measured atmospheric-corridor improvement, such as a
rate-limited or phase-aware lateral guidance law, followed by a complete framebuffer run.
The strict physical catch gates must remain unchanged until that run records a real
two-pin contact.
